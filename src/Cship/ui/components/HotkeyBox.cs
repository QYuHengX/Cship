using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Core;

namespace Cship.Ui.Components;

/// <summary>
/// 快捷键录入框（2026-10-05 需求③）：显示当前组合，点击进入录入态（"请按下按键…"），
/// 之后按下任意"主键（+修饰键）"即完成；Esc 或点击别处取消。
/// 录入必须走 <see cref="KeyboardHotkey.CaptureSink"/>（低级键盘钩子层）而不是 WPF 键盘事件——
/// 本程序自己吞掉了 Tab / Alt+Tab，只有钩子层能接住录制期的按键（放行 Alt+Tab 会弹出系统任务切换器）。
/// </summary>
public class HotkeyBox : Border
{
    /// <summary>用户按下新组合：页面据此决定是否落盘（接受后回写 <see cref="SetChord"/>）。</summary>
    public event Action<KeyChord>? ChordSet;

    /// <summary>当前处于录入态的框（全局至多一个）。</summary>
    public static HotkeyBox? Active { get; private set; }

    /// <summary>收口录入态（设置窗隐藏/关闭时调用，防钩子一直吞键）。</summary>
    public static void CancelActive() => Active?.EndCapture(commit: false, default);

    readonly TextBlock _text = new() { FontSize = 12 };
    bool _dark;
    bool _capturing;
    KeyChord _value;
    Window? _win;

    public HotkeyBox(bool dark)
    {
        _dark = dark;
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(12, 5, 12, 5);
        MinWidth = 104;
        Cursor = Cursors.Hand;
        Focusable = false;
        HorizontalAlignment = HorizontalAlignment.Right;
        Child = _text;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_capturing) EndCapture(commit: false, default); // 再点一次=取消
            else BeginCapture();
        };
        MouseEnter += (_, _) => { if (!_capturing) ApplyVisual(hover: true); };
        MouseLeave += (_, _) => { if (!_capturing) ApplyVisual(); };
        Unloaded += (_, _) => { if (_capturing) EndCapture(commit: false, default); };
        ApplyVisual();
    }

    /// <summary>当前显示的组合（未被页面接受时保持原值）。</summary>
    public KeyChord Value => _value;

    /// <summary>设置显示值。只改显示，不写设置——存储由页面在 ChordSet 里落盘。</summary>
    public void SetChord(KeyChord chord)
    {
        _value = chord;
        if (!_capturing) Render();
    }

    public void SetTheme(bool dark)
    {
        _dark = dark;
        if (!_capturing) ApplyVisual();
    }

    /// <summary>语言热切换：录入提示语重取文案（组合名本身与语言无关）。</summary>
    public void RefreshText()
    {
        if (_capturing) _text.Text = I18n.Tr("set.hotkey.press");
    }

    void BeginCapture()
    {
        CancelActive(); // 互斥：多框同时录入会让钩子只剩最后一个回调
        _capturing = true;
        Active = this;
        KeyboardHotkey.CaptureSink = OnChord; // 钩子层吞掉一切按键并回传组合
        _text.Text = I18n.Tr("set.hotkey.press");
        ApplyVisual();
        _win = Window.GetWindow(this);
        if (_win != null)
        {
            _win.PreviewMouseDown += OnWindowMouseDown; // 点别处=取消（不能在框内）
            _win.Deactivated += OnWindowDeactivated;    // 窗口失活=取消
        }
    }

    void EndCapture(bool commit, KeyChord chord)
    {
        if (!_capturing) return;
        _capturing = false;
        if (ReferenceEquals(Active, this)) Active = null;
        KeyboardHotkey.CaptureSink = null;
        if (_win != null)
        {
            _win.PreviewMouseDown -= OnWindowMouseDown;
            _win.Deactivated -= OnWindowDeactivated;
            _win = null;
        }
        Render();
        if (commit) ChordSet?.Invoke(chord);
    }

    /// <summary>钩子回调（UI 线程）：返回 true=继续录入。</summary>
    bool OnChord(KeyChord chord)
    {
        if (chord.IsEscape) { EndCapture(commit: false, default); return false; }
        if (chord.IsModifierOnly) return true; // 只按了修饰键：等主键
        EndCapture(commit: true, chord);
        return false;
    }

    void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Visual v && IsAncestorOf(v)) return; // 点自己交给 Up 分支处理
        EndCapture(commit: false, default);
    }

    void OnWindowDeactivated(object? sender, EventArgs e) => EndCapture(commit: false, default);

    void Render()
    {
        if (_capturing) return;
        _text.Text = _value.ToDisplay();
        ApplyVisual();
    }

    void ApplyVisual(bool hover = false)
    {
        BorderThickness = new Thickness(1);
        if (_capturing)
        {
            Background = SettingsPalette.Accent(_dark);
            BorderBrush = SettingsPalette.Accent(_dark);
            _text.Foreground = SettingsPalette.OnAccent(_dark);
            return;
        }
        Background = hover ? SettingsPalette.HoverOverlay(_dark) : SettingsPalette.Track(_dark);
        BorderBrush = SettingsPalette.Divider(_dark);
        _text.Foreground = SettingsPalette.Text(_dark);
    }
}
