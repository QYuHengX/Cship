using System;
using System.Collections.Generic;
using System.Text.Json;
namespace Cship.Core;

/// <summary>
/// state.json（运行状态）：dock.pos / board.size.* / categories / firstRunDone 等，
/// 键名见 00 §8。无默认表，读取时按调用方给的缺省回退。
/// </summary>
public sealed class StateStore : JsonKeyValueStore
{
    static StateStore? _instance;
    public static StateStore Instance => _instance ?? throw new InvalidOperationException("StateStore 未初始化");

    public static void Init(string path) => _instance ??= new StateStore(path);

    // state.json 的 200ms 落盘防抖（性能 · 2026-10-05）：原 debounceMs=0 表示每次 Set*
    // 都在调用线程同步"全文档 JSON 重建 + tmp 写 + File.Replace"——SaveSizeNow 一次写两个键
    // 就是两轮全文档序列化 + 两次原子写（UI 线程）。加防抖后合并为一次落盘；
    // 退出路径（App.OnExit / 强退兜底）仍显式 Flush 兜底，不会丢状态。
    StateStore(string path) : base(path, debounceMs: 200) { }

    /// <summary>结构化键值读取（步骤 05 配置导出用）。</summary>
    public JsonElement GetRawValue(string key) => GetRaw(key);

    /// <summary>结构化键值写入（步骤 05 配置导入用：state.categories / state.iconOrder 整段热替换）。</summary>
    public void SetRawValue(string key, JsonElement value) => SetRaw(key, value);

