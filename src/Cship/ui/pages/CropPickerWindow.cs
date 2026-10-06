using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Cship.Core;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 图片框选器（步骤 05 §2 CropPicker）：显示图片 + 可拖直角四边形（8 手柄：四角+四边中点），
/// 输出 crop 矩形（原图像素坐标）。确认/取消经回调返回（取消=null）。
/// </summary>
public partial class CropPickerWindow : GlassWindow
{
    readonly ImageSource _source;
    readonly double _scale;          // 显示尺寸 / 解码图像素
    readonly double _decodeScale = 1; // 解码图像素 / 原图像素（原图过大时的解码缩小比例）
    readonly Canvas _overlay = new();
    readonly Border _sel = new()
    {
        BorderThickness = new Thickness(1),
        BorderBrush = Brushes.White,
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
    };
    bool _dark;

    Rect _rect; // 显示坐标
    const double MinSize = 24;
    enum Handle { None, Move, Nw, N, Ne, E, Se, S, Sw, W }
    Handle _drag = Handle.None;
    Point _dragStart;
    Rect _dragStartRect;

    /// <summary>确认回调：crop 为原图像素坐标矩形；取消时 rect 为 null。</summary>
    public event Action<Rect?>? Finished;

    public CropPickerWindow(string imagePath)
    {
        // 内存优化（2026-10-05）：原实现整幅解码原图——用户拿 8K 照片做背景时，
        // 只为显示 ≤720×440 的框选视图就要 ~130MB BGRA 峰值驻留到窗口关闭。
        // 与 BoardWindow.DecodeBackground 同口径：BitmapDecoder 只读文件头拿原图尺寸，
        // 超过封顶宽则按 DecodePixelWidth 缩小解码，crop 输出再按 _decodeScale 换回原图坐标。
        double origW = 0;
        try
        {
            var probe = BitmapDecoder.Create(new Uri(imagePath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            if (probe.Frames.Count > 0)
                origW = probe.Frames[0].PixelWidth;
        }
        catch { /* 探测失败按未知尺寸处理 */ }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        const int capW = 1440; // 框选显示区上限 720 宽 ×2（HiDPI）再留余量
        if (origW > capW)
            bmp.DecodePixelWidth = capW;
        bmp.UriSource = new Uri(imagePath);
        bmp.EndInit();
        bmp.Freeze();
        _source = bmp;
        _decodeScale = origW > 0 ? bmp.PixelWidth / origW : 1.0;

        _dark = ThemeResolver.IsDark();
        double maxW = 720, maxH = 440;
        _scale = Math.Min(maxW / _source.Width, maxH / _source.Height);
        double dispW = Math.Max(64, Math.Round(_source.Width * _scale));
        double dispH = Math.Max(64, Math.Round(_source.Height * _scale));

        Width = dispW + 48;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var image = new Image
        {
            Source = _source,
            Width = dispW,
            Height = dispH,
            Stretch = Stretch.Fill,
        };
        var imgHost = new Grid { Width = dispW, Height = dispH, ClipToBounds = true };
        imgHost.Children.Add(image);
        imgHost.Children.Add(_overlay);
        _overlay.Children.Add(_sel);

        // 初始选区：居中 60%
        _rect = new Rect(dispW * 0.2, dispH * 0.2, dispW * 0.6, dispH * 0.6);
        LayoutSelection();
        BuildHandles();

        imgHost.MouseLeftButtonDown += (_, e) => StartDrag(Handle.Move, e);
        imgHost.MouseMove += OnMouseMove;
        imgHost.MouseLeftButtonUp += (_, e) => EndDrag();

        var title = new TextBlock
        {
            Text = I18n.Tr("set.crop.title"),
            FontSize = 13,
            Foreground = SettingsPalette.Text(_dark),
            Margin = new Thickness(4, 0, 0, 8),
        };
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                try { DragMove(); } catch { } // 仅标题区可拖窗（窗口级 handler 会吞掉按钮点击，实测踩坑）
        };
        var okBtn = MakeButton(I18n.Tr("dialog.ok"), true);
        var cancelBtn = MakeButton(I18n.Tr("dialog.cancel"), false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(okBtn);

        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(title);
        var imgWrap = new Border
        {
            Child = imgHost,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = SettingsPalette.Track(_dark),
        };
        content.Children.Add(imgWrap);
        content.Children.Add(buttons);

        var plate = new Border
        {
            Background = SettingsPalette.Bg(_dark),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = SettingsPalette.Divider(_dark),
            Child = content,
        };
        Content = plate;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finished?.Invoke(null);
                Close();
            }
        };
    }

