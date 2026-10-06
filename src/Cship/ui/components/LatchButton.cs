using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 按下保持亮起（强调色填充）、再点熄灭的闩锁按钮（步骤 05 §2，高级页内容过滤用）。
/// </summary>
public class LatchButton : Border
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(LatchButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOnChanged));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>状态变化（含外部置值）。</summary>
    public event Action<bool>? Toggled;

    static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (LatchButton)d;
        self.ApplyVisual();
        self.Toggled?.Invoke(self.IsOn);
    }

    readonly TextBlock _label = new() { FontSize = 12 };
    bool _dark;

    public LatchButton(bool dark)
    {
        _dark = dark;
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(10, 5, 10, 5);
        Margin = new Thickness(0, 0, 6, 0);
        Cursor = Cursors.Hand;
        Focusable = false;
        Child = _label;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            IsOn = !IsOn;
        };
        MouseEnter += (_, _) => ApplyVisual(hover: true);
        MouseLeave += (_, _) => ApplyVisual();
        ApplyVisual();
    }

    public void SetText(string text) => _label.Text = text;

    public void SetTheme(bool dark)
    {
        _dark = dark;
        ApplyVisual();
    }

    void ApplyVisual(bool hover = false)
    {
        var accent = SettingsPalette.Accent(_dark);
        if (IsOn)
        {
            Background = accent;
            _label.Foreground = SettingsPalette.OnAccent(_dark);
        }
        else
        {
            Background = hover ? SettingsPalette.HoverOverlay(_dark) : SettingsPalette.Track(_dark);
            _label.Foreground = SettingsPalette.Text(_dark);
        }
    }
}
