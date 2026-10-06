using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Cship.Core;
using Cship.Ui.Animations;
using Cship.Ui.Components;
using Cship.Ui.Pages;

namespace Cship.Ui;

/// <summary>
/// 设置窗口（步骤 05 §1；2026-10-01 改版）：尺寸缩至原 2/3（600×413，仍固定不可 resize）；
/// 出入场仿 mac（0.94 缩放+淡入 / 缩至 0.96 淡出，关闭=仅隐藏、重复打开=置前）；
/// 左列导航选中/悬停带透明度过渡；右列分页切换=淡出→换页→滑入过渡；
/// 恒置顶且始终压在收纳板之上（2026-10-02，RaiseAbove 按 HWND 校正）。
/// mac 风左上三色圆点（红点=关闭设置窗）。
/// </summary>
public class SettingsWindow : GlassWindow, IEscHandler
{
    // 窗口=视觉板 600×413 + 四周阴影落脚余量（2026-10-04 清单01任务5）：DropShadowEffect
    // （Blur24+Depth5）在窗口与板同尺寸时被窗口边界硬切，四角出现深灰"脏块"——余量给足后
    // 阴影完整落进窗口，板尺寸/居中观感不变（CenterOn 用 W/H 常量自动兼容）
    const double W = 648, H = 467;           // 视觉板 600×413 + 阴影余量
    const double PlateMarginL = 24, PlateMarginT = 24, PlateMarginR = 24, PlateMarginB = 30; // 底=Blur24+Depth5
    const double TitleBarH = 36;
    const double NavW = 150;

    Border _plate = null!;
    MaterialBackground _material = null!; // 步骤 06：设置窗材质板底（模拟通道）
    Grid _shell = null!; // 窗口内容根（含视觉板与其阴影余量）：主题/语言快照过渡的挂载点
    Canvas _overlay = null!; // 展开卡承载层（同窗渲染不跃出，2026-10-04）
    StackPanel _navHost = null!;
    ContentControl _pageHost = null!;
    readonly List<NavRow> _navs = new();
    SettingsPageBase? _currentPage;
    string _currentId = "global";
    bool _dark;
    int _hideGen; // 消失动画代次：Present 用它打断挂起的隐藏回调

    sealed class NavRow
    {
        public required string Id;
        public required string LabelKey;
        public required FrameworkElement Row;
        public required Border Hover;  // 悬停底（透明度过渡）
        public required Border Sel;    // 选中底（透明度过渡）
        public required Rectangle Bar; // 强调色竖条（随选中淡入淡出）
        public required TextBlock Label;
        public required Func<SettingsPageBase> Create;
    }

    public SettingsWindow()
    {
        Width = W;
        Height = H;
        MinWidth = MaxWidth = W;
        MinHeight = MaxHeight = H;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        // Topmost 不再恒置顶：跟随 advanced.dockPriority（2026-10-01，与收纳板同口径），
        // 句柄建立时由 ApplyTopmostStrategy 落定，设置切换经 OnSettingChanged 实时生效
        _dark = ThemeResolver.IsDark();
        FontFamily = new FontFamily(SettingsStore.Instance.GetString("general.font", "Microsoft YaHei"));

        BuildLayout();
        ApplyTheme();
        SelectNav("global", animate: false);

        SettingsStore.Instance.SettingsChanged += OnSettingChanged;
        I18n.LanguageChanged += OnLanguageChanged;
        ThemeAuto.DarkChanged += OnAutoDarkChanged; // auto 时段翻转同款过渡（清单05任务3）
        AdvancedPage.ImportedConfigHotReload += OnConfigImported;
        AdvancedPage.DefaultsRestored += OnDefaultsRestored; // 恢复默认（2026-10-06）
        Closed += (_, _) =>
        {
            SettingsStore.Instance.SettingsChanged -= OnSettingChanged;
            I18n.LanguageChanged -= OnLanguageChanged;
            ThemeAuto.DarkChanged -= OnAutoDarkChanged;
            AdvancedPage.ImportedConfigHotReload -= OnConfigImported;
            AdvancedPage.DefaultsRestored -= OnDefaultsRestored;
            ShortcutRouter.Unregister(this);
            ExpanderOverlay.Detach(_overlay); // 清残留展开卡并解除承载层
        };
    }

