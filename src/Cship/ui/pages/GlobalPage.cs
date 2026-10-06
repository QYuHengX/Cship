using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Cship.Core;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 全局设置页（步骤 05 §3.1，原文 L26-33）：主题四选预览卡 / 开机自启动 / 刷新率 / 语言 / 字体 / 隐藏桌面图标。
/// </summary>
public class GlobalPage : SettingsPageBase
{
    ToggleSwitch _autostart = null!;
    ToggleSwitch _hideIcons = null!;
    FoldingList _fps = null!;
    FoldingList _lang = null!;
    FoldingList _font = null!;
    ThemeCardView[] _themeViews = null!;

    /// <summary>主题四选（2026-10-06：06 步加的"透明"档已按用户要求整体删除，
    /// 界面颜色回到 light/dark/auto/system 四档）。</summary>
    static readonly (string Mode, string Key)[] ThemeModes =
    {
        ("light", "set.theme.light"),
        ("dark", "set.theme.dark"),
        ("auto", "set.theme.auto"),
        ("system", "set.theme.system"),
    };

    public GlobalPage()
    {
        var body = PageBody();

        // ---- 界面颜色：4 选 1 预览卡 ----
        var themeSection = Section("set.theme");
        BodyOf(themeSection).Children.Add(BuildThemeCardRow());
        body.Children.Add(themeSection);

        // ---- 开机自启动 ----
        var autoSection = Section("set.general");
        _autostart = new ToggleSwitch { IsChecked = Autostart.IsEnabled() };
        _autostart.CheckedChanged += AutostartWriteback;
        Row(BodyOf(autoSection), "set.autostart", _autostart);

        // ---- 刷新率 ----
        _fps = new FoldingList(Dark);
        _fps.SetOptions(new[]
        {
            new FoldingOption("60", "60"),
            new FoldingOption("90", "90"),
            new FoldingOption("120", "120"),
            new FoldingOption("165", "165"),
        });
        _fps.SetSelected(SettingsStore.Instance.GetInt("general.fps", 60).ToString(), fireChanged: false);
        _fps.SelectedChanged += v =>
        {
            SettingsStore.Instance.SetInt("general.fps", int.Parse(v));
            _fps.Collapse();
        };
        RowRaw(BodyOf(autoSection), Label("set.fps"), _fps);

        // ---- 语言 ----
        _lang = new FoldingList(Dark);
        _lang.SetOptions(new[]
        {
            new FoldingOption("zh-CN", I18n.Tr("set.lang.zh")),
            new FoldingOption("en-US", I18n.Tr("set.lang.en")),
        });
        _lang.SetSelected(SettingsStore.Instance.GetString("general.language", "zh-CN"), fireChanged: false);
        _lang.SelectedChanged += v =>
        {
            // 只写存储：语言切换的副作用（I18n.SetLanguage → 全局文案刷新）收口在 App 的 SettingsChanged，
            // 这样"导入配置 / 恢复默认"改到语言时同样生效（2026-10-06）
            SettingsStore.Instance.SetString("general.language", v);
            _lang.Collapse();
        };
        RowRaw(BodyOf(autoSection), Label("set.language"), _lang);

        // ---- 字体 ----
        _font = new FoldingList(Dark);
        _font.SetOptions(BuildFontOptions());
        _font.SetSelected(SettingsStore.Instance.GetString("general.font", "Microsoft YaHei"), fireChanged: false);
        _font.SelectedChanged += v =>
        {
            SettingsStore.Instance.SetString("general.font", v);
            _font.Collapse();
        };
        RowRaw(BodyOf(autoSection), Label("set.font"), _font);

        // ---- 隐藏桌面图标 ----
        _hideIcons = new ToggleSwitch { IsChecked = SettingsStore.Instance.GetBool("general.hideDesktopIcons", false) };
        _hideIcons.CheckedChanged += on =>
        {
            // 系统侧效果（SetDesktopIconsVisible）收口在 App 的 SettingsChanged（2026-10-06）
            SettingsStore.Instance.SetBool("general.hideDesktopIcons", on);
        };
        Row(BodyOf(autoSection), "set.hideIcons", _hideIcons);
        body.Children.Add(autoSection);

        Content = ScrollWrap(body);
        I18n.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => I18n.LanguageChanged -= OnLanguageChanged;
        RefreshText();
    }

    /// <summary>换页时由 SettingsWindow 显式解绑：Unloaded 在"页面尚未完成首次布局就被换掉"
    /// 的时序下不触发，构造期订阅的静态事件会把本页钉在进程里（幂等）。</summary>
    public override void Detach() => I18n.LanguageChanged -= OnLanguageChanged;

