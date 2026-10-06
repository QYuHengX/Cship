using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
namespace Cship.Core;

/// <summary>
/// JSON 键值存储基类（settings.json / state.json 共用）：
/// 扁平点号键 + 可选防抖落盘 + 原子写（*.tmp + File.Replace）。
/// </summary>
public abstract class JsonKeyValueStore : IDisposable
{
    protected readonly object Gate = new();
    readonly string _path;
    readonly int _debounceMs;
    readonly Dictionary<string, JsonElement> _data = new();
    System.Threading.Timer? _flushTimer;

    protected JsonKeyValueStore(string path, int debounceMs,
        IEnumerable<KeyValuePair<string, JsonElement>>? seedEntries = null)
    {
        _path = path;
        _debounceMs = debounceMs;
        if (seedEntries != null)
        {
            foreach (var kv in seedEntries)
                _data[kv.Key] = kv.Value; // 先种默认，文件值随后覆盖 → 缺键补默认
        }
        Load();
    }

    /// <summary>本次加载是否因文件损坏而走了默认值（07 §6：调用方据此气泡告知）。</summary>
    public bool LoadFailed { get; private set; }

    /// <summary>损坏文件的备份路径（改名到 *.bak 后保留；未发生损坏时为 null）。</summary>
    public string? CorruptBackupPath { get; private set; }

    void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            foreach (var prop in doc.RootElement.EnumerateObject())
                _data[prop.Name] = prop.Value.Clone();
        }
        catch (Exception ex)
        {
            // 配置损坏容错（07 §6）：原文件改名 .bak 保留（不静默覆盖用户数据），
            // 内存里只剩种子默认值 → 随后的 Flush 会重建一份合法的默认文件。
            Logger.Warn($"解析 {_path} 失败，相关键走默认/缺省：{ex.Message}");
            LoadFailed = true;
            try
            {
                string bak = _path + ".bak";
                File.Copy(_path, bak, overwrite: true);
                CorruptBackupPath = bak;
                Logger.Warn($"损坏的配置已备份：{bak}（随后重建默认）");
            }
            catch (Exception bex)
            {
                Logger.Warn($"备份损坏配置失败（继续重建默认）：{bex.Message}");
            }
        }
    }

    /// <summary>缺键时返回 ValueKind=Undefined 的空元素，调用方按类型回退。</summary>
    protected JsonElement GetRaw(string key)
    {
        lock (Gate)
        {
            return _data.TryGetValue(key, out var v) ? v : default;
        }
    }

    protected void SetRaw(string key, JsonElement value)
    {
        lock (Gate)
        {
            _data[key] = value.Clone();
            ScheduleFlush();
        }
    }

    public bool GetBool(string key, bool fallback = false)
    {
        var e = GetRaw(key);
        return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : fallback;
    }

    public int GetInt(string key, int fallback = 0)
    {
        var e = GetRaw(key);
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i : fallback;
    }

    public double GetDouble(string key, double fallback = 0)
    {
        var e = GetRaw(key);
        return e.ValueKind == JsonValueKind.Number ? e.GetDouble() : fallback;
    }

    public string GetString(string key, string fallback = "")
    {
        var e = GetRaw(key);
        return e.ValueKind == JsonValueKind.String ? e.GetString() ?? fallback : fallback;
    }

    // virtual：SettingsStore override 后统一走 Set(key,value)（含 SettingsChanged 广播），
    // 否则快捷写法会绕过广播（2026-09-25 步骤04 实测踩坑：SetBool 写 catShow* 后 UI 不刷新）
    public virtual void SetBool(string key, bool value) => SetRaw(key, JsonSerializer.SerializeToElement(value));
    public virtual void SetInt(string key, int value) => SetRaw(key, JsonSerializer.SerializeToElement(value));
    public virtual void SetDouble(string key, double value) => SetRaw(key, JsonSerializer.SerializeToElement(value));
    public virtual void SetString(string key, string value) => SetRaw(key, JsonSerializer.SerializeToElement(value));

    void ScheduleFlush()
    {
        if (_debounceMs <= 0)
        {
            Flush();
            return;
        }
        lock (Gate)
        {
            if (_flushTimer == null)
                _flushTimer = new System.Threading.Timer(_ => Flush(), null, _debounceMs, Timeout.Infinite);
            else
                _flushTimer.Change(_debounceMs, Timeout.Infinite);
        }
    }

    /// <summary>立即落盘（防抖到点 / 退出前兜底都走这里）。</summary>
    public void Flush()
    {
        string payload;
        lock (Gate)
            payload = BuildJson();
        try
        {
            AtomicWrite(_path, payload);
        }
        catch (Exception ex)
        {
            Logger.Warn($"写入 {_path} 失败：{ex.Message}");
        }
    }

    /// <summary>全部键值快照（供子类排序/导出）。</summary>
    protected KeyValuePair<string, JsonElement>[] RawEntries()
    {
        lock (Gate)
            return _data.ToArray();
    }

    /// <summary>子类可重写以控制键序（默认按名排序）。</summary>
    protected virtual IEnumerable<KeyValuePair<string, JsonElement>> OrderedEntries()
        => RawEntries().OrderBy(kv => kv.Key, StringComparer.Ordinal);

    string BuildJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in OrderedEntries())
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>原子写：先写 *.tmp 再 File.Replace，断电/崩溃不会留下半截 JSON。</summary>
    static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    public void Dispose()
    {
        lock (Gate)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
        }
        Flush();
    }
}