    /// <summary>读取 [x,y] 物理像素坐标（如 dock.pos）；缺失/形状不对返回 null。</summary>
    public (int X, int Y)? GetPoint2(string key)
    {
        var e = GetRaw(key);
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2) return null;
        var x = e[0];
        var y = e[1];
        if (x.ValueKind != JsonValueKind.Number || y.ValueKind != JsonValueKind.Number) return null;
        return ((int)Math.Round(x.GetDouble()), (int)Math.Round(y.GetDouble()));
    }

    public void SetPoint2(string key, int x, int y)
        => SetRaw(key, JsonSerializer.SerializeToElement(new[] { x, y }));

    /// <summary>
    /// iconOrder（00 §8：{文件名:{r,c}}）读取；缺键/形状不对返回空字典。
    /// </summary>
    public Dictionary<string, (int R, int C)> GetIconOrder(string key = "iconOrder")
    {
        var result = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var e = GetRaw(key);
        if (e.ValueKind != JsonValueKind.Object) return result;
        foreach (var prop in e.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array || prop.Value.GetArrayLength() != 2) continue;
            var r = prop.Value[0];
            var c = prop.Value[1];
            if (r.ValueKind == JsonValueKind.Number && c.ValueKind == JsonValueKind.Number
                && r.TryGetInt32(out int ri) && c.TryGetInt32(out int ci))
                result[prop.Name] = (ri, ci);
        }
        return result;
    }

    public void SetIconOrder(IReadOnlyDictionary<string, (int R, int C)> order, string key = "iconOrder")
    {
        var payload = new Dictionary<string, int[]>(order.Count);
        foreach (var (name, rc) in order)
            payload[name] = new[] { rc.R, rc.C };
        SetRaw(key, JsonSerializer.SerializeToElement(payload));
    }

    /// <summary>
    /// 被用户"移除"（不在收纳板显示）的文件名集合（2026-08-29 用户反馈）。
    /// 按文件名记录（与 iconOrder 同口径）；缺失/形状不对返回空集合。
    /// </summary>
    public HashSet<string> GetRemovedItems(string key = "removedItems")
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var e = GetRaw(key);
        if (e.ValueKind != JsonValueKind.Array) return result;
        foreach (var v in e.EnumerateArray())
            if (v.ValueKind == JsonValueKind.String)
                result.Add(v.GetString() ?? "");
        result.Remove("");
        return result;
    }

    public void SetRemovedItems(IReadOnlyCollection<string> names, string key = "removedItems")
        => SetRaw(key, JsonSerializer.SerializeToElement(names));

    /// <summary>
    /// 仅收纳板存在的"快捷方式"列表（2026-08-29 批次六）：["path"] 或 {"path":..,"name":"显示名"}；
    /// 缺失/形状不对返回空列表。
    /// </summary>
    public List<(string Path, string Name)> GetVirtualItems(string key = "virtualItems")
    {
        var result = new List<(string Path, string Name)>();
        var e = GetRaw(key);
        if (e.ValueKind != JsonValueKind.Array) return result;
        foreach (var v in e.EnumerateArray())
        {
            if (v.ValueKind == JsonValueKind.String)
            {
                var p = v.GetString() ?? "";
                if (p.Length > 0) result.Add((p, ""));
            }
            else if (v.ValueKind == JsonValueKind.Object)
            {
                string p = "", n = "";
                if (v.TryGetProperty("path", out var pe) && pe.ValueKind == JsonValueKind.String)
                    p = pe.GetString() ?? "";
                if (v.TryGetProperty("name", out var ne) && ne.ValueKind == JsonValueKind.String)
                    n = ne.GetString() ?? "";
                if (p.Length > 0) result.Add((p, n));
            }
        }
        return result;
    }

    public void SetVirtualItems(IReadOnlyList<(string Path, string Name)> items, string key = "virtualItems")
    {
        var payload = new List<object?>(items.Count);
        foreach (var (path, name) in items)
        {
            if (string.IsNullOrEmpty(path)) continue;
            payload.Add(string.IsNullOrEmpty(name)
                ? path
                : JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["path"] = path, ["name"] = name }));
        }
        SetRaw(key, JsonSerializer.SerializeToElement(payload));
    }

    /// <summary>
    /// 分类页数据（步骤 04 · 2026-09-25 改版，00 §8）：数组，内置
    /// <c>{"id","builtin":true,"type","name"(可选覆盖),"items":[成员]}</c>、
    /// 自定义 <c>{"id","name","builtin":false,"items":[文件名]}</c>；缺失/形状不对返回空列表。
    /// "全部"为固定首项，不持久化。
    /// </summary>
    public List<Models.CategoryModel> GetCategories(string key = "categories")
    {
        var result = new List<Models.CategoryModel>();
        var e = GetRaw(key);
        if (e.ValueKind != JsonValueKind.Array) return result;
        foreach (var v in e.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Object) continue;
            var c = new Models.CategoryModel();
            if (v.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                c.Id = id.GetString() ?? "";
            if (v.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                c.Name = nm.GetString() ?? "";
            if (v.TryGetProperty("builtin", out var bi) && bi.ValueKind is JsonValueKind.True or JsonValueKind.False)
                c.Builtin = bi.GetBoolean();
            if (v.TryGetProperty("type", out var tp) && tp.ValueKind == JsonValueKind.String)
                c.Type = tp.GetString() ?? "";
            if (v.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Array)
                foreach (var n in it.EnumerateArray())
                    if (n.ValueKind == JsonValueKind.String && n.GetString() is { } s && s.Length > 0)
                        c.Items.Add(s);
            if (c.Id.Length > 0)
                result.Add(c);
        }
        return result;
    }

    public void SetCategories(IReadOnlyList<Models.CategoryModel> cats, string key = "categories")
    {
        var payload = new List<object>(cats.Count);
        foreach (var c in cats)
        {
            if (c.Builtin)
            {
                var entry = new Dictionary<string, object> { ["id"] = c.Id, ["builtin"] = true, ["type"] = c.Type };
                if (c.Name.Length > 0 && c.Name != c.Id)
                    entry["name"] = c.Name; // 重命名覆盖（显示层回退 I18n）
                if (c.Items.Count > 0)
                    entry["items"] = c.Items; // 显式拖入成员（04 §4.3）
                payload.Add(entry);
            }
            else
            {
                payload.Add(new Dictionary<string, object> { ["id"] = c.Id, ["name"] = c.Name, ["builtin"] = false, ["items"] = c.Items });
            }
        }
        SetRaw(key, JsonSerializer.SerializeToElement(payload));
    }
}