    TextBlock Label(string key)
    {
        var label = new TextBlock { FontSize = 12.5, Text = I18n.Tr(key), Foreground = SettingsPalette.Text(Dark) };
        _texts.Add((label, key));
        return label;
    }

    void OnLanguageChanged()
    {
        // 语言/字体选项的显示名随语言变化，需要重建 FoldingList 内容（保持选中）
        RebuildLanguageOptions();
        RebuildFontOptions();
        RefreshText();
    }

    void RebuildLanguageOptions()
    {
        _lang.SetOptions(new[]
        {
            new FoldingOption("zh-CN", I18n.Tr("set.lang.zh")),
            new FoldingOption("en-US", I18n.Tr("set.lang.en")),
        });
        _lang.SetSelected(SettingsStore.Instance.GetString("general.language", "zh-CN"), fireChanged: false);
    }

    void RebuildFontOptions()
    {
        _font.SetOptions(BuildFontOptions());
        _font.SetSelected(SettingsStore.Instance.GetString("general.font", "Microsoft YaHei"), fireChanged: false);
    }

    FoldingOption[] BuildFontOptions()
    {
        (string Id, string Key)[] fonts =
        {
            ("Microsoft YaHei", "font.msYaHei"),
            ("DengXian", "font.dengXian"),
            ("SimSun", "font.simSun"),
            ("SimHei", "font.simHei"),
            ("KaiTi", "font.kaiTi"),
            ("Segoe UI", "font.segoeUi"),
            ("Consolas", "font.consolas"),
        };
        var result = new FoldingOption[fonts.Length];
        for (int i = 0; i < fonts.Length; i++)
            result[i] = new FoldingOption(fonts[i].Id, I18n.Tr(fonts[i].Key));
        return result;
    }

    // ---- 主题四选预览卡（2026-10-05 重绘，口径"简约明了"）----
    // 四张卡共用同一套语言，不再各画各的图标：圆角画布 + 一枚"迷你界面"缩略图
    // （窗口 = 标题条 + 内容区），先让人看懂"界面会变什么样"，再用一处差异点区分模式——
    //   白皙 / 暗夜     = 纯亮 / 纯暗界面
    //   自动            = 左亮右暗硬分界（会切换）+ 日月（按时段）
    //   跟随系统        = 左亮右暗硬分界（会切换）+ 四格徽标（按系统）
    // 选中效果：2px 描边**恒定粗细只换颜色**（旧实现 1→2px 会让卡内内容位移 1px）、
    // 右上角勾选徽标、卡名转为正文色半粗——三处线索同时变化，扫一眼就能认出选中的是哪个。
    // 2026-10-06：原"透明"档卡（棋盘格画布 + 发丝描边迷你窗）随透明主题一并删除。

    /// <summary>单张主题卡的三个可变部件（选中态只动这三个，预览图本身不变色不变形）。</summary>
    sealed class ThemeCardView
    {
        public Border Root = null!;
        public FrameworkElement Badge = null!;
        public TextBlock Caption = null!;
    }

    /// <summary>构建主题预览卡行（4 等分星号列布满整行宽；旧固定 92px 横排在窄窗里溢出）。</summary>
    UIElement BuildThemeCardRow()
    {
        _themeViews = new ThemeCardView[ThemeModes.Length];
        var views = _themeViews;
        var grid = new Grid { Margin = new Thickness(2, 0, 0, 4) };
        for (int i = 0; i < ThemeModes.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 卡片
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 说明文字
        for (int i = 0; i < ThemeModes.Length; i++)
        {
            var (mode, key) = ThemeModes[i];
            var badge = SelectionBadge(); // 选中徽标（右上角，默认隐藏）
            var preview = new Grid();
            preview.Children.Add(CanvasLayer(mode));
            preview.Children.Add(MiniWindow(mode));
            preview.Children.Add(badge);
            var card = new Border
            {
                Height = 68,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2), // 恒定：选中只换颜色，卡片内布局零位移
                Margin = new Thickness(0, 0, i == ThemeModes.Length - 1 ? 0 : 10, 0),
                Child = preview,
                Cursor = Cursors.Hand,
            };
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SettingsStore.Instance.SetString("theme.mode", mode); // ※ ThemeFade 过渡由各窗口接管
                ApplySelection(views);
            };

            var cap = new TextBlock
            {
                FontSize = 10.5,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, i == ThemeModes.Length - 1 ? 0 : 10, 0),
            };
            _texts.Add((cap, key));
            views[i] = new ThemeCardView { Root = card, Badge = badge, Caption = cap };

