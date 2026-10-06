using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
namespace Cship.Core;

/// <summary>
/// 配置导出/导入（步骤 05 §4，原文 L60-61；2026-10-05 适配当前设置项生态）：
/// 文本格式 = `key=value`，嵌套值单行 JSON；UTF-8；# 开头为注释。
///
/// **范围**：settings.json 全键（§8 表序）+ <see cref="ConfigStateKeys"/> 各占一行
/// （行内以 <c>state.</c> 前缀与 settings 键区分）；**位置尺寸类 state 键
/// （dock.pos / board.size.* / board.pos.*）与心跳标记刻意不含**——机器与屏幕相关，换机导入无意义。
/// 自定义图片不含本体（仅相对路径，缺失回退默认）。
///
/// **键集合与导出对称**：可导入键 = 00 §8 默认表 ∪ **当前 settings.json 实际持有的键**
/// ∪ 纳入配置的 state 键。旧实现只认默认表，表外键（删除功能的残留键、后续步骤新增的键）
/// **导出得出去、导不回来**，摘要恒报"忽略 N 项"；步骤 06 起主题/材质等新键免改本文件即随生态进出。
///
/// **state 键名映射（2026-10-05 修）**：state.json 里是**裸键名**（`categories` / `iconOrder` /
/// `removedItems` / `virtualItems`，见 00 §8 state 表与 StateStore 各存取方法），
/// 而配置文件按 05 §4 的口径写成 `state.categories=`。旧实现直接拿 `state.categories` 去读写
/// state.json——导出时读不到（静默跳过，导出文件里从来没有这两行）、导入时写进一个没有任何
/// 调用方读取的假键，"收纳板布局"那一半从未真正生效。现在统一在 <see cref="FileKey"/> /
/// <see cref="StoreKey"/> 两处做映射。
///
/// **枚举值白名单（2026-10-06 适配材质改动）**：材质新增"无"档（`personal.material=none`）、
/// 主题删掉"透明"档，都是"合法取值集合刚刚变过"的键。这类键在导入侧多一道校验——
/// 值不在白名单里就按"非法值"跳过并计数（摘要里看得见），而不是热应用一个谁都不认识的档
/// （那样各处只会各自回退默认，观感随机且无从排查）。键集合仍是自动适配的（见下），
/// 本表只负责"值域"，新增材质档时在 <see cref="EnumValues"/> 补一项即可。
///
/// **形状/区间校验（2026-10-06 适配"各层不透明度"口径）**：除枚举外还有一类键的值是**结构**
/// ——三层不透明度数组、三层图片数组、背景图对象、百分比数值。它们同样"值域封闭"：
/// 结构不对时各处读取只是各自兜底（静默），导入侧若照单全收，用户拿到的是一个"看起来导入了、
/// 实际什么都没变"的结果。故 <see cref="Coerce"/> 逐键做**形状校验 + 区间夹回**：
/// 结构非法 → 跳过并点名（`（值非法）`）；仅数值越界 → 夹回合法区间后放行（不丢用户配置）。
///
/// 导入：未知键忽略、非法值跳过并计数（键名一并回传供摘要与日志展示）；确认后全部热应用
/// （settings 经 Set 广播即时生效；state 键由 BoardWindow.ApplyImportedConfig 重载）。
/// </summary>
public static class ConfigPort
{
    const string StatePrefix = "state.";

    /// <summary>纳入配置的 state.json **裸键名**（顺序即导出行序）。
    /// 与 00 §8 state 表对照：只取"用户可带走"的内容，运行位置/尺寸与心跳标记不入配置。</summary>
    static readonly string[] ConfigStateKeys =
    {
        "categories",   // 分类页数据（含内置项重命名/成员）
        "iconOrder",    // 图标格序
        "removedItems", // 右键"移除"名单
        "virtualItems", // 板内快捷方式（外部文件拖入产生）
    };

    /// <summary>
    /// 取值集合封闭的键 → 合法值（2026-10-06）。只列"值域刚刚变过 / 值域本身很窄"的键：
    /// 材质（新增"无"）、悬浮窗自定义模式、主题（已删"透明"）。
    /// 大小写不敏感（材质 id 在存储里是驼峰，手写配置文件时大小写不一很常见）。
    /// </summary>
    static readonly Dictionary<string, string[]> EnumValues = new(StringComparer.Ordinal)
    {
        ["personal.material"] = new[] { "none", "acrylic", "blur", "liquidGlass" },
        ["personal.dockCustom.material"] = new[] { "acrylic", "blur", "liquidGlass" },
        ["personal.dockCustom.mode"] = new[] { "preset", "custom" },
        ["theme.mode"] = new[] { "light", "dark", "auto", "system" },
    };

