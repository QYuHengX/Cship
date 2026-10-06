using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Cship.Ui.Components;

/// <summary>
/// 设置窗展开卡承载层（2026-10-04，清单01任务12/13）：展开卡不再用 Popup（独立 HWND 顶级窗口，
/// 会跃出设置窗边界），统一渲染进设置窗自身的可视化树——同窗渲染、绝不跃出。
/// SettingsWindow 在内容根挂一层全窗口 Canvas 后 <see cref="Attach"/>；
/// 有卡打开时承载层背景参与命中，点击卡外任意区域 = <see cref="CloseAll"/>（点外关闭，带各自收口动画）。
/// </summary>
public static class ExpanderOverlay
{
    static Panel? _host;
    static readonly List<Action> _openClosers = new();

    /// <summary>承载层是否已挂进设置窗（未挂时展开卡一律拒绝展开并记日志，防跳出同窗约束）。</summary>
    public static bool Available => _host != null;

    public static void Attach(Panel host)
    {
        _host = host;
        // 背景命中（2026-10-04 修订B）：无卡打开时 Background=null（整层不参与命中，不挡下层交互）；
        // 有卡打开时改 Transparent（可命中）。关闭必须成对消费 Down+Up 且在 Up 才触发：
        // 只吃 Down 会漏 Up 给下层控件——展开按钮/折叠头部都是 Up 触发，"点按钮关卡"会变成
        // Down 关卡 + Up 重开（用户实机的"再点一次反而重新展开"根因）。
        // Up 时按原始命中目标判定：命中背景（=点卡外）才关闭；命中卡内（如滑块拖动、卡空白处）
        // 只吞事件不关闭——不能按 Down 判定，滑块等控件自捕获 Down 后 Up 会继续冒泡上来。
        host.MouseLeftButtonDown += (_, e) =>
        {
            if (_openClosers.Count == 0) return;
            e.Handled = true; // 下层控件全程不感知这次点击（卡内控件自带 Down 处理者不受影响）
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            if (_openClosers.Count == 0) return;
            e.Handled = true;
            if (ReferenceEquals(e.OriginalSource, _host)) CloseAll();
        };
        host.Unloaded += (_, _) => Detach(host);
    }

    public static void Detach(Panel host)
    {
        if (!ReferenceEquals(_host, host)) return;
        CloseAll();
        _host = null;
    }

    /// <summary>登记一张已打开的卡（close=收口回调，内部自带关闭动画）。返回解除登记的句柄。</summary>
    public static IDisposable RegisterOpen(Action close)
    {
        _openClosers.Add(close);
        UpdateBackdrop();
        return new Token(close);
    }

    sealed class Token(Action close) : IDisposable
    {
        public void Dispose()
        {
            _openClosers.Remove(close);
            UpdateBackdrop();
        }
    }

    /// <summary>关闭当前打开的所有展开卡（逐个调用收口回调；互斥由此天然成立）。</summary>
    public static void CloseAll()
    {
        if (_openClosers.Count == 0) return;
        var closers = _openClosers.ToArray();
        _openClosers.Clear();
        UpdateBackdrop();
        foreach (var close in closers) close();
    }

    static void UpdateBackdrop()
    {
        if (_host != null)
            _host.Background = _openClosers.Count > 0 ? Brushes.Transparent : null; // null=整层不参与命中
    }

    /// <summary>把 card 摆进承载层：锚点 anchor 下方覆盖式出现；宽高/位置 clamp 在承载层内
    /// （shadowPad=四周阴影余量，卡被 clamp 后阴影仍不触界，视觉上无硬切残块）。
    /// scroll 参数可为 null；非 null 时其 MaxHeight 会按剩余空间收缩，保证卡整体装得下。
    /// 返回 false=承载层不可用。</summary>
    public static bool TryPlace(FrameworkElement anchor, FrameworkElement card,
        ScrollViewer? scroll, double scrollBaseMax, double shadowPad)
    {
        if (_host is not { IsLoaded: true, ActualWidth: > 1, ActualHeight: > 1 })
        {
            Core.Logger.Warn("展开卡承载层不可用，拒绝展开（防跃出设置窗）");
            return false;
        }
        double hostW = _host.ActualWidth, hostH = _host.ActualHeight;
        var origin = anchor.TransformToVisual(_host).Transform(new Point(0, 0));
        double top = origin.Y + anchor.ActualHeight + 3;

        // 先按"剩余高度"约束卡内滚动区，再量卡的实际宽高做 clamp
        double availH = Math.Max(hostH - top - shadowPad, 40);
        if (scroll != null)
            scroll.MaxHeight = Math.Min(scrollBaseMax, Math.Max(availH - 10, 40));
        card.Measure(new Size(hostW, double.PositiveInfinity));
        double w = Math.Min(card.DesiredSize.Width, hostW - shadowPad * 2);

        card.BeginAnimation(UIElement.OpacityProperty, null);
        _host.Children.Add(card);
        double left = Math.Clamp(origin.X, shadowPad, Math.Max(hostW - w - shadowPad, shadowPad));
        double topPos = Math.Min(top, Math.Max(hostH - card.DesiredSize.Height - shadowPad, shadowPad));
        // 落点取整到整物理像素（2026-10-05 需求①）：锚点经布局换算出的坐标常带小数，
        // 卡片落在半像素上时其中的文字会被重采样发虚。DIP→物理取整再折回 DIP，1.0 缩放下即整数。
        var dpi = VisualTreeHelper.GetDpi(_host);
        Canvas.SetLeft(card, Math.Round(left * dpi.DpiScaleX) / dpi.DpiScaleX);
        Canvas.SetTop(card, Math.Round(topPos * dpi.DpiScaleY) / dpi.DpiScaleY);
        return true;
    }
}
