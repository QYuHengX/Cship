using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Cship.Core;

namespace Cship.Ui.Themes;

/// <summary>
/// 主题引擎（步骤 06 §2）：token 换肤的唯一来源。
///
/// 资源结构（§2.1）：<c>ui\themes\Tokens.xaml</c>（尺寸/几何键）+
/// <c>Light.xaml</c> / <c>Dark.xaml</c>（同名配色键，后加载者覆盖）。
/// App 级合并字典在 <see cref="Init"/> 装配；窗口/控件一律经 <see cref="Brush"/> / <see cref="GetColor"/> 取 token
/// （等价于 DynamicResource 的代码侧取用）。
/// **拓展点**：新增一套配色 = 新增一个字典文件 + 在 <see cref="PaletteFile"/> 注册一行，不改任何窗口代码。
///
/// 模式解析（§2.2，2026-10-06 修订：删去"透明"档）：light / dark 直接换字典；
/// auto = 06:00~18:00 亮（<see cref="ThemeAuto"/> 每分钟检查）；
/// system = 读 <c>HKCU\...\AppsUseLightTheme</c>，经 <c>SystemEvents.UserPreferenceChanged</c> **即时跟随**。
/// 存储里若有历史值（如已删除的 <c>transparent</c>）不属于四档，一律按 light 兜底
/// （<see cref="SettingsStore"/> 构造时另做一次"transparent→dark"迁移，见该处说明）。
///
/// 与 <see cref="ThemeResolver"/> 的关系：解析结果（亮/暗）与系统主题缓存仍由 ThemeResolver 持有并对外提供
/// （批次九的 5s TTL + <c>InvalidateSystemTheme()</c> 失效链路原样保留），本引擎负责"字典/画笔/模式"。
/// </summary>
public static class ThemeEngine
{
    /// <summary>合法主题档（2026-10-06：透明档已删，四选一）。</summary>
    static readonly string[] Modes = { "light", "dark", "auto", "system" };

    const string PackBase = "pack://application:,,,/ui/themes/";
    static readonly object Gate = new();
    static readonly Dictionary<string, object?> TokenCache = new(StringComparer.Ordinal);
    static readonly Dictionary<string, ResourceDictionary> Loaded = new(StringComparer.Ordinal);
    static ResourceDictionary? _palette;
    static string _paletteId = "";
    static bool _hooked;

    /// <summary>App 启动时调用一次：装配 App 级合并字典（Tokens + 当前配色）并挂系统偏好监听。</summary>
    public static void Init()
    {
        var app = Application.Current;
        if (app == null) return; // 无 Application（离屏夹具）时 Token 取用自动退回代码兜底值
        try
        {
            app.Resources.MergedDictionaries.Add(Load("Tokens"));
            _palette = Load(PaletteId());
            _paletteId = PaletteId();
            app.Resources.MergedDictionaries.Add(_palette);
            Logger.Info($"ThemeEngine 已装配：配色={_paletteId}（tokens + palette 两本字典）");
        }
        catch (Exception ex)
        {
            Logger.Error("ThemeEngine 字典装配失败（token 取用退代码兜底值）", ex);
        }
        HookSystemPreference();
    }

    /// <summary>系统主题即时跟随（§2.2）：偏好变化 → 失效 ThemeResolver 缓存 → 重评估（auto/system 翻转才广播）。</summary>
    static void HookSystemPreference()
    {
        if (_hooked) return;
        _hooked = true;
        try
        {
            SystemEvents.UserPreferenceChanged += (_, _) =>
            {
                ThemeResolver.InvalidateSystemTheme(); // 批次九失效链路：下一次取值即重读注册表
                ThemeAuto.EvaluateOnUi();              // 每分钟定时之外的即时通道（system 模式 ≤1 帧跟随）
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"系统主题监听安装失败（system 模式退回每分钟轮询）：{ex.Message}");
        }
    }

    // ---- 模式解析 ----

    /// <summary>theme.mode 原值；不在四档内的历史/非法值（含已删除的 transparent）一律按 light 兜底。</summary>
    public static string Mode()
    {
        string raw = SettingsStore.Instance.GetString("theme.mode", "light").Trim().ToLowerInvariant();
        foreach (var mode in Modes)
            if (string.Equals(mode, raw, StringComparison.Ordinal)) return mode;
        return "light";
    }