    Border MakeButton(string text, bool isOk)
    {
        var btn = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(18, 5, 18, 5),
            Margin = new Thickness(6, 0, 0, 0),
            Background = isOk ? SettingsPalette.Accent(_dark) : SettingsPalette.Track(_dark),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = isOk ? SettingsPalette.OnAccent(_dark) : SettingsPalette.Text(_dark),
            },
        };
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Finished?.Invoke(isOk ? CropInImagePixels() : null);
            Close();
        };
        return btn;
    }

    Rect CropInImagePixels()
    {
        double denom = _scale * _decodeScale; // 显示坐标 → 原图像素（含解码缩小折算）
        return new Rect(
            Math.Max(0, Math.Round(_rect.X / denom)),
            Math.Max(0, Math.Round(_rect.Y / denom)),
            Math.Max(1, Math.Round(_rect.Width / denom)),
            Math.Max(1, Math.Round(_rect.Height / denom)));
    }

    // ---- 8 手柄 ----

    void BuildHandles()
    {
        foreach (Handle h in new[] { Handle.Nw, Handle.N, Handle.Ne, Handle.E, Handle.Se, Handle.S, Handle.Sw, Handle.W })
        {
            var thumb = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                StrokeThickness = 1.5,
                RadiusX = 2,
                RadiusY = 2,
                Tag = h,
                Cursor = h switch
                {
                    Handle.Nw or Handle.Se => Cursors.SizeNWSE,
                    Handle.Ne or Handle.Sw => Cursors.SizeNESW,
                    Handle.N or Handle.S => Cursors.SizeNS,
                    _ => Cursors.SizeWE,
                },
            };
            thumb.MouseLeftButtonDown += (_, e) => { StartDrag(h, e); e.Handled = true; };
            _overlay.Children.Add(thumb);
            _handles.Add(thumb);
        }
    }

    readonly System.Collections.Generic.List<Rectangle> _handles = new();

    void LayoutSelection()
    {
        Canvas.SetLeft(_sel, _rect.X);
        Canvas.SetTop(_sel, _rect.Y);
        _sel.Width = _rect.Width;
        _sel.Height = _rect.Height;

        double cx = _rect.X + _rect.Width / 2, cy = _rect.Y + _rect.Height / 2;
        (Handle, double, double)[] pos =
        {
            (Handle.Nw, _rect.X, _rect.Y),
            (Handle.N, cx, _rect.Y),
            (Handle.Ne, _rect.Right, _rect.Y),
            (Handle.E, _rect.Right, cy),
            (Handle.Se, _rect.Right, _rect.Bottom),
            (Handle.S, cx, _rect.Bottom),
            (Handle.Sw, _rect.X, _rect.Bottom),
            (Handle.W, _rect.X, cy),
        };
        for (int i = 0; i < _handles.Count && i < pos.Length; i++)
        {
            Canvas.SetLeft(_handles[i], pos[i].Item2 - 5);
            Canvas.SetTop(_handles[i], pos[i].Item3 - 5);
        }
    }

    void StartDrag(Handle handle, MouseEventArgs e)
    {
        _drag = handle;
        _dragStart = e.GetPosition(_overlay);
        _dragStartRect = _rect;
        _overlay.CaptureMouse();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == Handle.None || !_overlay.IsMouseCaptured) return;
        var p = e.GetPosition(_overlay);
        double dx = p.X - _dragStart.X;
        double dy = p.Y - _dragStart.Y;
        var r = _dragStartRect;
        double maxX = _overlay.ActualWidth, maxY = _overlay.ActualHeight;
        switch (_drag)
        {
            case Handle.Move:
                _rect = new Rect(
                    Math.Clamp(r.X + dx, 0, Math.Max(0, maxX - r.Width)),
                    Math.Clamp(r.Y + dy, 0, Math.Max(0, maxY - r.Height)), r.Width, r.Height);
                break;
            case Handle.Nw: _rect = RectFromEdges(r.Left + dx, r.Top + dy, r.Right, r.Bottom); break;
            case Handle.N: _rect = RectFromEdges(r.Left, r.Top + dy, r.Right, r.Bottom); break;
            case Handle.Ne: _rect = RectFromEdges(r.Left, r.Top + dy, r.Right + dx, r.Bottom); break;
            case Handle.E: _rect = RectFromEdges(r.Left, r.Top, r.Right + dx, r.Bottom); break;
            case Handle.Se: _rect = RectFromEdges(r.Left, r.Top, r.Right + dx, r.Bottom + dy); break;
            case Handle.S: _rect = RectFromEdges(r.Left, r.Top, r.Right, r.Bottom + dy); break;
            case Handle.Sw: _rect = RectFromEdges(r.Left + dx, r.Top, r.Right, r.Bottom + dy); break;
            case Handle.W: _rect = RectFromEdges(r.Left + dx, r.Top, r.Right, r.Bottom); break;
        }
        LayoutSelection();
    }

    /// <summary>由四边合成矩形（支持反拖），并施加最小尺寸与显示区夹紧。</summary>
    Rect RectFromEdges(double l, double t, double rt, double b)
    {
        double left = Math.Min(l, rt), right = Math.Max(l, rt);
        double top = Math.Min(t, b), bottom = Math.Max(t, b);
        if (right - left < MinSize) { if (l < rt) right = left + MinSize; else left = right - MinSize; }
        if (bottom - top < MinSize) { if (t < b) bottom = top + MinSize; else top = bottom - MinSize; }
        double maxX = _overlay.ActualWidth, maxY = _overlay.ActualHeight;
        left = Math.Clamp(left, 0, Math.Max(0, maxX - MinSize));
        top = Math.Clamp(top, 0, Math.Max(0, maxY - MinSize));
        right = Math.Clamp(right, left + MinSize, maxX);
        bottom = Math.Clamp(bottom, top + MinSize, maxY);
        return new Rect(left, top, right - left, bottom - top);
    }

    void EndDrag()
    {
        if (_drag == Handle.None) return;
        _drag = Handle.None;
        _overlay.ReleaseMouseCapture();
    }
}
