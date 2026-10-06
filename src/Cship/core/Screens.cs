using System;
using System.Collections.Generic;
using WinForms = System.Windows.Forms;

namespace Cship.Core;

/// <summary>一台显示器的展示信息（07 §3：设置页"显示器N · 2560×1440 · DELL U2723QE"）。</summary>
public sealed record ScreenInfo(
    string DeviceName, string Model, int Width, int Height, bool Primary, WinForms.Screen Screen)
{
    /// <summary>基础信息回退（型号解析失败时的展示，绝不因 WMI/枚举失败而空白）。</summary>
    public string Describe(int index, string primarySuffix, string secondarySuffix)
    {
        string role = Primary ? primarySuffix : secondarySuffix;
        string head = $"{Width}×{Height} · {role}";
        return string.IsNullOrEmpty(Model) ? head : $"{head} · {Model}";
    }
}

/// <summary>
/// 屏幕选择（00 §8 display.screen：auto=光标所在屏；否则按设备名匹配）+ 枚举信息（07 §3）。
/// 与 FloatingDock 内部逻辑同一口径，收纳板等窗口共用。
///
/// 缓存（07 §3）：<c>Screen.AllScreens</c> 底层是 P/Invoke，批次九实测它是热点调用之一；
/// 而 <c>SelectedScreen()</c> 在开板/拖板/夹边等高频路径上被反复调用。故：
/// ① 枚举结果按 <see cref="Invalidate"/> 失效重取（显示设置变化时由 App 调用）；
/// ② 型号名单独缓存（EnumDisplayDevices 相对昂贵，且与设备名一一对应，几乎不变）。
/// </summary>
public static class Screens
{
    static WinForms.Screen[]? _allCache;
    static Dictionary<string, string>? _modelCache; // DeviceName → 型号名（空串=已知取不到）
    static readonly object Gate = new();

    /// <summary>显示设置变化（分辨率/热插拔）后清缓存（07 §3；由 App 的 DisplaySettingsChanged 调用）。</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _allCache = null;
            _modelCache = null; // 换屏后型号映射可能整体改变
        }
    }

    /// <summary>全部屏幕（缓存副本，调用方不得修改）。</summary>
    public static WinForms.Screen[] All()
    {
        lock (Gate)
            return _allCache ??= WinForms.Screen.AllScreens;
    }

    /// <summary>按设备名找屏；不存在返回 null（热插拔后"所选屏已拔掉"的判据）。</summary>
    public static WinForms.Screen? ByDeviceName(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return null;
        foreach (var s in All())
            if (string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }

    /// <summary>设置页用的枚举信息（型号名失败回退基础信息，绝不阻塞）。</summary>
    public static IReadOnlyList<ScreenInfo> Enumerate()
    {
        var list = new List<ScreenInfo>();
        foreach (var s in All())
            list.Add(new ScreenInfo(s.DeviceName, ModelOf(s.DeviceName),
                s.Bounds.Width, s.Bounds.Height, s.Primary, s));
        return list;
    }

    /// <summary>型号名（EnumDisplayDevices；失败/通用占位返回空串）。按设备名缓存。</summary>
    public static string ModelOf(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return "";
        lock (Gate)
        {
            _modelCache ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_modelCache.TryGetValue(deviceName, out var cached)) return cached;
        }
        string model = SystemBridge.MonitorModel(deviceName);
        lock (Gate)
        {
            _modelCache ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _modelCache[deviceName] = model;
        }
        return model;
    }

    /// <summary>所选屏（auto=光标所在屏；指定=按设备名匹配；指定屏已拔掉时回退光标所在屏）。</summary>
    public static WinForms.Screen SelectedScreen()
    {
        var id = SettingsStore.Instance.GetString("display.screen", "auto");
        if (!string.Equals(id, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var match = ByDeviceName(id);
            if (match != null) return match;
            // 指定的屏不在了（热插拔）：回退光标所在屏（App 的 DisplaySettingsChanged 会把
            // display.screen 正式改回 auto 并气泡告知，此处只是"改回之前"的即时兜底）
        }
        var cur = WinForms.Cursor.Position;
        return WinForms.Screen.FromPoint(new System.Drawing.Point(cur.X, cur.Y));
    }

    /// <summary>主屏（多屏迁移目标：热插拔后悬浮窗复位到它）。</summary>
    public static WinForms.Screen Primary()
    {
        foreach (var s in All())
            if (s.Primary) return s;
        var all = All();
        return all.Length > 0 ? all[0] : WinForms.Screen.PrimaryScreen!;
    }

    /// <summary>指定屏是否仍在当前枚举中（热插拔检测用）。</summary>
    public static bool Exists(string deviceName) => ByDeviceName(deviceName) != null;

    /// <summary>工作区矩形（物理像素）是否与传入值一致（分辨率变化检测用）。</summary>
    public static bool WorkAreaMatches(string deviceName, int x, int y, int w, int h)
    {
        var s = ByDeviceName(deviceName);
        if (s == null) return false;
        var b = s.WorkingArea;
        return b.X == x && b.Y == y && b.Width == w && b.Height == h;
    }
}