    /// <summary>
    /// 结构化取值的形状校验 + 区间夹回（2026-10-06）。返回 false = 结构非法（跳过并点名）；
    /// 返回 true 时 <paramref name="normalized"/> 为可安全热应用的值（可能被夹回合法区间）。
    /// 未列入的键原样放行（键集合仍是自动适配的，本方法只补"值域"）。
    /// </summary>
    static bool Coerce(string key, JsonElement value, out JsonElement normalized)
    {
        normalized = value;
        switch (key)
        {
            // 百分比数值：越界夹回 0~100（不丢配置），非数字=非法
            case "personal.boardOpacity":
            case "personal.boardBgOpacity":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double pct)) return false;
                normalized = JsonSerializer.SerializeToElement(Math.Clamp(pct, 0, 100));
                return true;

            // 三层各自不透明度（[上,中,下]）：必须是 3 个数字，逐项夹回 0~100
            case "personal.dockCustom.opacity":
                if (!TryNumbers(value, 3, out double[] ops)) return false;
                normalized = JsonSerializer.SerializeToElement(ops);
                return true;

            // 三层图片相对名（[上,中,下]）：必须是 3 个字符串（缺失项补空）
            case "personal.dockCustom.images":
                if (!TryStrings(value, 3, out string[] names)) return false;
                normalized = JsonSerializer.SerializeToElement(names);
                return true;

