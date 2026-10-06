using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>折叠列表的单个选项（value=settings 键取值；preview=行尾"预览"小按钮回调，可空）。</summary>
public sealed record FoldingOption(string Value, string Label, Action? Preview = null);

/// <summary>
/// 折叠列表（步骤 05 §2；2026-10-01 改版）：头部（名称+当前值摘要+chevron）点击展开；
/// 选项卡自头部**向下滑出**覆盖下方内容（不撑大母项高度），任意子项选中即关闭。
/// 2026-10-04 修订（清单01任务12/13）：选项卡不再走 Popup（独立 HWND 会跃出设置窗），
/// 改经 <see cref="ExpanderOverlay"/> 渲染进设置窗自身可视化树（同窗、不跃出）；
/// 点击卡外任意区域=带动画关闭（承载层背景命中→CloseAll）；互斥由 CloseAll 天然成立。
/// 内容=单选卡片行+勾选，行可带"预览"小按钮。选中即收起并更新摘要。
/// </summary>
public class FoldingList : Grid
{
    public event Action<string>? SelectedChanged;

    readonly Border _header = new();
    readonly TextBlock _labelText = new() { FontSize = 12.5 };
    readonly TextBlock _summaryText = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border _chevronHost = new();
    readonly RotateTransform _chevronRotate = new();
    readonly StackPanel _optionsHost = new();
    readonly Border _popupCard = null!;
    readonly Border _shadowPlate = null!;   // 阴影底板（只含纯色板底，不带文字）
    readonly Border _contentPlate = null!;  // 内容板（文字/描边，无 Effect → 锐利）
    readonly TranslateTransform _popShift = new();
    readonly List<FoldingOption> _options = new();
    readonly ScrollViewer _cardScroll;
    IDisposable? _openToken; // 展开卡在承载层的登记（点外关闭/互斥据此收口）
    string _selected = "";
    bool _dark;
    bool _expanded;
    bool _building; // 重建选项时抑制选中事件

