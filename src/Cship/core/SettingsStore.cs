using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace Cship.Core;

public sealed record SettingChangedArgs(string Key, JsonElement Value);

/// <summary>
/// settings.json（步骤 1.5）：键名与默认值全部来自 00 §8 键表，缺键补默认；
/// 300ms 防抖落盘；变更经 SettingsChanged(key, value) 全局广播。
/// </summary>
public sealed class SettingsStore : JsonKeyValueStore
{
    static SettingsStore? _instance;
    public static SettingsStore Instance => _instance ?? throw new InvalidOperationException("SettingsStore 未初始化");

    public static void Init(string path) => _instance ??= new SettingsStore(path);

    /// <summary>
    /// 默认值以 JSON 字面量书写，加载时统一 Parse 成 JsonElement（与文件读回同构）。
    /// **2026-10-05 用户要求（步骤 06 附加项）：把"当前设置"落为默认配置**——
    /// 下表按当时 settings.json 的实际取值逐键对齐（theme.mode=light、fps=120、dockSize=58.7、
    /// iconSize=67.1、rowSpacing=0、colSpacing=10.9、dockEdgeRange≈40.24、iconMask=true、
    /// dockPriority=low、dockFollowTheme=false、boardAnim=slideEdge、boardOpacity=91 等），
    /// 保证删除 settings.json / 全新机器首启即得到与当前一致的外观与行为。
    /// 各处 Get* 的"缺键兜底字面量"已由 build\sync_defaults.py 同步为同一组值（单一口径）。
    /// </summary>
    static readonly Dictionary<string, string> DefaultRaw = new()
    {
        ["theme.mode"] = "\"light\"",
        ["general.autostart"] = "true",
        ["general.fps"] = "120",
        ["general.language"] = "\"zh-CN\"",
        ["general.font"] = "\"Microsoft YaHei\"",
        ["general.hideDesktopIcons"] = "false",
        ["display.dockSize"] = "58.7",
        ["display.iconStyle"] = "\"original\"",
        ["display.iconOpacity"] = "100",
        ["display.showLabels"] = "false", // 默认不显示文件名（2026-08-29 用户反馈；00 §8 已同步）
        ["display.hoverLabels"] = "true",
        ["display.labelLines"] = "\"two\"", // 图标下方文件名行数（2026-10-03）：one|two|full（full=3 行文字，2026-10-04 口径）
        ["display.iconSize"] = "67.1", // 2026-08-28 用户反馈默认由 64 改小（00 §8 已同步）
        ["display.rowSpacing"] = "0",
        ["display.colSpacing"] = "10.9",
        ["display.screen"] = "\"auto\"",
        ["advanced.popupDirection"] = "\"auto\"", // auto=按悬浮窗位置推断（2026-08-29 批次六，用户确认）
        ["advanced.dockEdgeRange"] = "40.243902439024396", // 弹出判定范围 0~100%（2026-10-02）
        ["advanced.showApps"] = "true",
        ["advanced.showFiles"] = "true",
        ["advanced.showFolders"] = "true",
        ["advanced.categoryDisabled"] = "false",
        ["advanced.catShowApps"] = "true",
        ["advanced.catShowFiles"] = "true",
        ["advanced.catShowFolders"] = "true",
        ["advanced.iconMask"] = "true",
        ["advanced.lockIconDrag"] = "false",
        ["advanced.dblOpenItems"] = "true",
        ["advanced.dblOpenFolders"] = "true",
        ["advanced.bounceOpen"] = "true",
        ["advanced.runningIndicator"] = "true",
        ["advanced.dockLocked"] = "false",
        ["advanced.dockPriority"] = "\"low\"", // 窗口优先级（2026-10-03 更名：收纳板+设置窗跟随；悬浮窗固定桌面级）
        ["advanced.dockFollowTheme"] = "false", // 悬浮窗跟随主题（2026-10-03 新增）：关=悬浮窗保持亮色观感
        ["advanced.hotkeyToggle"] = "\"Tab\"",     // 开合收纳板快捷键（2026-10-05 需求③，高级页可改）
        ["advanced.hotkeyForce"] = "\"Alt+Tab\"",  // 强制开合收纳板快捷键（2026-10-05 需求③，高级页可改）
        ["personal.boardAnim"] = "\"slideEdge\"",
        ["personal.material"] = "\"none\"", // 收纳板材质默认"无"=主题纯色板（2026-10-06 用户要求：当前取值落为默认）
        ["personal.boardOpacity"] = "91", // 收纳板纯色板底不透明度 0~100%（清单02任务7，2026-10-04）
        ["personal.boardBg"] = "null",
        ["personal.boardBgOpacity"] = "100", // 自定义背景图不透明度 0~100%（清单02任务9，2026-10-04）
        ["personal.dockCustom.mode"] = "\"preset\"",
        ["personal.dockCustom.material"] = "\"blur\"", // 悬浮窗底面材质默认"柔化"（2026-10-06 用户要求：当前取值落为默认）
        // personal.dockCustom.colors 已随颜色盘功能删除（清单02任务8）：残留键无害可留
        ["personal.dockCustom.images"] = "[\"\",\"\",\"\"]", // 三层层图（空=用材质底色）
        ["personal.dockCustom.opacity"] = "[100,100,100]", // 三层各自不透明度 0~100%（清单02任务8，2026-10-04）；2026-10-06 起**预设模式同样按层乘算**
    };

    static IReadOnlyDictionary<string, int>? _order;
    static IReadOnlyDictionary<string, JsonElement>? _defaults;

