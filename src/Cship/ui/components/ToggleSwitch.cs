using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// mac 风开关（步骤 05 §2；2026-10-01 重绘）：34×18 胶囊轨道 + 14px 滑块，
/// 滑块 EaseOutBack 弹性滑行（AnimTuning.ToggleSlide），ON 态轨道左半浮出白色对勾，
/// 按下时滑块微放大（mac 按压手感）、松手回弹。IsChecked 可双向绑定；改动经
/// CheckedChanged 事件（页面写回 SettingsStore）。继承 Canvas：轨道/滑块直接挂可视化树
/// （手工 AddVisualChild 在布局行内不渲染，实测踩坑）。
/// </summary>
public class ToggleSwitch : Canvas
{
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked), typeof(bool), typeof(ToggleSwitch),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>用户点击或外部置值后触发（外部同步时也触发，页面侧用值相等判断防回环）。</summary>
    public event Action<bool>? CheckedChanged;

    static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (ToggleSwitch)d;
        self.UpdateVisual(animate: self.IsLoaded);
        self.CheckedChanged?.Invoke(self.IsChecked);
    }

    const double TrackW = 34, TrackH = 18, Knob = 14;

    readonly Border _track = new() { CornerRadius = new CornerRadius(TrackH / 2), Width = TrackW, Height = TrackH };
    readonly Border _knob = new()
    {
        Width = Knob,
        Height = Knob,
        CornerRadius = new CornerRadius(Knob / 2),
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.3, Direction = 270 },
    };
    readonly TranslateTransform _knobShift = new();
    readonly ScaleTransform _knobScale = new(1, 1);

    public ToggleSwitch()
    {
        Width = TrackW;
        Height = TrackH + 2;
        Cursor = Cursors.Hand;
        Focusable = false;
        Background = Brushes.Transparent; // Canvas 无背景不参与命中测试（实测踩坑）

        _knob.RenderTransform = new TransformGroup { Children = { _knobScale, _knobShift } };
        _knob.RenderTransformOrigin = new Point(0.5, 0.5);
        Children.Add(_track);
        SetTop(_track, 1);
        SetLeft(_track, 0);
        Children.Add(_knob);
        SetTop(_knob, 3);
        SetLeft(_knob, 2);

        MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            CaptureMouse();
            Anim.Run(_knobScale, ScaleTransform.ScaleXProperty, null, 1.15, AnimTuning.Micro / 2, EaseStyle.EaseOutQuad);
            Anim.Run(_knobScale, ScaleTransform.ScaleYProperty, null, 1.15, AnimTuning.Micro / 2, EaseStyle.EaseOutQuad);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (IsMouseCaptured) ReleaseMouseCapture();
            KnobScaleBack();
            IsChecked = !IsChecked;
        };
        MouseLeave += (_, _) => { if (IsMouseCaptured) { ReleaseMouseCapture(); KnobScaleBack(); } };

        Loaded += (_, _) => UpdateVisual(animate: false);
        UpdateVisual(animate: false);
    }

    void KnobScaleBack()
    {
        Anim.Run(_knobScale, ScaleTransform.ScaleXProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseOutQuad);
        Anim.Run(_knobScale, ScaleTransform.ScaleYProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseOutQuad);
    }

    void UpdateVisual(bool animate)
    {
        bool on = IsChecked;
        bool dark = Core.ThemeResolver.IsDark();
        // 配色去蓝化（2026-10-02）：亮主题=白主体滑块+黑灰轨道，暗主题=黑主体滑块+灰白轨道
        Color onColor = ((SolidColorBrush)SettingsPalette.Accent(dark)).Color;
        Color offColor = dark ? Color.FromRgb(0x6A, 0x6A, 0x6E) : Color.FromRgb(0xC7, 0xC7, 0xCC);
        _knob.Background = dark ? SettingsPalette.Freeze("#2E2E2E") : Brushes.White;

        if (_track.Background is SolidColorBrush tBrush && !tBrush.IsFrozen)
            tBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(on ? onColor : offColor, Anim.Ms(animate ? AnimTuning.ToggleSlide : 0)));
        else
            _track.Background = new SolidColorBrush(on ? onColor : offColor);

        double target = on ? TrackW - Knob - 2 : 0; // 左右各留 2px
        if (animate)
            Anim.Run(_knobShift, TranslateTransform.XProperty, null, target, AnimTuning.ToggleSlide, EaseStyle.EaseInOutQuad); // 去果冻（2026-10-03：原 EaseOutBack 过冲感取消）
        else
        {
            _knobShift.BeginAnimation(TranslateTransform.XProperty, null);
            _knobShift.X = target;
        }
    }
}