    /// <summary>折算后的亮/暗。</summary>
    public static bool IsDark()
    {
        switch (Mode())
        {
            case "light": return false;
            case "dark": return true;
            case "auto":
                var hour = DateTime.Now.Hour;
                return hour < 6 || hour >= 18; // 06:00~18:00 亮（00 §9；每分钟检查见 ThemeAuto）
            default: return ThemeResolver.IsSystemDark();
        }
    }

    /// <summary>把当前模式的配色字典换进 App 级合并字典（幂等：目标与当前一致则不动）。</summary>
    public static void EnsurePalette()
    {
        string id = PaletteId();
        if (string.Equals(id, _paletteId, StringComparison.Ordinal) && _palette != null) return;
        var app = Application.Current;
        try
        {
            var next = Load(id);
            if (app != null && _palette != null)
            {
                int index = app.Resources.MergedDictionaries.IndexOf(_palette);
                if (index >= 0) app.Resources.MergedDictionaries[index] = next;
                else app.Resources.MergedDictionaries.Add(next);
            }
            _palette = next;
            _paletteId = id;
            lock (Gate) TokenCache.Clear(); // 配色换代：清 token 取值缓存
            Logger.Info($"ThemeEngine 配色切换：{id}");
        }
        catch (Exception ex)
        {
            Logger.Error($"ThemeEngine 配色切换失败（保持 {_paletteId}）", ex);
        }
    }

    /// <summary>配色字典注册表（新增配色只改这一行 + 加一个字典文件）。</summary>
    static string PaletteFile(string id) => id switch
    {
        "dark" => "Dark",
        _ => "Light",
    };

    static string PaletteId() => Mode() switch
    {
        "dark" => "dark",
        "light" => "light",
        _ => IsDark() ? "dark" : "light", // auto / system：按折算结果选字典
    };

    static ResourceDictionary Load(string name)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(name, out var hit)) return hit;
            var dict = new ResourceDictionary { Source = new Uri(PackBase + name + ".xaml", UriKind.Absolute) };
            FreezeBrushes(dict);
            Loaded[name] = dict;
            return dict;
        }
    }

    /// <summary>字典内的 SolidColorBrush 统一 Freeze（跨线程共享安全，运行期不再变更）。</summary>
    static void FreezeBrushes(ResourceDictionary dict)
    {
        foreach (var key in dict.Keys)
        {
            if (key is string s && s.StartsWith("Brush.", StringComparison.Ordinal)
                && dict[key] is SolidColorBrush brush && brush.CanFreeze)
                brush.Freeze();
        }
    }

    // ---- token 取用 ----

    /// <summary>取配色画刷（如 <c>Brush.TextPrimary</c>）；缺失/无 Application 时返回透明兜底（绝不空引用）。</summary>
    public static Brush Brush(string key)
    {
        lock (Gate)
        {
            if (TokenCache.TryGetValue(key, out var hit))
                return hit as Brush ?? Brushes.Transparent;
        }
        Brush result = Brushes.Transparent;
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush b) result = b;
        }
        catch { /* 资源未就绪：透明兜底 */ }
        lock (Gate) TokenCache[key] = result;
        return result;
    }

    /// <summary>取配色（如 <c>Color.TextPrimary</c>）；缺失返回透明。</summary>
    public static System.Windows.Media.Color GetColor(string key)
    {
        lock (Gate)
        {
            if (TokenCache.TryGetValue(key, out var hit) && hit is System.Windows.Media.Color c)
                return c;
        }
        var result = Colors.Transparent;
        try
        {
            if (Application.Current?.TryFindResource(key) is System.Windows.Media.Color col) result = col;
        }
        catch { /* 同上 */ }
        lock (Gate) TokenCache[key] = result;
        return result;
    }

    /// <summary>取尺寸/几何 token（如 <c>Dim.Corner</c>）。</summary>
    public static double Dim(string key, double fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is double d) return d;
        }
        catch { /* 同上 */ }
        return fallback;
    }
}
