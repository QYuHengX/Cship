using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Core;

namespace Cship.Ui.Components;

/// <summary>
/// 轻量输入对话框（新建快捷方式填目标路径用，步骤 4.2）。
/// 简化实现：普通小窗 + 主题配色，不做玻璃材质（属临时交互件，【补充决策】见 STEP_LOG）。
/// </summary>
public sealed class InputDialog : Window
{
    readonly TextBox _box;
    readonly Button _ok;

    InputDialog(string title, string label, string initial)
    {
        bool dark = ThemeResolver.IsDark();
        var fg = new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1B));
        Background = new SolidColorBrush(dark ? Color.FromRgb(0x2A, 0x2A, 0x2A) : Color.FromRgb(0xF9, 0xF9, 0xF9));

        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;

        _box = new TextBox
        {
            Margin = new Thickness(0, 6, 0, 14),
            FontSize = 13,
            Text = initial,
        };
        _ok = new Button
        {
            Content = I18n.Tr("dialog.ok"),
            Width = 84,
            Height = 28,
            IsDefault = true,
        };
        _ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button
        {
            Content = I18n.Tr("dialog.cancel"),
            Width = 84,
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _ok, cancel },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = label, Foreground = fg, FontSize = 13 },
                _box,
                buttons,
            },
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) DialogResult = true;
        };
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>弹出输入框；确定返回去除首尾空白的文本，取消返回 null。</summary>
    public static string? Ask(string title, string label, string initial = "")
    {
        var dialog = new InputDialog(title, label, initial);
        return dialog.ShowDialog() == true ? dialog._box.Text.Trim() : null;
    }
}
