using System;
using System.Collections.Generic;
using System.Windows.Media;
using Cship.Ui.Themes;
namespace Cship.Ui.Components;

/// <summary>
/// 设置界面配色（步骤 06 §2.1：退化为"从 token 取值"的薄封装）。
/// 画刷一律取 <see cref="ThemeEngine"/> 的 <c>Brush.*</c> token（单一来源=themes\*.xaml 字典）；
/// token 未就绪（极早期调用/无 Application）时回落到旧字面量，保证任何时序下都不会出现"透明控件"。
/// <c>dark</c> 参数保留为调用方兼容签名——真实配色由 ThemeEngine 按当前模式决定，
/// 因此"系统/自动在窗口存活期间翻转"也能被这些取值即时反映。
/// 性能（2026-10-05）：<see cref="Freeze(string)"/> 仍按色值缓存已 Freeze 画刷。
/// </summary>
public static class SettingsPalette
{
    static readonly object Gate = new();
    static readonly Dictionary<string, Brush> Cache = new(StringComparer.Ordinal);

    static Brush Token(string key, bool dark, string lightHex, string darkHex)
    {
        var brush = ThemeEngine.Brush(key);
        if (brush != null && !ReferenceEquals(brush, Brushes.Transparent)) return brush;
        return Freeze(dark ? darkHex : lightHex); // 兜底：token 未就绪
    }

    public static Brush Bg(bool dark) => Token("Brush.Bg", dark, "#F2F9F9F9", "#F21F1F1F");
    public static Brush Card(bool dark) => Token("Brush.Card", dark, "#80FFFFFF", "#14161622");
    public static Brush Text(bool dark) => Token("Brush.TextPrimary", dark, "#1B1B1B", "#F2F2F2");
    public static Brush TextSecondary(bool dark) => Token("Brush.TextSecondary", dark, "#5F5F5F", "#B8B8B8");
    public static Brush Divider(bool dark) => Token("Brush.Divider", dark, "#E5E5E5", "#3A3A3A");
    // 强调色去蓝化（2026-10-02 用户反馈）：亮=黑灰 / 暗=灰白，选中填充一律中性色
    public static Brush Accent(bool dark) => Token("Brush.Accent", dark, "#3C3C3C", "#D4D4D4");
    /// <summary>强调色填充上的前景色（选中段/闩锁/按钮文字）：暗主题强调色为灰白→深字，亮主题→白字。</summary>
    public static Brush OnAccent(bool dark) => Token("Brush.OnAccent", dark, "#FFFFFF", "#1B1B1B");
    public static Brush HoverOverlay(bool dark) => Token("Brush.HoverOverlay", dark, "#14000000", "#14FFFFFF");
    public static Brush Track(bool dark) => Token("Brush.Track", dark, "#26000000", "#33FFFFFF");
    public static Brush Danger(bool dark) => Token("Brush.Danger", dark, "#FF6B6B", "#FF6B6B");
    /// <summary>危险色填充上的前景色（"恢复默认"这类破坏性按钮）：红底恒配白字。</summary>
    public static Brush OnDanger(bool dark) => Freeze("#FFFFFF");

    /// <summary>取（并缓存）指定色值的冻结画刷；同色值全进程共享同一实例。</summary>
    public static Brush Freeze(string hex)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(hex, out var hit)) return hit;
        }
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        lock (Gate)
        {
            if (Cache.TryGetValue(hex, out var raced)) return raced;
            Cache[hex] = brush;
        }
        return brush;
    }
}
