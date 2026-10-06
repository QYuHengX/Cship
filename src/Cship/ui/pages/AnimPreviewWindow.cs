using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cship.Core;
using Cship.Ui.Animations;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 动画预设迷你预览窗（步骤 05 §3.4）：320×200 画布内按预设节奏演示缩略开合，播完自动关闭。
/// 预设 Open/Close 注册表签名绑定 BoardWindow，预览为同节奏（AnimTuning 同源）的独立缩略编排。
/// </summary>
public class AnimPreviewWindow : GlassWindow
{
    public AnimPreviewWindow(string presetId)
    {
        bool dark = ThemeResolver.IsDark();
        Width = 352;
        Height = 240;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var plate = new Border
        {
            Width = 200,
            Height = 120,
            CornerRadius = new CornerRadius(10),
            Background = dark ? SettingsPalette.Freeze("#CC1F1F1F") : SettingsPalette.Freeze("#CCF9F9F9"),
            BorderBrush = SettingsPalette.Divider(dark),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = I18n.Tr(BoardAnimPresets.Find(presetId)?.I18nName ?? presetId),
                FontSize = 12,
                Foreground = SettingsPalette.TextSecondary(dark),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Opacity = 0,
        };

        var canvas = new Grid { Width = 320, Height = 200, ClipToBounds = true };
        canvas.Children.Add(plate);

        var border = new Border
        {
            Background = SettingsPalette.Bg(dark),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = SettingsPalette.Divider(dark),
            Child = canvas,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.35, Direction = 270 },
        };
        Content = border;

        Loaded += (_, _) => Play(presetId, plate, () => Close());
    }

    static void Play(string presetId, FrameworkElement plate, Action done)
    {
        var root = (UIElement)plate;
        var center = new Point(160, 100);
        var origin = new Point(160, 200); // up 方向锚点（自底部滑入）
        var scale = new ScaleTransform(1, 1, center.X, center.Y);
        var translate = new TranslateTransform();

        void FinishOpen(Action? next) { plate.Opacity = 1; next?.Invoke(); }

        switch (presetId)
        {
            case "spitOut":
                var grp = new TransformGroup { Children = { translate, scale } };
                plate.RenderTransform = grp;
                Anim.Sequence(new Action<Action>[]
                {
                    next => Anim.Parallel(new Action<Action>[]
                    {
                        ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, 0.1, AnimTuning.SpitScaleMin, AnimTuning.SpitPress, EaseStyle.EaseInQuad, ok),
                        ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, 0.1, AnimTuning.SpitScaleMin, AnimTuning.SpitPress, EaseStyle.EaseInQuad, ok),
                        ok => Anim.Run(plate, OpacityProperty, 0, 1, AnimTuning.SpitPress, EaseStyle.EaseInQuad, ok),
                    }, next),
                    next => Anim.Parallel(new Action<Action>[]
                    {
                        ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, null, 1, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
                        ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, null, 1, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
                        ok => Anim.Run(translate, TranslateTransform.XProperty, null, 0, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
                        ok => Anim.Run(translate, TranslateTransform.YProperty, null, 0, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
                    }, () => FinishOpen(next)),
                }, () => ScheduleClose(done));
                break;
            case "popBounce":
                plate.RenderTransform = scale;
                double peakAt = AnimTuning.PopBounce * 0.6;
                Anim.RunKeys(scale, ScaleTransform.ScaleXProperty, new (double, double, EaseStyle)[]
                {
                    (0, 0, EaseStyle.EaseOutQuad),
                    (AnimTuning.PopBouncePeak, peakAt, EaseStyle.EaseOutQuad),
                    (1, AnimTuning.PopBounce, EaseStyle.EaseInOutQuad),
                });
                Anim.RunKeys(scale, ScaleTransform.ScaleYProperty, new (double, double, EaseStyle)[]
                {
                    (0, 0, EaseStyle.EaseOutQuad),
                    (AnimTuning.PopBouncePeak, peakAt, EaseStyle.EaseOutQuad),
                    (1, AnimTuning.PopBounce, EaseStyle.EaseInOutQuad),
                });
                Anim.Run(plate, OpacityProperty, 0, 1, AnimTuning.Micro, EaseStyle.Linear, onDone: () => ScheduleClose(done));
                break;
            case "slideEdge":
                plate.RenderTransform = translate;
                Anim.Parallel(new Action<Action>[]
                {
                    ok => Anim.Run(translate, TranslateTransform.YProperty, 120, 0, AnimTuning.SlideEdgeOpen, EaseStyle.EaseOutCubic, ok),
                    ok => Anim.Run(plate, OpacityProperty, 0, 1, AnimTuning.SlideEdgeOpen, EaseStyle.EaseOutCubic, ok),
                }, () => ScheduleClose(done));
                break;
            case "fadeSoft":
                plate.RenderTransform = translate;
                Anim.Parallel(new Action<Action>[]
                {
                    ok => Anim.Run(translate, TranslateTransform.YProperty, AnimTuning.SoftFadeRise, 0, AnimTuning.SoftFade, EaseStyle.EaseOutCubic, ok),
                    ok => Anim.Run(plate, OpacityProperty, 0, 1, AnimTuning.SoftFade, EaseStyle.EaseOutCubic, ok),
                }, () => ScheduleClose(done));
                break;
            default: // expand：自锚边滑入（up）+淡入
                plate.RenderTransform = translate;
                Anim.Parallel(new Action<Action>[]
                {
                    ok => Anim.Run(translate, TranslateTransform.YProperty, 140, 0, AnimTuning.Enter, EaseStyle.EaseOutCubic, ok),
                    ok => Anim.Run(plate, OpacityProperty, AnimTuning.FadeOpenFrom, 1, AnimTuning.Enter, EaseStyle.EaseOutCubic, ok),
                }, () => ScheduleClose(done));
                break;
            }

        _ = origin; // 预览统一自画布下方滑入/缩放（缩略口径，节奏与真实开合同源）
    }

    /// <summary>开合演示停顿一拍后自动关闭窗口。</summary>
    static void ScheduleClose(Action done)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            done();
        };
        timer.Start();
    }
}