    /// <summary>auto 模式时段翻转（06:00~18:00 为亮）：与手动切主题同走快照过渡（清单05任务3）。</summary>
    void OnAutoDarkChanged(bool dark)
        => Dispatcher.BeginInvoke(() => RunWithFade(RebuildForTheme));

    /// <summary>设置窗跟随"窗口优先级"（2026-10-03：advanced.dockPriority 更名并改语义——
    /// 收纳板与设置窗跟随、悬浮窗固定桌面级）：low=不置顶，medium/high=置顶。
    /// 同为置顶层时由 <see cref="RaiseAbove"/> 按 HWND 次序保证设置窗始终压在收纳板之上。</summary>
    protected override void ApplyTopmostStrategy()
        => Topmost = !string.Equals(
            SettingsStore.Instance.GetString("advanced.dockPriority", "low"), "low", StringComparison.OrdinalIgnoreCase);

    // ---- 全屏前台退让（07 §8 方案 B，2026-10-05 用户裁决）----

    bool _fullScreenRetreat;

    /// <summary>重新按 <c>advanced.dockPriority</c> 落定置顶态（全屏退让恢复时调用）。</summary>
    public void ReapplyPriority() => ApplyTopmostStrategy();

    /// <summary>
    /// 进入所选屏真全屏前台时让置顶中的设置窗临时退层；全屏退出后按优先级档重新落定
    /// （不写死 true，low 档仍保持不置顶）。由 <c>App.OnFullScreenTick</c> 每秒轮询驱动。
    /// </summary>
    public void SetFullScreenRetreat(bool retreat)
    {
        if (_fullScreenRetreat == retreat) return;
        _fullScreenRetreat = retreat;
        if (retreat)
        {
            Topmost = false;
            Logger.Info("全屏前台：设置窗临时退层（Topmost=false）");
        }
        else
        {
            ReapplyPriority();
            Logger.Info("全屏退出：设置窗恢复优先级档");
        }
    }

