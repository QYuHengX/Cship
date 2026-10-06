using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 滚轮平滑滚动（与 BoardWindow 同款：滚轮只推进目标偏移，渲染帧按比例插值逼近，
/// AnimTuning.ScrollFrameLerp ≈120ms 收敛）。设置窗分页/折叠卡复用（2026-10-02）。
/// </summary>
public sealed class SmoothScroller
{
    readonly ScrollViewer _sv;
    double _target;
    bool _animating;

    public SmoothScroller(ScrollViewer sv)
    {
        _sv = sv;
        sv.PreviewMouseWheel += OnWheel;
        sv.ScrollChanged += (_, _) => { if (!_animating) _target = sv.VerticalOffset; };
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_sv.ScrollableHeight <= 0) return;
        _target = Math.Clamp(_target - e.Delta, 0, _sv.ScrollableHeight);
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }
        e.Handled = true;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double diff = _target - _sv.VerticalOffset;
        if (Math.Abs(diff) < AnimTuning.ScrollFrameEpsilon)
        {
            _sv.ScrollToVerticalOffset(_target);
            _animating = false;
            CompositionTarget.Rendering -= OnFrame;
            return;
        }
        _sv.ScrollToVerticalOffset(_sv.VerticalOffset + diff * AnimTuning.ScrollFrameLerp);
    }
}
