using System;
using Microsoft.Win32;
using Cship.Ui.Themes;
namespace Cship.Core;

/// <summary>
/// 亮暗折算入口（步骤 06 起由 <see cref="ThemeEngine"/> 接管模式解析）：
/// light/dark 直取；auto 按时间段；system 读系统注册表（00 §10.3 允许读主题键）。
/// 本类保留为"全项目最常用的一个布尔"的稳定调用面（<see cref="IsDark"/>）——
/// 既有调用方（MaterialBackground / GlassPopup / IconItem / 各设置页…）零改动即获得四档模式支持。
/// 性能（2026-10-05）：IsSystemDark 散布在 UI 绘制/配色路径（每次调用一次注册表开关），
/// 结果缓存 + UserPreferenceChanged 即时失效 + 5s TTL 兜底，避免每帧 N 次注册表读取。
/// **06 步契约**：system 模式的即时跟随必须经 <see cref="InvalidateSystemTheme"/> 失效
/// （ThemeEngine 的 UserPreferenceChanged 钩子已接），否则会出现最多 5s 延迟。
/// </summary>
public static class ThemeResolver
{
    /// <summary>折算后的亮/暗。见 ThemeEngine.IsDark。</summary>
    public static bool IsDark() => ThemeEngine.IsDark();

    static readonly object SysGate = new();
    static bool _sysDark;
    static bool _sysHasValue;
    static DateTime _sysReadAt;
    static bool _sysHooked;
    static readonly TimeSpan SysTtl = TimeSpan.FromSeconds(5);

    public static bool IsSystemDark()
    {
        lock (SysGate)
        {
            if (_sysHasValue && DateTime.UtcNow - _sysReadAt < SysTtl)
                return _sysDark;
        }
        bool dark = ReadSystemDark();
        lock (SysGate)
        {
            _sysDark = dark;
            _sysHasValue = true;
            _sysReadAt = DateTime.UtcNow;
            if (!_sysHooked)
            {
                _sysHooked = true;
                try { SystemEvents.UserPreferenceChanged += (_, _) => InvalidateSystemTheme(); }
                catch { /* 无消息泵的宿主：退回 TTL 兜底 */ }
            }
            return dark;
        }
    }

    static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return true; // 读不到按暗色兜底
        }
    }

    /// <summary>系统主题变化（或 ThemeEngine 需要强制重读）时清缓存，下一次取值即重读注册表。</summary>
    public static void InvalidateSystemTheme()
    {
        lock (SysGate)
            _sysHasValue = false;
    }
}