            Grid.SetColumn(card, i);
            Grid.SetRow(card, 0);
            grid.Children.Add(card);
            Grid.SetColumn(cap, i);
            Grid.SetRow(cap, 1);
            grid.Children.Add(cap);
        }
        ApplySelection(views);
        return grid;
    }

    void ApplySelection(ThemeCardView[]? views = null)
    {
        views ??= _themeViews;
        string current = SettingsStore.Instance.GetString("theme.mode", "light");
        for (int i = 0; i < ThemeModes.Length; i++)
        {
            bool on = string.Equals(ThemeModes[i].Mode, current, StringComparison.OrdinalIgnoreCase);
            var view = views[i];
            view.Root.BorderBrush = on ? SettingsPalette.Accent(Dark) : SettingsPalette.Divider(Dark);
            view.Badge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            view.Caption.Foreground = on ? SettingsPalette.Text(Dark) : SettingsPalette.TextSecondary(Dark);
            view.Caption.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // ---- 预览图绘制（纯几何，无位图素材；像素尺寸固定，1px 描边始终锐利）----

    // 画布（卡片底）与"界面"面：亮/暗两套取自 00 §9 调色板的中性面，保证四张卡放在
    // 浅色设置卡片上也彼此可辨（白皙若用纯白会与卡片底糊在一起）；暗色一侧刻意拉开
    // 画布/窗口/标题条/内容线四级明度，否则"黑底黑窗"缩略图看不清结构。
    const string CanvasLight = "#FFF0F2F6";
    const string CanvasDark = "#FF22262C";
    const string FaceLight = "#FFFFFFFF";
    const string FaceDark = "#FF3A424F";
    const string FrameLight = "#FFDCE1E8";
    const string FrameDark = "#FF5A6371";
    const string FrameSplit = "#FF98A2AF"; // 分屏窗口的框：一个中间色，两半上都看得见
    const string StripLight = "#FFE7EBF1";
    const string StripDark = "#FF4B5464";
    const string LineLight = "#FFC6CDD8";
    const string LineDark = "#FF8A94A4";

    /// <summary>画布层：白皙/暗夜为纯色底；自动/跟随系统为"左亮右暗"硬分界（表达"会随条件切换"）。</summary>
    static Rectangle CanvasLayer(string mode) => new()
    {
        RadiusX = 6,
        RadiusY = 6,
        Fill = mode switch
        {
            "light" => SettingsPalette.Freeze(CanvasLight),
            "dark" => SettingsPalette.Freeze(CanvasDark),
            _ => HardSplit(CanvasLight, CanvasDark),
        },
    };

    /// <summary>左右各半的硬分界渐变：0.5 处双停靠连成一条直线，无接缝、免拼接圆角缺口。</summary>
    static LinearGradientBrush HardSplit(string left, string right)
    {
        var l = (Color)ColorConverter.ConvertFromString(left);
        var r = (Color)ColorConverter.ConvertFromString(right);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(l, 0));
        brush.GradientStops.Add(new GradientStop(l, 0.5));
        brush.GradientStops.Add(new GradientStop(r, 0.5));
        brush.GradientStops.Add(new GradientStop(r, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>迷你界面缩略图：一枚 50×32 的窗口（1px 框 + 圆角 + 标题条）居中在卡片上；
    /// 自动/跟随系统的窗口、标题条、内容一律左右分屏，与画布同一刀切。</summary>
    static FrameworkElement MiniWindow(string mode)
    {
        bool split = mode is "auto" or "system";
        bool dark = mode == "dark";
        var win = new Border
        {
            Width = 50,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = SettingsPalette.Freeze(split ? FrameSplit : dark ? FrameDark : FrameLight),
            Background = split ? HardSplit(FaceLight, FaceDark)
                : SettingsPalette.Freeze(dark ? FaceDark : FaceLight),
        };

        var inner = new Grid();
        inner.Children.Add(new Border
        {
            Height = 8,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            Background = split ? HardSplit(StripLight, StripDark)
                : SettingsPalette.Freeze(dark ? StripDark : StripLight),
        });
        switch (mode)
        {
            case "auto": // 日月：亮半边=太阳，暗半边=月亮（按时段）
                var pair = new Grid { Margin = new Thickness(3, 8, 3, 0) };
                pair.ColumnDefinitions.Add(new ColumnDefinition());
                pair.ColumnDefinitions.Add(new ColumnDefinition());
                var sun = new Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Fill = SettingsPalette.Freeze("#FFFFC24A"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var moon = Crescent();
                moon.HorizontalAlignment = HorizontalAlignment.Center;
                moon.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(sun, 0);
                Grid.SetColumn(moon, 1);
                pair.Children.Add(sun);
                pair.Children.Add(moon);
                inner.Children.Add(pair);
                break;
            case "system": // 四格徽标：左右两列各自取所在半边的对比色（像界面一样"跟着变"）
                inner.Children.Add(SystemMark());
                break;
            default: // 白皙/暗夜：两条内容线，即"这个界面的观感"
                inner.Children.Add(ContentLines(dark ? LineDark : LineLight));
                break;
        }
        win.Child = inner;
        return win;
    }

    /// <summary>月牙：实心圆 + 一枚与所在半边同色的遮挡圆（自动卡的右半边=暗面）。</summary>
    static FrameworkElement Crescent()
    {
        var grid = new Grid { Width = 9, Height = 9 };
        grid.Children.Add(new Ellipse { Fill = SettingsPalette.Freeze("#FFE6EAF2") });
        grid.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = SettingsPalette.Freeze(FaceDark),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(3.4, 0, 0, 0),
        });
        return grid;
    }

    /// <summary>两条内容线（宽度不等，像一行标题 + 一行正文）。</summary>
    static FrameworkElement ContentLines(string color)
    {
        var lines = new StackPanel { Margin = new Thickness(7, 11, 7, 0), VerticalAlignment = VerticalAlignment.Top };
        lines.Children.Add(new Rectangle { Width = 28, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = SettingsPalette.Freeze(color), HorizontalAlignment = HorizontalAlignment.Left });
        lines.Children.Add(new Rectangle { Width = 17, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = SettingsPalette.Freeze(color), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) });
        return lines;
    }

    /// <summary>四格徽标（系统标记）：2×2 圆角小方块，左列取深色（压在亮半边）、右列取浅色（压在暗半边）。</summary>
    static FrameworkElement SystemMark()
    {
        var mark = new Grid
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        mark.ColumnDefinitions.Add(new ColumnDefinition());
        mark.ColumnDefinitions.Add(new ColumnDefinition());
        mark.RowDefinitions.Add(new RowDefinition());
        mark.RowDefinitions.Add(new RowDefinition());
        void Cell(int row, int col, string fill, Thickness margin)
        {
            var cell = new Rectangle { RadiusX = 1, RadiusY = 1, Fill = SettingsPalette.Freeze(fill), Margin = margin };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            mark.Children.Add(cell);
        }
        Cell(0, 0, "#FF4A5160", new Thickness(0, 0, 1, 1));
        Cell(0, 1, "#FFC9D0DA", new Thickness(1, 0, 0, 1));
        Cell(1, 0, "#FF4A5160", new Thickness(0, 1, 1, 0));
        Cell(1, 1, "#FFC9D0DA", new Thickness(1, 1, 0, 0));
        return mark;
    }

    /// <summary>选中徽标：强调色圆底 + 勾（浅色主题=深底白勾，深色主题=浅底深勾，取 OnAccent 对色）。</summary>
    FrameworkElement SelectionBadge()
    {
        var badge = new Grid
        {
            Width = 15,
            Height = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Visibility = Visibility.Collapsed,
        };
        badge.Children.Add(new Ellipse { Fill = SettingsPalette.Accent(Dark) });
        badge.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,2.5 L2.1,4.6 L6,0.6"),
            Stroke = SettingsPalette.OnAccent(Dark),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return badge;
    }

    public override void RefreshDynamic() { }

    public override void ReloadFromSettings()
    {
        _autostart.IsChecked = Autostart.IsEnabled();
        _autostart.CheckedChanged -= AutostartWriteback; // 防回环：同步期间不触发写回
        _autostart.IsChecked = Autostart.IsEnabled();
        _autostart.CheckedChanged += AutostartWriteback;
        _fps.SetSelected(SettingsStore.Instance.GetInt("general.fps", 60).ToString(), fireChanged: false);
        _lang.SetSelected(SettingsStore.Instance.GetString("general.language", "zh-CN"), fireChanged: false);
        _font.SetSelected(SettingsStore.Instance.GetString("general.font", "Microsoft YaHei"), fireChanged: false);
        _hideIcons.IsChecked = SettingsStore.Instance.GetBool("general.hideDesktopIcons", false);
        ApplySelection(_themeViews);
    }

    void AutostartWriteback(bool on)
    {
        // 注册表写入收口在 App 的 SettingsChanged（2026-10-06）；此处只写存储
        SettingsStore.Instance.SetBool("general.autostart", on);
    }
}
