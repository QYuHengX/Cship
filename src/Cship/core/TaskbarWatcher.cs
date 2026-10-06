using System;
using System.Windows;
using System.Windows.Interop;

namespace Cship.Core;

/// <summary>
/// 资源管理器重启监视（07 §2）：监听系统广播消息 <c>TaskbarCreated</c>——资源管理器
/// 崩溃重启后会广播它，桌面窗口（SHELLDLL_DefView/SysListView32）随之重建，桌面图标
/// 回到"可见"的默认态，因此必须**重放隐藏意图**。
///
/// 生命周期自管（技术指引：<c>AddHook</c> 的钩子必须在窗口关闭时 <c>RemoveHook</c>，
/// 否则 HwndSource 泄漏）：构造时若窗口已有 HWND 立即挂，否则等 <c>SourceInitialized</c>；
/// 窗口 <c>Closed</c> 时自动 <see cref="Dispose"/>（幂等）。消息号按进程缓存（见 SystemBridge）。
/// </summary>
public sealed class TaskbarWatcher : IDisposable
{
    readonly Window _window;
    HwndSource? _source;
    uint _message;
    bool _disposed;

    /// <summary>资源管理器重启（在钩子所在线程触发；UI 消费方自行调度到 UI 线程）。</summary>
    public event Action? TaskbarCreated;

    public TaskbarWatcher(Window window)
    {
        _window = window;
        window.SourceInitialized += OnSourceInitialized;
        window.Closed += OnWindowClosed;
        Attach(); // 窗口可能已经 Show 过（SourceInitialized 不会再来）
    }

    void OnSourceInitialized(object? sender, EventArgs e) => Attach();

    void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    void Attach()
    {
        if (_disposed || _source != null) return;
        try
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return; // 尚未建窗：等 SourceInitialized
            _message = SystemBridge.TaskbarCreatedMessage();
            if (_message == 0)
            {
                Logger.Warn("TaskbarCreated 消息注册失败：资源管理器重启后无法重放桌面图标隐藏意图");
                return;
            }
            _source = HwndSource.FromHwnd(hwnd);
            if (_source == null)
            {
                Logger.Warn("TaskbarCreated 钩子挂载失败：HwndSource 为空");
                return;
            }
            _source.AddHook(Hook);
            Logger.Info($"资源管理器重启监视已挂载（TaskbarCreated=0x{_message:X4}）");
        }
        catch (Exception ex)
        {
            Logger.Warn($"TaskbarCreated 钩子挂载异常：{ex.Message}");
        }
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _message && _message != 0)
        {
            Logger.Info("收到 TaskbarCreated：资源管理器已重启");
            try { TaskbarCreated?.Invoke(); }
            catch (Exception ex) { Logger.Warn($"TaskbarCreated 处理异常：{ex.Message}"); }
        }
        return IntPtr.Zero; // 不拦截，交给系统默认处理
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnWindowClosed;
        if (_source != null)
        {
            _source.RemoveHook(Hook); // 必须移除：否则 HwndSource 持有本对象 → 泄漏
            _source = null;
        }
    }
}
