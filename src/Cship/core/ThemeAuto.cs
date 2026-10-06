using System;
using System.Windows.Threading;
using Cship.Ui.Themes;
namespace Cship.Core;

/// <summary>
/// 主题模式的自折算检查（步骤 05【补充决策】；06 步扩到 auto + system 两档）：
/// auto=本地时间 06:00~18:00 取亮，每分钟检查一次；system=跟随系统，经 ThemeEngine 的
/// <c>UserPreferenceChanged</c> 钩子**即时**调用 <see cref="EvaluateOnUi"/>（不依赖分钟轮询）。
/// 折算结果翻转时经 <see cref="DarkChanged"/> 通知各窗口换装（订阅方自行走 ThemeFade 过渡）；
/// 其余模式不触发事件（mode 切换本身经 SettingsChanged 广播）。
/// </summary>
public static class ThemeAuto
{
    static DispatcherTimer? _timer;
    static Dispatcher? _dispatcher;
    static bool _lastDark;
    static bool _hasValue;

    /// <summary>auto/system 下亮暗折算翻转（UI 线程触发）。</summary>
    public static event Action<bool>? DarkChanged;

    /// <summary>启动每分钟检查（UI 线程调用，幂等）。</summary>
    public static void EnsureStarted()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();
        Evaluate();
    }

    /// <summary>跨线程安全入口（SystemEvents 钩子在非 UI 线程触发）：调度到 UI 线程再评估。</summary>
    public static void EvaluateOnUi()
    {
        var d = _dispatcher;
        if (d == null || d.CheckAccess()) Evaluate();
        else d.BeginInvoke(new Action(Evaluate));
    }

    /// <summary>立即评估（theme.mode 切到 auto/system、启动、系统偏好变化时调用）。
    /// 仅 auto / system 模式对外报告翻转。</summary>
    public static void Evaluate()
    {
        ThemeEngine.EnsurePalette(); // auto/system 折算结果可能换配色字典（幂等）
        string mode = SettingsStore.Instance.GetString("theme.mode", "light");
        if (!string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "system", StringComparison.OrdinalIgnoreCase)) return;
        bool dark = ThemeResolver.IsDark();
        if (_hasValue && dark != _lastDark)
        {
            Logger.Info($"{mode} 主题折算切换：→ {(dark ? "暗" : "亮")}"
                + (string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase) ? "（06:00~18:00 为亮）" : "（系统偏好变化）"));
            DarkChanged?.Invoke(dark);
        }
        _lastDark = dark;
        _hasValue = true;
    }
}