            // 自定义背景图：null（未设置）或 { image: "<相对名>", crop?: {...} }
            case "personal.boardBg":
                if (value.ValueKind == JsonValueKind.Null) return true;
                if (value.ValueKind != JsonValueKind.Object) return false;
                if (!value.TryGetProperty("image", out var img) || img.ValueKind != JsonValueKind.String) return false;
                return true; // crop 缺失/不合法由读取侧容错（无 crop = 不裁剪），此处不强制
        }
        return true;
    }

    /// <summary>取恰好 <paramref name="count"/> 个数字（缺项补 0 不算非法——旧文件可能少写一层）。</summary>
    static bool TryNumbers(JsonElement value, int count, out double[] numbers)
    {
        numbers = new double[count];
        for (int i = 0; i < count; i++) numbers[i] = 100;
        if (value.ValueKind != JsonValueKind.Array) return false;
        int i2 = 0;
        foreach (var v in value.EnumerateArray())
        {
            if (i2 >= count) break; // 多余项忽略（向后兼容未来加层）
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d)) return false;
            numbers[i2++] = Math.Clamp(d, 0, 100);
        }
        return true;
    }

    /// <summary>取恰好 <paramref name="count"/> 个字符串（缺项补空）。</summary>
    static bool TryStrings(JsonElement value, int count, out string[] strings)
    {
        strings = new string[count];
        for (int i = 0; i < count; i++) strings[i] = "";
        if (value.ValueKind != JsonValueKind.Array) return false;
        int i2 = 0;
        foreach (var v in value.EnumerateArray())
        {
            if (i2 >= count) break;
            if (v.ValueKind != JsonValueKind.String) return false;
            strings[i2++] = v.GetString() ?? "";
        }
        return true;
    }

    /// <summary>单条导入解析结果（摘要对话框数据源）。</summary>
    public sealed record Entry(string Key, JsonElement Value);

    /// <summary>解析结果：可应用项 / 忽略条数 / 被忽略的键名（含"值非法"标注，供摘要与日志）。</summary>
    public sealed record ParseResult(List<Entry> Apply, int Ignored, List<string> IgnoredKeys);

    /// <summary>配置文件里的键名（state 键带 <c>state.</c> 前缀，与 05 §4 示例一致）。</summary>
    static string FileKey(string storeKey) => StatePrefix + storeKey;

    /// <summary>配置文件键名 → state.json 裸键名；不是本配置纳入的 state 键则返回 null。</summary>
    static string? StoreKey(string fileKey)
    {
        if (!fileKey.StartsWith(StatePrefix, StringComparison.Ordinal)) return null;
        string bare = fileKey[StatePrefix.Length..];
        return ConfigStateKeys.Contains(bare, StringComparer.Ordinal) ? bare : null;
    }

    /// <summary>导出到指定文件（覆盖写）。</summary>
    public static void Export(string file)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Cship 配置 v1");
        sb.AppendLine($"# 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# 范围: settings 全键 + {string.Join(" / ", ConfigStateKeys.Select(FileKey))}；不含位置尺寸与图片本体");
        foreach (var (key, value) in SettingsStore.Instance.Snapshot())
            sb.Append(key).Append('=').AppendLine(value.GetRawText());
        int written = 0;
        foreach (var storeKey in ConfigStateKeys)
        {
            var e = StateStore.Instance.GetRawValue(storeKey);
            if (e.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
            sb.Append(FileKey(storeKey)).Append('=').AppendLine(e.GetRawText());
            written++;
        }
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        Logger.Info($"配置已导出：{file}（收纳板布局 {written}/{ConfigStateKeys.Length} 项）");
    }

    /// <summary>解析导入文本：已知键取值、未知键忽略、非法值跳过并计数。</summary>
    public static ParseResult Parse(string content)
    {
        var apply = new List<Entry>();
        var ignoredKeys = new List<string>();
        int ignored = 0;
        var known = KnownKeys();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) { ignored++; continue; }
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (!known.Contains(key)) { ignored++; ignoredKeys.Add(key); continue; }
            JsonElement element;
            try
            {
                using var doc = JsonDocument.Parse(value);
                element = doc.RootElement.Clone();
            }
            catch
            {
                ignored++; // 非法值跳过并计数
                ignoredKeys.Add(key + "（值非法）");
                continue;
            }
            if (EnumValues.TryGetValue(key, out var allowed)
                && (element.ValueKind != JsonValueKind.String
                    || !allowed.Contains(element.GetString(), StringComparer.OrdinalIgnoreCase)))
            {
                ignored++; // 取值不在白名单（如已删的主题"透明"档、拼错的材质档）
                ignoredKeys.Add(key + "（值非法）");
                continue;
            }
            if (!Coerce(key, element, out var coerced))
            {
                ignored++; // 结构非法（如三层不透明度写成字符串数组）
                ignoredKeys.Add(key + "（值非法）");
                continue;
            }
            apply.Add(new Entry(key, coerced));
        }
        return new ParseResult(apply, ignored, ignoredKeys);
    }

    /// <summary>
    /// 当前可导入键集合 = §8 默认表 ∪ settings.json 实际持有的键（与导出集合对称）
    /// ∪ 纳入配置的 state 键（带前缀的文件键名）。每次解析现算：导入是低频人工操作，
    /// 缓存反而会在新键写入后过期。
    /// </summary>
    static HashSet<string> KnownKeys()
    {
        var known = new HashSet<string>(SettingsStore.Defaults.Keys, StringComparer.Ordinal);
        foreach (var (key, _) in SettingsStore.Instance.Snapshot())
            known.Add(key);
        foreach (var storeKey in ConfigStateKeys)
            known.Add(FileKey(storeKey));
        return known;
    }

    /// <summary>热应用（摘要确认后调用）：settings 键经 Set 广播即时生效；state 键按裸键名整段写入，由 BoardWindow 重载。</summary>
    public static void Apply(IEnumerable<Entry> entries)
    {
        var list = entries as IList<Entry> ?? entries.ToList();
        int stateCount = 0;
        foreach (var entry in list)
        {
            string? storeKey = StoreKey(entry.Key);
            if (storeKey != null)
            {
                StateStore.Instance.SetRawValue(storeKey, entry.Value);
                stateCount++;
            }
            else
            {
                SettingsStore.Instance.Set(entry.Key, entry.Value);
            }
        }
        Logger.Info($"配置导入热应用：{list.Count} 项（settings {list.Count - stateCount} / 收纳板布局 {stateCount}）");
    }

    /// <summary>差异摘要文案（应用 N 项 / 忽略 M 项[/ 被忽略的键名，最多 6 个]）。</summary>
    public static string Summary(ParseResult parsed, string appliedFmt, string ignoredFmt, string ignoredKeysFmt)
    {
        string text = $"{string.Format(appliedFmt, parsed.Apply.Count)}\n{string.Format(ignoredFmt, parsed.Ignored)}";
        if (parsed.Ignored > 0)
        {
            var names = parsed.IgnoredKeys.Take(6).ToList();
            string list = string.Join("、", names);
            if (parsed.IgnoredKeys.Count > names.Count) list += $" 等 {parsed.IgnoredKeys.Count} 个";
            text += "\n" + string.Format(ignoredKeysFmt, list);
        }
        return text;
    }
}
