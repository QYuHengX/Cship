using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 滑轨（步骤 05 §2）：轨道强调色填充 + 常驻数值（数值 + 单位，显示在滑轨右侧）；Minimum/Maximum/Value 双向绑定。
/// 继承 Canvas：轨道/填充/滑块/数值直接挂可视化树（手工 AddVisualChild 不渲染，实测踩坑）。
/// 拖动中即时触发 ValueChanged（页面写回 SettingsStore，设置侧有防抖落盘）。
/// 2026-10-04 错误修复：① 滑块圆球与单位文字随主题（黑主题白 / 白主题黑）；
/// ② 数值不再悬停气泡显示，改为恒定居中显示在单位前、滑轨后（Unit 属性自带单位，各页不再另挂单位标签）。
/// </summary>
public class SliderX : Canvas
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SliderX), new PropertyMetadata(0.0, OnRangeChanged));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SliderX), new PropertyMetadata(100.0, OnRangeChanged));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SliderX),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>步进吸附（2026-10-03）：>0 时拖动值吸附到 Minimum+n×Step（如 0.1=精确到 0.1、1=不连续档位）；
    /// 0=旧行为（靠近整数时吸附整数）。设为 CLR 属性即可，无需变更通知。</summary>
    public double Step { get; set; }

    /// <summary>数值单位（"px"/"%"；空=不带单位）：与数值同排显示，颜色随主题（2026-10-04 错误修复①）。</summary>
    public string Unit
    {
        get => _unit;
        set { if (_unit == value) return; _unit = value ?? ""; LayoutParts(); }
    }
    string _unit = "";

    /// <summary>是否显示常驻数值（默认 true）；档位滑轨等另有文案的场景可关（2026-10-04 错误修复②）。</summary>
    public bool ShowValue
    {
        get => _showValue;
        set { if (_showValue == value) return; _showValue = value; LayoutParts(); }
    }
    bool _showValue = true;

    /// <summary>数值变化（拖动中逐帧触发）。</summary>
    public event Action<double>? ValueChanged;

    static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SliderX)d).LayoutParts();
    static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (SliderX)d;
        self.LayoutParts(glide: self.Step >= 1 && self.ActualWidth > 1); // 档位滑轨：视觉滑向新档（清单01任务13）
        self.ValueChanged?.Invoke(self.Value);
    }

    const double TrackHeight = 4;
    const double ValueGap = 8;      // 滑轨右端到数值文字的间距
    const double ValueAreaUnit = 46; // 带单位（如 "100.0%"）的数值区宽
    const double ValueAreaPlain = 34; // 不带单位的数值区宽
    double ValueArea => !_showValue ? 0 : (_unit.Length > 0 ? ValueAreaUnit : ValueAreaPlain);

    readonly Border _track = new() { Height = TrackHeight, CornerRadius = new CornerRadius(2), ClipToBounds = true };
    readonly Border _fill = new() { CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
    readonly Border _thumb = new()
    {
        Width = 14,
        Height = 14,
        CornerRadius = new CornerRadius(7),
        IsHitTestVisible = false,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.3, Direction = 270 },
    };
    // 常驻数值（数值 + 单位同排，显示在滑轨右侧，不再悬停气泡、不再有出现/消失动画）
    readonly TextBlock _valueText = new() { FontSize = 11, IsHitTestVisible = false };
    readonly TranslateTransform _thumbShift = new();
    readonly TranslateTransform _fillShift = new();
    bool _dark;
    bool _dragging;

    public SliderX(bool dark)
    {
        _dark = dark;
        MinHeight = 34;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent; // Canvas 无背景不参与命中测试（实测踩坑）
        _thumb.RenderTransform = _thumbShift;
        _fill.RenderTransform = _fillShift;

        _track.Child = _fill;
        Children.Add(_track);
        Children.Add(_thumb);
        Children.Add(_valueText);

        ApplyVisual();
        Loaded += (_, _) => LayoutParts();
        SizeChanged += (_, _) => LayoutParts();

        MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _dragging = true;
            CaptureMouse();
            UpdateFromPoint(e.GetPosition(this));
        };
        MouseMove += (_, e) =>
        {
            if (_dragging)
                UpdateFromPoint(e.GetPosition(this));
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
        };
    }

    public void SetTheme(bool dark)
    {
        _dark = dark;
        ApplyVisual();
    }

    void ApplyVisual()
    {
        _track.Background = SettingsPalette.Track(_dark);
        _fill.Background = SettingsPalette.Accent(_dark);
        // 拖动圆球随主题（2026-10-04 错误修复①）：黑主题白、白主题黑
        _thumb.Background = _dark ? Brushes.White : SettingsPalette.Freeze("#2E2E2E");
        _valueText.Foreground = _dark ? Brushes.White : SettingsPalette.Freeze("#2E2E2E"); // 单位（px/%）同口径
    }

    void UpdateFromPoint(Point p)
    {
        double width = Math.Max(ActualWidth - ValueArea, 1); // 命中口径=滑轨段（右侧数值区不参与）
        double frac = Math.Clamp(p.X / width, 0, 1);
        double raw = Minimum + frac * (Maximum - Minimum);
        if (Step > 0.0001)
        {
            // 步进吸附（2026-10-03）：档位=Minimum + n×Step，钳回域内
            double n = Math.Round((raw - Minimum) / Step);
            raw = Math.Clamp(Minimum + n * Step, Minimum, Maximum);
        }
        else
        {
            double snapped = Math.Round(raw);
            if (Math.Abs(snapped - raw) < 0.001) raw = snapped; // 整数域滑轨吸附整数
        }
        if (Math.Abs(raw - Value) < 0.0001) return;
        Value = raw;
    }

    /// <summary>摆放轨道/填充/滑块/常驻数值。glide=true 时滑块与填充**动画**滑向目标位置（档位过渡，
    /// 只动视觉 transform、不回写 Value，设置回写在 OnValueChanged 已即时完成）；false=直接落位
    /// （布局初始化/尺寸变化/连续滑轨）。右侧 ValueArea 宽固定留给数值，滑轨宽度=总宽−该区。</summary>
    void LayoutParts(bool glide = false)
    {
        double width = ActualWidth;
        if (width <= 0) return;
        double trackW = Math.Max(0, width - ValueArea);
        double range = Math.Max(Maximum - Minimum, 0.0001);
        double frac = Math.Clamp((Value - Minimum) / range, 0, 1);
        double x = frac * trackW;
        double h = Math.Max(ActualHeight, 30);

        SetTop(_track, (h - TrackHeight) / 2);
        _track.Width = trackW;
        _fill.Width = trackW;

        SetTop(_thumb, (h - _thumb.Height) / 2);
        if (glide)
        {
            // from=null=当前有效值接续（Anim.Run 语义）：连续拖动逐帧换目标也不跳变
            Anim.Run(_thumbShift, TranslateTransform.XProperty, null, x - _thumb.Width / 2,
                AnimTuning.SliderStepSnap, EaseStyle.EaseOutQuad);
            Anim.Run(_fillShift, TranslateTransform.XProperty, null, -(trackW - x),
                AnimTuning.SliderStepSnap, EaseStyle.EaseOutQuad);
        }
        else
        {
            _thumbShift.X = x - _thumb.Width / 2;
            _fillShift.X = -(trackW - x); // 左侧填充：右移裁切
        }

        _valueText.Visibility = _showValue ? Visibility.Visible : Visibility.Collapsed;
        if (!_showValue) return;
        _valueText.Text = FormatValue(Value) + _unit; // 数值恒定显示在单位之前、滑轨之后
        _valueText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_valueText, trackW + ValueGap);
        SetTop(_valueText, (h - _valueText.DesiredSize.Height) / 2);
    }

    string FormatValue(double v)
    {
        if (Step >= 1) return Math.Round(v).ToString("0");        // 档位滑轨：整数
        if (Step > 0.0001) return Math.Round(v, 1).ToString("0.0"); // 0.1 精度（2026-10-03）
        return Math.Abs(v - Math.Round(v)) < 0.001 ? Math.Round(v).ToString("0") : v.ToString("0.#");
    }
}