    /// <summary>00 §8 全部键的默认值（缺键补默认的来源）。</summary>
    public static IReadOnlyDictionary<string, JsonElement> Defaults
        => _defaults ??= DefaultRaw.ToDictionary(
            kv => kv.Key,
            kv => JsonDocument.Parse(kv.Value).RootElement.Clone());

    SettingsStore(string path) : base(path, debounceMs: 300, seedEntries: Defaults)
    {
        // 一次性迁移（2026-08-29 批次六）：旧版默认 "up" 会被持久化进 settings.json，缺键补默认
        // 不会覆盖已存在的键——而方向 UI 尚未存在（05 步），"up" 必为旧默认残留而非用户选择，
        // 统一升为 auto（按悬浮窗位置推断）。
        if (string.Equals(GetString("advanced.popupDirection", "up"), "up", StringComparison.OrdinalIgnoreCase))
            SetRaw("advanced.popupDirection", System.Text.Json.JsonSerializer.SerializeToElement("auto"));
        // 一次性迁移（2026-10-06）：主题"透明"档已按用户要求整体删除，历史值按最接近的暗夜档折算
        // （原透明档=暗底浅字配色），否则未知模式会回落到白皙，观感突变更大。
        if (string.Equals(GetString("theme.mode", "light"), "transparent", StringComparison.OrdinalIgnoreCase))
            SetRaw("theme.mode", System.Text.Json.JsonSerializer.SerializeToElement("dark"));
    }

    /// <summary>设置变更广播（在调用方线程触发；UI 侧自行调度）。</summary>
    public event Action<SettingChangedArgs>? SettingsChanged;

    /// <summary>结构化键值读取（如 personal.boardBg 对象）；缺键返回 ValueKind=Undefined。</summary>
    public JsonElement GetRawValue(string key) => GetRaw(key);

    /// <summary>全部键值快照（步骤 05 配置导出用，按 §8 表序）。</summary>
    public KeyValuePair<string, JsonElement>[] Snapshot() => RawEntries();

    /// <summary>唯一写入口：写盘 + SettingsChanged 广播（键值变化才广播）。
    /// 基类 SetBool/SetInt/SetDouble/SetString 已 override 到此，快捷写法不再绕过广播
    /// （2026-09-25 步骤04 实测踩坑：SetBool 写 catShow* 后 UI 不刷新）。</summary>
    public void Set(string key, JsonElement value)
    {
        bool changed;
        lock (Gate)
        {
            var old = GetRaw(key);
            changed = old.ValueKind == JsonValueKind.Undefined || !JsonEquals(old, value);
            SetRaw(key, value);
        }
        if (changed)
            SettingsChanged?.Invoke(new SettingChangedArgs(key, value));
    }

    public override void SetBool(string key, bool value) => Set(key, JsonSerializer.SerializeToElement(value));
    public override void SetInt(string key, int value) => Set(key, JsonSerializer.SerializeToElement(value));
    public override void SetDouble(string key, double value) => Set(key, JsonSerializer.SerializeToElement(value));
    public override void SetString(string key, string value) => Set(key, JsonSerializer.SerializeToElement(value));

    /// <summary>JsonElement 深比较（.NET 8 无 DeepEquals，按类型+原文比对）。</summary>
    static bool JsonEquals(JsonElement a, JsonElement b)
        => a.ValueKind == b.ValueKind
           && string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);

    /// <summary>
    /// 恢复默认设置（高级页"恢复默认"按钮）：逐键写回 <see cref="DefaultRaw"/> 的默认值，
    /// 每键都经 <see cref="Set"/> 走一次广播 → 主题/材质/字体/快捷键等各窗口联动与
    /// "开机自启动 / 隐藏桌面图标 / 语言"这类系统副作用一并即时生效（副作用收口见 App 的
    /// SettingsChanged 监听与各窗口自己的监听）。返回实际发生变化的键数。
    ///
    /// **只动 settings.json**：分类项、图标格序、移除名单、板内快捷方式（state.json）与窗口位置尺寸
    /// 一律不碰——用户要求"不会动分类项"，而位置尺寸属"这台机器的当前状态"而非"设置项"。
    /// 表外键（历史残留，如已删功能的 personal.dockCustom.colors）保留原值：它已不是设置项，
    /// 静默删除等于替用户丢数据。
    /// </summary>
    public int ResetToDefaults()
    {
        int changed = 0;
        foreach (var (key, value) in Defaults)
        {
            bool differs;
            lock (Gate)
            {
                var old = GetRaw(key);
                differs = old.ValueKind == JsonValueKind.Undefined || !JsonEquals(old, value);
            }
            Set(key, value);
            if (differs) changed++;
        }
        return changed;
    }

    /// <summary>按 00 §8 键表顺序输出，便于人工核对；表外键排最后。</summary>
    protected override IEnumerable<KeyValuePair<string, JsonElement>> OrderedEntries()
    {
        var list = new List<KeyValuePair<string, JsonElement>>();
        foreach (var (key, value) in RawEntries())
            list.Add(new(key, value));
        var order = _order ??= DefaultRaw.Keys.Select((k, i) => (k, i)).ToDictionary(kv => kv.k, kv => kv.i);
        list.Sort((a, b) =>
        {
            int ia = order.TryGetValue(a.Key, out var va) ? va : int.MaxValue;
            int ib = order.TryGetValue(b.Key, out var vb) ? vb : int.MaxValue;
            return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.Key, b.Key);
        });
        return list;
    }
}
