using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 分段控件（步骤 05 §2）：N 段二/多选一，选中段强调色填充（mac 风）。
/// 清单04任务10：选中底改为**独立滑块**，切换时滑过去（Micro/EaseInOutQuad），
/// 段文字颜色同步淡入淡出（ColorAnimation 挂非冻结克隆刷）；初始/换语言/换主题仍瞬时落位。
/// </summary>
public class Segmented : Border
{
    /// <summary>选中段变化（外部 SetSelected(syncToUi:false) 不触发）。</summary>
    public event Action<int>? SelectedChanged;

    readonly Grid _root = new();
    readonly StackPanel _host = new() { Orientation = Orientation.Horizontal };
    readonly Border _thumb = new()
    {
        CornerRadius = new CornerRadius(5),
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Stretch,
        Width = 0, // 数值基值：Width=NaN(auto) 会让 Anim.Run(from=null) 构造出 From=NaN 的动画直接抛异常
    };
    readonly TranslateTransform _thumbShift = new();
    readonly List<Border> _segments = new();
    readonly List<TextBlock> _labels = new();
    readonly List<SolidColorBrush> _labelBrushes = new();
    int _selected;
    bool _dark;
    bool _thumbPending; // 段尺寸未就绪（未 Loaded/刚换文字）：等下次布局补落位

    public Segmented(bool dark)
    {
        _dark = dark;
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(4);
        Background = SettingsPalette.Track(dark);
        _thumb.RenderTransform = _thumbShift;
        _root.Children.Add(_thumb);
        _root.Children.Add(_host);
        Child = _root;
        _host.SizeChanged += (_, _) => { if (_thumbPending) LayoutThumb(animate: false); };
        Loaded += (_, _) => { if (_thumbPending) LayoutThumb(animate: false); };
    }

    /// <summary>重建分段（labels 变化/语言切换时调用）。</summary>
    public void SetOptions(IReadOnlyList<string> labels)
    {
        _host.Children.Clear();
        _segments.Clear();
        _labels.Clear();
        _labelBrushes.Clear();
        for (int i = 0; i < labels.Count; i++)
        {
            var text = new TextBlock { FontSize = 12 };
            var seg = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 5, 10, 5),
                Child = text,
                Cursor = Cursors.Hand,
            };
            int index = i;
            seg.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SetSelected(index, syncToUi: true);
            };
            _host.Children.Add(seg);
            _segments.Add(seg);
            _labels.Add(text);
            _labelBrushes.Add(((SolidColorBrush)SettingsPalette.Text(_dark)).Clone()); // 非冻结克隆才可动画
        }
        RefreshTexts(labels);
        ApplyVisual(animate: false);
    }

    /// <summary>语言切换后重刷段文字（段数须一致）。文字变宽可改段宽而总宽不变
    /// （Host SizeChanged 不触发），布局补跑后再核对一次滑块落位。</summary>
    public void RefreshTexts(IReadOnlyList<string> labels)
    {
        for (int i = 0; i < Math.Min(labels.Count, _labels.Count); i++)
            _labels[i].Text = labels[i];
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            () => { if (_thumbPending) LayoutThumb(animate: false); });
    }

    public int SelectedIndex => _selected;

    public void SetSelected(int index, bool syncToUi)
    {
        bool changed = index != _selected;
        _selected = Math.Max(0, Math.Min(index, Math.Max(_segments.Count - 1, 0)));
        ApplyVisual(animate: changed);
        if (syncToUi && changed)
            SelectedChanged?.Invoke(_selected);
    }

    public void SetTheme(bool dark)
    {
        _dark = dark;
        Background = SettingsPalette.Track(dark);
        _thumb.Background = SettingsPalette.Accent(dark);
        ApplyVisual(animate: false);
    }

    /// <summary>选中底滑块 + 段文字颜色落位。animate=false 供初始/换语言/换主题（瞬时）。</summary>
    void ApplyVisual(bool animate)
    {
        _thumb.Background = SettingsPalette.Accent(_dark);
        for (int i = 0; i < _segments.Count; i++)
        {
            bool on = i == _selected;
            var target = (SolidColorBrush)(on ? SettingsPalette.OnAccent(_dark) : SettingsPalette.Text(_dark));
            var brush = _labelBrushes[i];
            _labels[i].Foreground = brush;
            if (animate && brush.Color != target.Color)
            {
                var anim = new ColorAnimation(target.Color, Anim.Ms(AnimTuning.Micro))
                {
                    EasingFunction = AnimTuning.Map(EaseStyle.EaseInOutQuad),
                };
                Cship.Ui.Animations.AnimationClock.Apply(anim);
                brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
            else
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                brush.Color = target.Color;
            }
        }
        LayoutThumb(animate);
    }

    /// <summary>
    /// 选中滑块滑到当前段（宽度 + 位移都走动画接续，Anim.Run from=null 语义）；
    /// 段尺寸未就绪（未布局/刚换文字）则记账，等 SizeChanged/Loaded 补落位。
    /// </summary>
    void LayoutThumb(bool animate)
    {
        if (_selected < 0 || _selected >= _segments.Count)
        {
            _thumb.Visibility = Visibility.Collapsed;
            return;
        }
        var seg = _segments[_selected];
        if (!IsLoaded || seg.ActualWidth <= 0)
        {
            _thumbPending = true;
            return;
        }
        _thumbPending = false;
        _thumb.Visibility = Visibility.Visible;
        double x = 0;
        for (int i = 0; i < _selected; i++) x += _segments[i].ActualWidth;
        if (animate)
        {
            Anim.Run(_thumb, WidthProperty, null, seg.ActualWidth, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
            Anim.Run(_thumbShift, TranslateTransform.XProperty, null, x, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
        }
        else
        {
            // 瞬时落位（初始/换语言/换主题）：直接清动画设值，不走 0ms 动画
            _thumb.BeginAnimation(WidthProperty, null);
            _thumb.Width = seg.ActualWidth;
            _thumbShift.BeginAnimation(TranslateTransform.XProperty, null);
            _thumbShift.X = x;
        }
    }
}