    public FoldingList(bool dark)
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
        var textCol = new StackPanel { Orientation = Orientation.Horizontal };
        textCol.Children.Add(_labelText);
        textCol.Children.Add(_summaryText);
        _summaryText.Margin = new Thickness(8, 0, 0, 0);
        _summaryText.MaxWidth = 150; // 窄卡片里摘要溢出按省略号收（RowRaw 的右列宽度不受挤爆）
        Grid.SetColumn(textCol, 0);
        Grid.SetColumn(_chevronHost, 1);
        _chevronHost.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(textCol);
        head.Children.Add(_chevronHost);
        _header.Child = head;
        _header.Padding = new Thickness(10, 6, 10, 6);
        _header.CornerRadius = new CornerRadius(7);
        _header.Cursor = Cursors.Hand;
        _header.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_expanded)
                CloseAnimated(); // 已展开：头部=关闭（带动画，2026-10-03）
            else
                SetExpanded(true);
        };

        // 展开卡：经 ExpanderOverlay 渲染进设置窗自身（同窗、不跃出，2026-10-04）。
        // 点外关闭由承载层背景命中统一处理；关闭一律走本类动画收口
        var scroll = new ScrollViewer
        {
            Content = _optionsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // 滚动条不渲染（2026-10-04 用户要求），滚轮滚动保留
            MaxHeight = 260,
            // 焦点框（2026-10-05 需求②）：ScrollViewer 是 Control，主题默认带虚线 FocusVisualStyle——
            // 按 Alt 弹出键盘提示时会围绕卡片画一圈虚线框，与本控件无关，直接摘掉
            FocusVisualStyle = null,
        };
        _cardScroll = scroll;

        // 阴影/内容分层（2026-10-05 需求①）：DropShadowEffect 从"含文字的卡"挪到"只含纯色板底的
        // 底板"上。原因——WPF 对带 Effect 的元素会先把整棵子树渲进中间位图再参与合成，文字一旦落进
        // 位图就丢亚像素灰阶，卡片又落于分数坐标时还会被重采样；实测同页同级文字中间灰占比 3.2%，
        // 而卡内文字高达 26.8%（暗核/中间灰 1.3~2.0 vs 0.14），观感即"文字发虚"。
        // 分层后阴影仍由同尺寸板底的 alpha 生成（观感不变），文字回到正常渲染路径。
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
            UseLayoutRounding = true,  // 落点已由 ExpanderOverlay 取整到物理像素，这里再防半像素行
            SnapsToDevicePixels = true,
        };
        _optionsHost.Margin = new Thickness(0);
        ApplyVisual();
    }

    /// <summary>头部名称（语言切换重刷）。</summary>
    public void SetLabel(string text) => _labelText.Text = text;

    /// <summary>重建选项列表（保持当前选中；selected 不在新列表则清空摘要）。</summary>
    public void SetOptions(IEnumerable<FoldingOption> options)
    {
        _building = true;
        _options.Clear();
        _optionsHost.Children.Clear();
        foreach (var opt in options)
        {
            _options.Add(opt);
            var row = BuildRow(opt);
            _optionsHost.Children.Add(row);
        }
        _building = false;
        // 选中项可能已被重建（语言切换仅改 Label），刷新摘要与勾选
        SetSelected(_selected, fireChanged: false, animate: false);
        RefreshSummary();
    }

    Border BuildRow(FoldingOption opt)
    {
        bool selected = string.Equals(opt.Value, _selected, StringComparison.Ordinal);
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 1, 0, 1),
            Background = selected ? SettingsPalette.HoverOverlay(_dark) : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = opt,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = opt.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = SettingsPalette.Text(_dark) };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        if (opt.Preview != null)
        {
            var previewBtn = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 2, 8, 2),
                Background = SettingsPalette.Track(_dark),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = Core.I18n.Tr("set.preview"),
                    FontSize = 11,
                    Foreground = SettingsPalette.TextSecondary(_dark),
                },
            };
            previewBtn.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                opt.Preview.Invoke();
            };
            previewBtn.MouseEnter += (_, _) => previewBtn.Background = SettingsPalette.HoverOverlay(_dark);
            previewBtn.MouseLeave += (_, _) => previewBtn.Background = SettingsPalette.Track(_dark);
            Grid.SetColumn(previewBtn, 1);
            grid.Children.Add(previewBtn);
        }

        var check = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,3 L3,6 L8,0"),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stroke = SettingsPalette.Accent(_dark),
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        row.Tag = (opt, check, label);
        Grid.SetColumn(check, 2);
        grid.Children.Add(check);
        row.Child = grid;

        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SetSelected(opt.Value, fireChanged: true, animate: true);
            CloseAnimated(); // 任意子项选中后关闭展开卡（带动画）
        };
        row.MouseEnter += (_, _) => { if (!IsSelected(opt)) row.Background = SettingsPalette.HoverOverlay(_dark); };
        row.MouseLeave += (_, _) => row.Background = IsSelected(opt) ? SettingsPalette.HoverOverlay(_dark) : Brushes.Transparent;
        return row;
    }

    bool IsSelected(FoldingOption opt) => string.Equals(opt.Value, _selected, StringComparison.Ordinal);

    /// <summary>设置选中项。</summary>
    public void SetSelected(string value, bool fireChanged, bool animate = true)
    {
        _selected = value;
        foreach (var child in _optionsHost.Children)
        {
            if (child is Border row && row.Tag is (FoldingOption opt, System.Windows.Shapes.Path check, TextBlock label))
            {
                bool on = IsSelected(opt);
                check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                row.Background = on ? SettingsPalette.HoverOverlay(_dark) : Brushes.Transparent;
            }
        }
        RefreshSummary();
        if (fireChanged && !_building)
            SelectedChanged?.Invoke(value);
    }

    public string SelectedValue => _selected;

    /// <summary>摘要文案（当前选中项 Label；未选中显示"—"）。</summary>
    void RefreshSummary()
    {
        foreach (var opt in _options)
        {
            if (IsSelected(opt))
            {
                _summaryText.Text = opt.Label;
                _summaryText.Foreground = SettingsPalette.TextSecondary(_dark);
                return;
            }
        }
        _summaryText.Text = "—";
    }

    /// <summary>展开=选项卡自头部向下滑出（覆盖下方项，不占布局）；收起走 <see cref="CloseAnimated"/>。</summary>
    void SetExpanded(bool expand)
    {
        if (_expanded == expand) return;
        _expanded = expand;
        if (expand)
        {
            if (_optionsHost.Children.Count == 0) { _expanded = false; return; }
            ExpanderOverlay.CloseAll(); // 互斥：收起其它已展开的卡（2026-10-04 由承载层统一收口）
            _popupCard.MinWidth = Math.Max(_header.ActualWidth, 120);
            if (_popupCard.Parent is Panel old) old.Children.Remove(_popupCard); // 关闭动画未完又重开：先摘下旧挂载
            _popupCard.Opacity = 0;
            _popShift.Y = -8;
            if (!ExpanderOverlay.TryPlace(_header, _popupCard, _cardScroll, 260, ShadowPad))
            {
                _expanded = false; // 承载层不可用：拒绝展开（防跃出同窗约束），chevron 复位
                Anim.Run(_chevronRotate, RotateTransform.AngleProperty, null, 0, AnimTuning.FoldingPop, EaseStyle.EaseInOutQuad);
                return;
            }
            _openToken = ExpanderOverlay.RegisterOpen(CloseAnimated);
            // 下滑淡入（AnimTuning.FoldingPop）
            Anim.Run(_popShift, TranslateTransform.YProperty, -8, 0, AnimTuning.FoldingPop, EaseStyle.EaseOutCubic);
            Anim.Run(_popupCard, OpacityProperty, 0, 1, AnimTuning.FoldingPop, EaseStyle.EaseOutCubic);
            Anim.Run(_chevronRotate, RotateTransform.AngleProperty, null, 180, AnimTuning.FoldingPop, EaseStyle.EaseInOutQuad);
        }
        else
        {
            CloseAnimated(); // 2026-10-04：收口统一走动画（含从承载层摘除）
        }
    }

    /// <summary>展开卡在承载层内的四周阴影余量（DropShadowEffect Blur14+Depth3 → 16 足够）。</summary>
    const double ShadowPad = 16;

    /// <summary>关闭展开卡（带动画）：淡出+上收 8px 后从承载层摘除，chevron 同步收回。
    /// 点外关闭（承载层 CloseAll）、互斥、再次点头部、子项选中都走这里；重复调用安全。</summary>
    void CloseAnimated()
    {
        if (!_expanded)
        {
            if (_openToken == null) return; // 未展开也未登记：无事可做
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
                host.Children.Remove(_popupCard); // 从承载层摘除（Popup 时代的 IsOpen=false 对应物）
            _popupCard.Opacity = 1; // 复位，下次展开由 SetExpanded 重新淡入
            _popShift.Y = -8;
        });
    }

    /// <summary>收起（语言/主题切换或页面切走时调用）。</summary>
    public void Collapse() => CloseAnimated();

    public void SetTheme(bool dark)
    {
        _dark = dark;
        ApplyVisual();
        foreach (var child in _optionsHost.Children)
            if (child is Border row && row.Tag is (FoldingOption opt, System.Windows.Shapes.Path check, TextBlock label))
            {
                bool on = IsSelected(opt);
                row.Background = on ? SettingsPalette.HoverOverlay(_dark) : Brushes.Transparent;
                check.Stroke = SettingsPalette.Accent(_dark);
                label.Foreground = SettingsPalette.Text(_dark); // 折叠项文字随主题（2026-10-02）
            }
    }

    void ApplyVisual()
    {
        _header.Background = SettingsPalette.Card(_dark);
        _header.BorderBrush = SettingsPalette.Divider(_dark);
        _header.BorderThickness = new Thickness(1);
        _shadowPlate.Background = SettingsPalette.Bg(_dark);          // 板底（阴影来源）在底板
        _contentPlate.BorderBrush = SettingsPalette.Divider(_dark);   // 描边留无 Effect 的内容板：描边同样保持锐利
        _contentPlate.BorderThickness = new Thickness(1);
        _labelText.Foreground = SettingsPalette.Text(_dark);
        _summaryText.Foreground = SettingsPalette.TextSecondary(_dark);
        if (_chevronHost.Child is System.Windows.Shapes.Path chevron)
            chevron.Stroke = SettingsPalette.TextSecondary(_dark);
    }
}
