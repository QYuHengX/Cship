using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 大按钮折叠项（2026-10-06 关于页扩展内容）：整行"大按钮"头部（标题左 + chevron 右），点击后
/// 内容卡自头部向下滑出（覆盖下方内容、不撑大页面高度）——经 <see cref="ExpanderOverlay"/>
/// 渲染进设置窗自身可视化树（同窗不跃出、点外关闭、互斥），交互口径与 <see cref="FoldingList"/>
/// 完全一致；内容为调用方装配的任意 UIElement（区别于 FoldingList 的"选项行"）。
/// 阴影/内容分层同 FoldingList（2026-10-05 需求①）：Effect 只落在纯色板底，文字保持锐利。
/// </summary>
public class InfoExpander : Grid
{
    readonly Border _header = new();
    readonly TextBlock _titleText = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    readonly Border _chevronHost = new();
    readonly RotateTransform _chevronRotate = new();
    readonly StackPanel _contentHost = new();
    readonly ScrollViewer _cardScroll;
    readonly Border _shadowPlate = null!;   // 阴影底板（只含纯色板底，不带文字）
    readonly Border _contentPlate = null!;  // 内容板（文字/描边，无 Effect → 锐利）
    readonly TranslateTransform _popShift = new();
    readonly Border _popupCard = null!;
    IDisposable? _openToken;
    bool _dark;
    bool _expanded;

    public InfoExpander(bool dark)
    {
        _dark = dark;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_header);
        SetRow(_header, 0);

        var chevron = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L4,4 L8,0"),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stroke = SettingsPalette.TextSecondary(dark),
            RenderTransform = _chevronRotate,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        _chevronHost.Child = chevron;
        _chevronHost.Width = 12;
        _chevronHost.Margin = new Thickness(6, 0, 0, 0);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_titleText, 0);
        Grid.SetColumn(_chevronHost, 1);
        _chevronHost.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_titleText);
        head.Children.Add(_chevronHost);
        _header.Child = head;
        // "大按钮"观感：整行可点、比普通设置行更高的内边距与圆角
        _header.Padding = new Thickness(12, 9, 12, 9);
        _header.CornerRadius = new CornerRadius(8);
        _header.Cursor = Cursors.Hand;
        _header.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_expanded)
                CloseAnimated(); // 已展开：头部=关闭（带动画）
            else
                SetExpanded(true);
        };
        _header.MouseEnter += (_, _) => ApplyHeaderVisual(hover: true);
        _header.MouseLeave += (_, _) => ApplyHeaderVisual();

        var scroll = new ScrollViewer
        {
            Content = _contentHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // 滚动条不渲染（同 FoldingList），滚轮滚动保留
            MaxHeight = 300,
            FocusVisualStyle = null,
        };
        _cardScroll = scroll;

        _shadowPlate = new Border
        {
            CornerRadius = new CornerRadius(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.28, Direction = 270 },
        };
        _contentPlate = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = scroll,
            SnapsToDevicePixels = true,
        };
        var cardStack = new Grid();
        cardStack.Children.Add(_shadowPlate);
        cardStack.Children.Add(_contentPlate);
        _popupCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = cardStack,
            RenderTransform = _popShift,
            Opacity = 0,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        ApplyVisual();
    }

    /// <summary>头部标题（语言切换重刷）。</summary>
    public void SetTitle(string text) => _titleText.Text = text;

    /// <summary>替换展开卡内容（关于页内容随语言重建时调用）。</summary>
    public void SetContent(FrameworkElement content)
    {
        _contentHost.Children.Clear();
        _contentHost.Children.Add(content);
    }

    /// <summary>展开：内容卡自头部向下滑出（覆盖下方项，不占布局）；收起走 <see cref="CloseAnimated"/>。</summary>
    void SetExpanded(bool expand)
    {
        if (_expanded == expand) return;
        _expanded = expand;
        if (expand)
        {
            if (_contentHost.Children.Count == 0) { _expanded = false; return; }
            ExpanderOverlay.CloseAll(); // 互斥：收起其它已展开的卡
            _popupCard.MinWidth = Math.Max(_header.ActualWidth, 120);
            if (_popupCard.Parent is Panel old) old.Children.Remove(_popupCard);
            _popupCard.Opacity = 0;
            _popShift.Y = -8;
            if (!ExpanderOverlay.TryPlace(_header, _popupCard, _cardScroll, 300, ShadowPad))
            {
                _expanded = false; // 承载层不可用：拒绝展开，chevron 复位
                Anim.Run(_chevronRotate, RotateTransform.AngleProperty, null, 0, AnimTuning.FoldingPop, EaseStyle.EaseInOutQuad);
                return;
            }
            _openToken = ExpanderOverlay.RegisterOpen(CloseAnimated);
            Anim.Run(_popShift, TranslateTransform.YProperty, -8, 0, AnimTuning.FoldingPop, EaseStyle.EaseOutCubic);
            Anim.Run(_popupCard, OpacityProperty, 0, 1, AnimTuning.FoldingPop, EaseStyle.EaseOutCubic);
            Anim.Run(_chevronRotate, RotateTransform.AngleProperty, null, 180, AnimTuning.FoldingPop, EaseStyle.EaseInOutQuad);
        }
        else
        {
            CloseAnimated();
        }
    }

    /// <summary>展开卡在承载层内的四周阴影余量（同 FoldingList）。</summary>
    const double ShadowPad = 16;

    /// <summary>关闭展开卡（带动画）：淡出+上收 8px 后从承载层摘除；重复调用安全。</summary>
    void CloseAnimated()
    {
        if (!_expanded)
        {
            if (_openToken == null) return;
        }
        _expanded = false;
        _openToken?.Dispose();
        _openToken = null;
        Anim.Run(_popShift, TranslateTransform.YProperty, null, -8, AnimTuning.FoldingPop, EaseStyle.EaseInQuad);
        Anim.Run(_chevronRotate, RotateTransform.AngleProperty, null, 0, AnimTuning.FoldingPop, EaseStyle.EaseInOutQuad);
        Anim.Run(_popupCard, OpacityProperty, null, 0, AnimTuning.FoldingPop, EaseStyle.EaseInQuad, onDone: () =>
        {
            if (_expanded) return; // 关闭动画期间又被展开：别把新卡关掉
            if (_popupCard.Parent is Panel host)
                host.Children.Remove(_popupCard);
            _popupCard.Opacity = 1;
            _popShift.Y = -8;
        });
    }

    /// <summary>收起（页面切走时由 ExpanderOverlay.CloseAll 统一收口；此处供显式调用）。</summary>
    public void Collapse() => CloseAnimated();

    public void SetTheme(bool dark)
    {
        _dark = dark;
        ApplyVisual();
    }

    void ApplyHeaderVisual(bool hover = false)
    {
        _header.Background = hover ? SettingsPalette.HoverOverlay(_dark) : SettingsPalette.Track(_dark);
    }

    void ApplyVisual()
    {
        ApplyHeaderVisual();
        _header.BorderBrush = SettingsPalette.Divider(_dark);
        _header.BorderThickness = new Thickness(1);
        _shadowPlate.Background = SettingsPalette.Bg(_dark);
        _contentPlate.BorderBrush = SettingsPalette.Divider(_dark);
        _contentPlate.BorderThickness = new Thickness(1);
        _titleText.Foreground = SettingsPalette.Text(_dark);
        if (_chevronHost.Child is System.Windows.Shapes.Path chevron)
            chevron.Stroke = SettingsPalette.TextSecondary(_dark);
    }
}