    /// <summary>把设置窗放到收纳板 HWND 的正上方（不激活、不动位置尺寸）。
    /// 2026-10-04 错误修复⑧：旧实现直接 `SetWindowPosAfter(hwnd, boardHwnd)`，而 Win32 的
    /// `hWndInsertAfter` 语义是"排在目标窗口**之上**的窗口"——传板句柄实际把设置窗压到**板下方**
    /// （实测 EnumWindows 顺序确认）。改为取"板正上方那个窗口"作插入点，并先判定是否已在板上方
    /// （在则不动，免得把刚激活的设置窗降到别的窗口之下）。</summary>
    public void RaiseAbove(Window? board)
    {
        if (!IsVisible || board is not { IsVisible: true }) return;
        var boardHwnd = new System.Windows.Interop.WindowInteropHelper(board).Handle;
        if (boardHwnd == IntPtr.Zero) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || hwnd == boardHwnd) return;
        if (SystemBridge.IsWindowAbove(hwnd, boardHwnd)) return; // 已在板上方：保持现状
        SystemBridge.SetWindowPosAbove(hwnd, boardHwnd);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ShortcutRouter.Register(this, EscLayer.Settings); // 00 §6：设置窗 > 收纳板
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = ShortcutRouter.EscPressed(); // 设置窗优先消费：隐藏设置窗
        };
    }

    public bool TryHandleEsc()
    {
        HideAnimated();
        return true;
    }

    /// <summary>
    /// 从存储重载当前页控件值（07 §1：自启动写键失败时把开关回滚为注册表里的实际值）。
    /// 设置窗未打开/无当前页时为 no-op。
    /// </summary>
    public void ReloadCurrentPage() => _currentPage?.ReloadFromSettings();

    /// <summary>打开（重复打开=置前）：居中于收纳板所在屏，并从存储重载当前页控件值。
    /// 首次显示播仿 mac 出场（0.94 缩放+淡入）。</summary>
    public void Present(Window? board)
    {
        CenterOn(board);
        _hideGen++; // 打断挂起的消失动画回调
        // 闭合动画进行中被再次打开（2026-10-04）：仅打断 Hide 回调不够——半径动画会继续播完归 0，
        // 窗口停留成"IsVisible=true 但遮罩全透明"的僵死态，此后 Present 恒走"已可见"分支永远打不开。
        // 必须停掉半径动画并复位遮罩/透明度，窗口才真正恢复可见。
        if (_plate.OpacityMask != null)
        {
            _openMask.BeginAnimation(System.Windows.Media.RadialGradientBrush.RadiusXProperty, null);
            _openMask.BeginAnimation(System.Windows.Media.RadialGradientBrush.RadiusYProperty, null);
            _plate.OpacityMask = null;
        }
        _plate.BeginAnimation(OpacityProperty, null);
        _plate.Opacity = 1;
        if (!IsVisible)
        {
            PlayOpen();
            Show();
        }
        else
        {
            HotkeyBox.CancelActive(); // 已可见时再次 Present（置前）也要收口录入态
        }
        Activate();
        RaiseAbove(board); // 兜底：确保在收纳板之上（2026-10-04 错误修复⑧；已在板上方则 no-op）
        _currentPage?.ReloadFromSettings();
        Logger.Info("打开设置窗口");
    }

    /// <summary>仿 mac 出场（2026-10-02 重绘）：**由中间向四周**的径向渐显——中心先亮、
    /// 透明度向四周逐渐降低，半径展开至覆满全窗（无缩放）。关闭=同一遮罩逆向收回。</summary>
    readonly System.Windows.Media.RadialGradientBrush _openMask = new()
    {
        MappingMode = BrushMappingMode.RelativeToBoundingBox,
        Center = new Point(0.5, 0.5),
        GradientOrigin = new Point(0.5, 0.5),
        RadiusX = 0,
        RadiusY = 0,
        GradientStops =
        {
            new System.Windows.Media.GradientStop(Colors.White, 0),
            new System.Windows.Media.GradientStop(Colors.White, 0.55),   // 内圈全亮
            new System.Windows.Media.GradientStop(Colors.Transparent, 1), // 圆缘透明=向四周渐降
        },
    };

    const double OpenMaskFullRadius = 1.35; // 覆满对角（≈0.707）且内圈(0.55×R)完全盖住四角

    void PlayOpen()
    {
        _plate.RenderTransform = null;
        _plate.BeginAnimation(OpacityProperty, null);
        _plate.Opacity = 1;
        _openMask.RadiusX = _openMask.RadiusY = 0;
        _plate.OpacityMask = _openMask;
        Anim.Run(_openMask, System.Windows.Media.RadialGradientBrush.RadiusXProperty, 0, OpenMaskFullRadius,
            AnimTuning.SettingsWindowIn, EaseStyle.EaseOutCubic);
        Anim.Run(_openMask, System.Windows.Media.RadialGradientBrush.RadiusYProperty, 0, OpenMaskFullRadius,
            AnimTuning.SettingsWindowIn, EaseStyle.EaseOutCubic, onDone: () =>
            {
                if (_plate.OpacityMask == _openMask)
                    _plate.OpacityMask = null; // 全亮后摘遮罩（观感零差异，避免常驻效果）
            });
    }

    /// <summary>仿 mac 消失（2026-10-02 重绘）：径向渐显的逆放——四周先暗、向中心收拢，播完 Hide
    /// （关闭=仅隐藏，05 §5）。代次校验防 Present 打断后误 Hide。</summary>
    void HideAnimated()
    {
        if (!IsVisible) return;
        int gen = ++_hideGen;
        _plate.RenderTransform = null;
        _plate.BeginAnimation(OpacityProperty, null);
        _plate.Opacity = 1;
        _openMask.RadiusX = _openMask.RadiusY = OpenMaskFullRadius;
        _plate.OpacityMask = _openMask;
        // 缓动用 EaseOutQuad（2026-10-04）：径向遮罩内圈全亮区（0.55×R）在 R≥0.909 时完整盖住全窗，
        // EaseInQuad 前段慢走会让前半程完全无可见变化（用户实测"闭合没变慢"）——
        // 换 EaseOutQuad 把半径变化提前，闭合 300ms 几乎全程可见地收拢
        Anim.Run(_openMask, System.Windows.Media.RadialGradientBrush.RadiusXProperty, OpenMaskFullRadius, 0,
            AnimTuning.SettingsWindowOut, EaseStyle.EaseOutQuad);
        Anim.Run(_openMask, System.Windows.Media.RadialGradientBrush.RadiusYProperty, OpenMaskFullRadius, 0,
            AnimTuning.SettingsWindowOut, EaseStyle.EaseOutQuad, onDone: () =>
            {
                if (gen != _hideGen) return;
                _plate.OpacityMask = null;
                ExpanderOverlay.CloseAll(); // 隐藏前收口展开卡（防下次打开残影）
                HotkeyBox.CancelActive();   // 隐藏前收口快捷键录入态（否则钩子会一直吞掉全系统按键）
                Hide();
            });
    }

    void CenterOn(Window? board)
    {
        try
        {
            var screen = board is { IsVisible: true }
                ? System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(
                    (int)(board!.Left * DpiScaleX), (int)(board!.Top * DpiScaleY)))
                : Screens.SelectedScreen();
            double scale = DpiScaleX;
            double x = screen.Bounds.Left + (screen.Bounds.Width - W * scale) / 2;
            double y = screen.Bounds.Top + (screen.Bounds.Height - H * scale) / 2;
            MoveToPhysical(x, y);
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    // ---- 布局 ----

    void BuildLayout()
    {
        _navHost = new StackPanel { Margin = new Thickness(0, TitleBarH, 0, 10) };
        AddNav("global", "nav.global", IconGlobal);
        AddNav("display", "nav.display", IconDisplay);
        AddNav("advanced", "nav.advanced", IconAdvanced);
        AddNav("personalize", "nav.personalize", IconPersonalize);
        AddNav("about", "nav.about", IconAbout);

        var navScroll = new ScrollViewer
        {
            Content = _navHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Width = NavW,
            // 焦点框（2026-10-05 需求②）：ScrollViewer 是 Control，主题默认焦点视觉是虚线框；
            // 按下 Alt（键提示）/Tab 导航时会围绕左列画一圈虚线，纯属噪音 → 摘掉
            FocusVisualStyle = null,
        };

        _pageHost = new ContentControl();

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NavW) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(navScroll, 0);
        Grid.SetColumn(_pageHost, 1);
        columns.Children.Add(navScroll);
        columns.Children.Add(_pageHost);

        // 标题栏拖动热区（顶部无控件区）
        var dragBar = new Border
        {
            Height = TitleBarH,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Top,
        };
        dragBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };

        // mac 三色装饰圆点（红点=关闭设置窗；2026-10-04 错误修复⑦：悬停时点内浮现符号
        //  红=小叉号 / 黄=小减号，红点与黄点点击都关闭设置窗；绿点无动作）
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(11, 11, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        dots.Children.Add(Dot("#FF5F57", DotGlyph.Close));
        dots.Children.Add(Dot("#FEBC2E", DotGlyph.Minimize));
        dots.Children.Add(Dot("#28C840", DotGlyph.None));

        var root = new Grid();
        // 步骤 06：设置窗板底也走材质引擎（MaterialBackground 的静态图层）。
        // **不开系统模糊**（UseNativeBlur 保持默认 false）：本窗的板比窗口小一圈（PlateMargin 留给投影），
        // 而系统模糊覆盖整个窗口矩形且裁不住（实测），开了会把模糊漫到板外的投影区上；
        // 板底本身 94%~99% 不透明，少了背后那层画面在观感上几乎无差别（见 MaterialEngine 注释）。
        _material = new MaterialBackground();
        _material.SetCorners(new CornerRadius(12));
        _material.SetBoardOpacity(100); // 设置窗不消费收纳板板底不透明度
        root.Children.Add(_material);
        root.Children.Add(columns);
        root.Children.Add(dragBar);
        root.Children.Add(dots);
        // 展开卡承载层（最顶，2026-10-04）：平时 Background=null 不参与命中不挡下层交互；
        // 有卡打开时背景可命中（点外关闭，ExpanderOverlay 管理）。ClipToBounds 硬保证卡不跃出窗口。
        _overlay = new Canvas { Background = null, ClipToBounds = true };
        root.Children.Add(_overlay);

        _plate = new Border
        {
            CornerRadius = new CornerRadius(12),
            Child = root,
            Margin = new Thickness(PlateMarginL, PlateMarginT, PlateMarginR, PlateMarginB),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Opacity = 0.4, Direction = 270 },
        };
        // 快照过渡的宿主=整个视觉板（含板底色/描边/圆点）而不是板内内容根——
        // 2026-10-05 清单05任务3：只截内容根时板底色（主题切换的主视觉）在快照之外瞬变，
        // 观感仍是"瞬间换主题"，与用户"设置窗也该有过渡"的诉求不符
        var shell = new Grid();
        shell.Children.Add(_plate);
        Content = shell;
        _shell = shell;
        ExpanderOverlay.Attach(_overlay);
    }

    /// <summary>圆点内的悬停符号（mac 风）。</summary>
    enum DotGlyph { None, Close, Minimize }

    /// <summary>三色圆点（2026-10-04 错误修复⑦）：11px 圆 + 悬停浮现的符号（红=叉 / 黄=减号），
    /// 红、黄两点点击都关闭设置窗（关闭=仅隐藏，带仿 mac 径向收拢过渡）。
    /// 命中区=整个圆点容器，悬停/点击都作用在容器上（小圆点直接命中太窄）。</summary>
    UIElement Dot(string hex, DotGlyph glyph)
    {
        var dot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = SettingsPalette.Freeze(hex),
        };

        var host = new Grid
        {
            Width = 11,
            Height = 11,
            Margin = new Thickness(0, 0, 7, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        host.Children.Add(dot);

        TextBlock? symbol = glyph switch
        {
            DotGlyph.Close => new TextBlock { Text = "×", FontSize = 9, FontWeight = FontWeights.Bold },
            DotGlyph.Minimize => new TextBlock { Text = "−", FontSize = 9, FontWeight = FontWeights.Bold },
            _ => null,
        };
        if (symbol != null)
        {
            symbol.Foreground = SettingsPalette.Freeze("#8C0A0A0A");
            symbol.HorizontalAlignment = HorizontalAlignment.Center;
            symbol.VerticalAlignment = VerticalAlignment.Center;
            symbol.Opacity = 0;               // 悬停才浮现（无动画：与"立即显示"口径一致）
            symbol.IsHitTestVisible = false;
            host.Children.Add(symbol);
        }

        if (glyph != DotGlyph.None)
        {
            host.ToolTip = I18n.Tr("board.close");
            host.MouseEnter += (_, _) => { if (symbol != null) symbol.Opacity = 1; };
            host.MouseLeave += (_, _) => { if (symbol != null) symbol.Opacity = 0; };
            host.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                HideAnimated(); // 关闭=仅隐藏（带仿 mac 消失过渡）；重复打开=置前（05 §5）
            };
        }
        return host;
    }

    void AddNav(string id, string labelKey, Func<Geometry> iconFactory)
    {
        bool dark = ThemeResolver.IsDark();
        var bar = new Rectangle
        {
            Width = 3.5,
            Height = 20,
            RadiusX = 1.75,
            RadiusY = 1.75,
            Fill = SettingsPalette.Accent(dark),
            Opacity = 0,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var icon = new Path
        {
            Data = iconFactory(),
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            Fill = SettingsPalette.TextSecondary(dark),
            Margin = new Thickness(7, 0, 0, 0),
        };
        var label = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        label.Text = I18n.Tr(labelKey);

        // 悬停底 / 选中底分两层（透明度过渡，2026-10-01）
        var hover = new Border { CornerRadius = new CornerRadius(8), Background = SettingsPalette.HoverOverlay(dark), Opacity = 0 };
        var sel = new Border { CornerRadius = new CornerRadius(8), Background = SettingsPalette.Track(dark), Opacity = 0 };
        var content = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0, 5, 6, 5),
            Child = HStack(bar, icon, label),
            Background = Brushes.Transparent,
        };

        var row = new Grid { Cursor = Cursors.Hand, Margin = new Thickness(8, 1, 8, 1) };
        row.Children.Add(hover);
        row.Children.Add(sel);
        row.Children.Add(content);
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; SelectNav(id); };
        row.MouseEnter += (_, _) => { if (_currentId != id) Anim.Run(hover, OpacityProperty, null, 1, AnimTuning.SettingsNav, EaseStyle.EaseInOutQuad); };
        row.MouseLeave += (_, _) => Anim.Run(hover, OpacityProperty, null, 0, AnimTuning.SettingsNav, EaseStyle.EaseInOutQuad);

        _navHost.Children.Add(row);
        _navs.Add(new NavRow
        {
            Id = id,
            LabelKey = labelKey,
            Row = row,
            Hover = hover,
            Sel = sel,
            Bar = bar,
            Label = label,
            Create = MakePageFor(id),
        });
    }

    static UIElement HStack(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    Func<SettingsPageBase> MakePageFor(string id) => id switch
    {
        "display" => () => new DisplayPage(),
        "advanced" => () => new AdvancedPage(),
        "personalize" => () => new PersonalizePage(),
        "about" => () => new AboutPage(),
        _ => () => new GlobalPage(),
    };

    /// <summary>切页：左列选中态带过渡；右列=旧页淡出→换页→新页自右侧 14px 滑入（2026-10-01）。
    /// 2026-10-04（清单01任务11）：二次点击已选中项=直接 return，不重建右列、不重播出现动画。</summary>
    void SelectNav(string id, bool animate = true)
    {
        if (animate && id == _currentId && _currentPage != null)
            return; // 已是该页：导航视觉本就到位，任何动画都不重播
        _currentId = id;
        ExpanderOverlay.CloseAll(); // 换页收口：旧页的展开卡随页消亡，不带进新页
        _currentPage?.Detach(); // 显式解绑旧页的静态事件订阅（Unloaded 在未布局即被换掉时不触发）
        foreach (var nav in _navs)
        {
            bool on = nav.Id == id;
            double dur = animate ? AnimTuning.SettingsNav : 0;
            Anim.Run(nav.Sel, OpacityProperty, null, on ? 1 : 0, dur, EaseStyle.EaseInOutQuad);
            Anim.Run(nav.Bar, OpacityProperty, null, on ? 1 : 0, dur, EaseStyle.EaseInOutQuad);
            nav.Label.Foreground = on ? SettingsPalette.Text(_dark) : SettingsPalette.TextSecondary(_dark);
        }
        var page = MakePageFor(id)();
        _currentPage = page;
        if (animate && IsLoaded && _pageHost.Content != null)
        {
            var host = _pageHost;
            Anim.Run(host, OpacityProperty, null, 0, 90, EaseStyle.EaseInQuad, onDone: () =>
            {
                if (!ReferenceEquals(_currentPage, page)) return; // 连点：只让最后一次落位
                host.Content = page;
                SlideInPage();
            });
        }
        else
        {
            _pageHost.Content = page;
            ResetPageHostVisual();
        }
    }

    void SlideInPage()
    {
        var tt = new TranslateTransform(14, 0);
        _pageHost.RenderTransform = tt;
        Anim.Run(_pageHost, OpacityProperty, 0, 1, AnimTuning.SettingsPageSwitch, EaseStyle.EaseOutCubic);
        Anim.Run(tt, TranslateTransform.XProperty, 14, 0, AnimTuning.SettingsPageSwitch, EaseStyle.EaseOutCubic,
            onDone: () => { if (ReferenceEquals(_pageHost.RenderTransform, tt)) _pageHost.RenderTransform = null; });
    }

    void ResetPageHostVisual()
    {
        _pageHost.BeginAnimation(OpacityProperty, null);
        _pageHost.Opacity = 1;
        _pageHost.RenderTransform = null;
    }

    // ---- 图标（简单几何轮廓）----

    static Geometry IconGlobal() => Geometry.Parse("M8,1 A7,7 0 1,0 8,15 A7,7 0 1,0 8,1 M1.5,8 L14.5,8 M8,1 C5.5,4 5.5,12 8,15 M8,1 C10.5,4 10.5,12 8,15");
    static Geometry IconDisplay() => Geometry.Parse("M1,2 L15,2 L15,11 L1,11 Z M6,13 L10,13 L10,11 L6,11 Z M4,15 L12,15 L12,13 L4,13 Z");
    // 高级=调节杆（三条横杠+错位圆钮）：旧几何是纯线条（无封闭区域），Path 只设 Fill 渲染为空——
    // 用户看到"高级项没有图标"的根因；改用可填充的组合几何（2026-10-02）
    static Geometry IconAdvanced()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new RectangleGeometry(new Rect(1, 2.6, 14, 2.2), 1.1, 1.1));
        group.Children.Add(new RectangleGeometry(new Rect(1, 6.9, 14, 2.2), 1.1, 1.1));
        group.Children.Add(new RectangleGeometry(new Rect(1, 11.2, 14, 2.2), 1.1, 1.1));
        group.Children.Add(new EllipseGeometry(new Point(5, 3.7), 2.3, 2.3));
        group.Children.Add(new EllipseGeometry(new Point(11, 8), 2.3, 2.3));
        group.Children.Add(new EllipseGeometry(new Point(6, 12.3), 2.3, 2.3));
        group.Freeze();
        return group;
    }
    static Geometry IconPersonalize() => Geometry.Parse("M8,1 C5,6 3,8.5 3,11 A5,5 0 0,0 13,11 C13,8.5 11,6 8,1 Z");
    static Geometry IconAbout() => Geometry.Parse("M8,1 A7,7 0 1,0 8,15 A7,7 0 1,0 8,1 M8,7 L8,12 M8,3.5 L8,4.7");

    // ---- 主题 / 语言 / 导入 ----

    void ApplyTheme()
    {
        // 板底由 MaterialBackground 提供（含发丝描边），此处不再二次上色/描边
        _plate.Background = null;
        _plate.BorderThickness = new Thickness(0);
        _material.Material = SettingsStore.Instance.GetString("personal.material", MaterialEngine.None);
        _material.SetTheme(_dark);
        foreach (var nav in _navs)
        {
            bool on = nav.Id == _currentId;
            nav.Hover.Background = SettingsPalette.HoverOverlay(_dark);
            nav.Sel.Background = SettingsPalette.Track(_dark);
            nav.Bar.Fill = SettingsPalette.Accent(_dark);
            nav.Label.Foreground = on ? SettingsPalette.Text(_dark) : SettingsPalette.TextSecondary(_dark);
        }
    }

    void OnSettingChanged(SettingChangedArgs args)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSettingChanged(args));
            return;
        }
        switch (args.Key)
        {
            case "general.font":
                FontFamily = new FontFamily(SettingsStore.Instance.GetString("general.font", "Microsoft YaHei"));
                break;
            case "theme.mode":
                // 2026-10-05 清单05任务3：主题切换与收纳板同款过渡（截当前观感快照→换装→快照 300ms 淡出），
                // 窗口不可见/未渲染时 RunWithFade 内部退化为直接换装
                RunWithFade(RebuildForTheme);
                break;
            case "advanced.dockPriority":
                ApplyTopmostStrategy(); // 窗口优先级切换（2026-10-03）：设置窗跟随（悬浮窗固定桌面级）
                break;
            case "personal.material": // 材质档：设置窗板底即时跟随（步骤 06 §2.4）
                _material.Material = SettingsStore.Instance.GetString("personal.material", MaterialEngine.None);
                break;
        }
    }

    /// <summary>亮暗翻转（mode 切换或 auto 时段翻转）：控件配色为冻结画刷，当前页整体重建最可靠。</summary>
    public void RebuildForTheme()
    {
        bool dark = ThemeResolver.IsDark();
        _dark = dark;
        ApplyTheme();
        SelectNav(_currentId, animate: false); // 整页重建：右列视觉复位
    }

    void OnLanguageChanged()
        => RunWithFade(() =>
        {
            foreach (var nav in _navs)
                nav.Label.Text = I18n.Tr(nav.LabelKey);
            _currentPage?.RefreshText();
        });

    /// <summary>全局主题/语言切换过渡（2026-10-03，2026-10-05 清单05任务3 扩到整个视觉板）：
    /// 窗口可见时先截当前观感快照（含板底色/描边/内容）盖顶 → 换装 → 快照淡出（ThemeFade 同机制）；
    /// 窗口不可见/未渲染时直接换装。</summary>
    void RunWithFade(Action apply)
    {
        var host = _shell;
        if (!IsVisible || host is not { ActualWidth: > 1 } || host.ActualHeight < 1)
        {
            apply();
            return;
        }
        RenderTargetBitmap snapshot;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(host);
            snapshot = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(host.ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(host.ActualHeight * dpi.DpiScaleY)),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            snapshot.Render(host);
        }
        catch
        {
            apply();
            return;
        }
        var overlay = new Image
        {
            Source = snapshot,
            Width = host.ActualWidth,
            Height = host.ActualHeight,
            Stretch = Stretch.None,
            IsHitTestVisible = false,
        };
        Panel.SetZIndex(overlay, int.MaxValue);
        host.Children.Add(overlay);
        apply(); // 新主题/文案在快照之下换装，观感为交叉淡化
        Anim.Run(overlay, OpacityProperty, 1, 0, AnimTuning.ThemeFade, EaseStyle.EaseInOutQuad,
            onDone: () => host.Children.Remove(overlay));
    }

    void OnConfigImported() => _currentPage?.ReloadFromSettings();

    /// <summary>恢复默认设置后：当前页控件值整体回读。
    /// 主题若同时被改回默认，OnSettingChanged 已走 RunWithFade→RebuildForTheme 整页重建，
    /// 此处兜住"主题没变"的情形（开关/列表仍停在用户改过的值上）。</summary>
    void OnDefaultsRestored() => _currentPage?.ReloadFromSettings();
}
