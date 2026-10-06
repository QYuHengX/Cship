using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cship.Core;
using Cship.Models;
using Cship.Ui.Animations;
using Cship.Ui.Components;
using WinForms = System.Windows.Forms;

namespace Cship.Ui;

/// <summary>
/// 收纳板（步骤 1/2/3/4/5/6/7 汇总）：三层结构、锚边定位、可调长宽（按方向记忆）、
/// 桌面文件陈列、自绘右键菜单、垃圾桶拖删、图标拖动换位、与悬浮窗开合联动。
/// </summary>
public partial class BoardWindow : GlassWindow, IEscHandler
{
    /// <summary>齿轮点击（05 步接设置窗，本步仅信号）。</summary>
    public event Action? GearClicked;

    enum DragPhase { None, Pressing, Dragging }

    // 默认尺寸（2026-08-29 用户反馈"按截图"）：截图量得可见区域 ≈1334×806（底边贴工作区底、
    // 水平对齐悬浮窗中心后即图中位置）；其余方向仍用旧默认
    const double DefaultW = 1334, DefaultH = 806;  // 00 §7 收纳板默认尺寸
    const double ResizeHotZone = 8;                // 可调边热区
    // 最小可见尺寸（2026-10-05 需求③）：原 480×320 缩到 **1/3**（480/3=160、320/3≈106.7→107）。
    // 窗口矩形口径还要另加锚边延伸量，见 ClampSizeToWorkarea / ApplyResize；XAML 的
    // MinWidth/MinHeight 同值（否则 WPF 会按旧下限把窗口顶回去）。
    const double MinBoardW = 160, MinBoardH = 107;
    // 图标区内边距（与 BoardWindow.xaml 的 Scroller.Margin 同口径，Relayout 的 ViewportWidth 兜底值按它折算）：
    // 右/下 1px（2026-10-05 需求②：10/10/12 → 1）；左 6px（2026-10-06 需求①：最左列图标离板左缘多留几个像素）
    const double BoardInnerPad = 1;
    const double BoardPadLeft = 6;
    const double RevealRadius = 96;                // hoverReveal 半径【3.5】
    const double DragThresholdPx = 4;              // 物理像素阈值（同 01）
    const double SizeSaveDebounceMs = 500;
    // 贴边延伸（清单02任务17，批次十二 6→1px 基础上加强）：锚边贴屏幕边缘时窗口矩形向屏外
    // 延伸 AnchorOutsetPx 物理像素盖住缝隙；Root 内容以等量内边距（扣掉 AnchorKeepCoverPx 的
    // 搭接保险）平移回原视觉位置——延伸的只是窗口矩形，可见内容位置与旧版 1px 口径一致。
    const double AnchorOutsetPx = 10;
    const double AnchorKeepCoverPx = 1; // 内容保留的出屏搭接量：板边缘 1px 出屏，屏内首行必是板像素（防 DIP 取整露缝）

    /// <summary>方向圆角：锚边一侧两角为直角（与屏幕边缘融合），其余 12px（批次四）。</summary>
    CornerRadius DirectionCorners() => Direction switch
    {
        "down" => new CornerRadius(0, 0, 12, 12),   // 顶边贴屏顶 → 顶两角直角
        "left" => new CornerRadius(0, 12, 12, 0),   // 左边贴屏左 → 左两角直角
        "right" => new CornerRadius(12, 0, 0, 12),  // 右边贴屏右 → 右两角直角
        "center" => new CornerRadius(12),
        _ => new CornerRadius(12, 12, 0, 0),        // up：底两角直角
    };

    /// <summary>把方向圆角应用到各视觉层（材质/自定义背景）。自定义背景用 Border 圆角本身绘制裁切
    /// （2026-10-04 错误修复⑤：不再靠 RenderSize→Clip 的时序裁剪）。
    /// 2026-10-05 清单05任务1：自定义背景图与板底"内层"几何对齐——板底的 1px 发丝描边会把画刷
    /// 画在描边以内（内缩 1px、半径−1），背景图若贴满整块板就会在那 1px 描边带里露出
    /// （用户报告的"背景图片超出上下左右边缘几个像素"），故同量内缩 + 半径−1。</summary>
    void ApplyDirectionCorners()
    {
        var c = DirectionCorners();
        Material.SetCorners(c);
        double inset = MaterialBackground.PlateStroke;
        var inner = new CornerRadius(
            Math.Max(0, c.TopLeft - inset), Math.Max(0, c.TopRight - inset),
            Math.Max(0, c.BottomRight - inset), Math.Max(0, c.BottomLeft - inset));
        CustomBgHost.Margin = new Thickness(inset);
        CustomBgHost.CornerRadius = inner;
        BgShade.Margin = new Thickness(inset); // 10% 遮罩与背景图几何一致（§5）
        BgShade.CornerRadius = inner;
    }
    const int BatchSize = 64;                      // 增量分批参考值（首载现固定 16/批，批次七）
    const double RoundedCorner = 12;               // 00 §7 圆角

    FloatingDock? _dock;
    DesktopScanner? _scanner;
    readonly BoardModel _model = new();
    readonly Animations.HoverCycle _gearCycle = new(); // 齿轮 hover 完整播放编排（批次十二修订 B）
    Dictionary<string, (int R, int C)> _iconOrder = new();
    List<(string Path, string Name)> _virtualItems = new(); // 仅收纳板存在的"快捷方式"（批次六）

    // 鼠标拖动状态
    DragPhase _phase = DragPhase.None;
    BoardItem? _pressItem;
    Point _pressScreen;
    bool _renaming;
    Border? _ghost;
    bool _trashHot;
    int _cols = 1;

    // 定时器
    readonly DispatcherTimer _sizeSaveTimer;
    readonly DispatcherTimer _runningTimer;
    bool _firstLoadDone;
    bool _scanBusy;
    bool _openAnimPending;
    bool _closeStarted;   // 关板动画进行中（防 Deactivated/ToggleBoard 重入，批次七）
    DateTime _outsideCloseSuppressUntil = DateTime.MinValue; // 主动让出焦点（弹窗/启动应用）期间抑制"板外点击收起"
    readonly List<Thumb> _thumbs = new();

    /// <summary>接下来 ms 毫秒内的失焦不触发"板外点击收起"（打开弹窗/属性页/启动应用等场景）。</summary>
    void SuppressOutsideClose(double ms = 2500) => _outsideCloseSuppressUntil = DateTime.UtcNow.AddMilliseconds(ms);

    // ---- 中键点板外收起（批次八：WH_MOUSE_LL 全局钩子，开板装/关板卸）----

    IntPtr _mouseHook;
    SystemBridge.LowLevelMouseProc? _mouseHookProc; // 持引用防 GC 回收钩子

    void InstallOutsideCloseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseHookProc = OutsideCloseHook;
        _mouseHook = SystemBridge.SetWindowsHookEx(SystemBridge.WH_MOUSE_LL, _mouseHookProc,
            SystemBridge.GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
            Logger.Warn("中键板外收起钩子安装失败（功能降级为不可用，不影响其它行为）");
    }

    void UninstallOutsideCloseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        SystemBridge.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
        _mouseHookProc = null;
    }

    IntPtr OutsideCloseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)SystemBridge.WM_MBUTTONDOWN && IsVisible && !_closeStarted)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<SystemBridge.MSLLHOOKSTRUCT>(lParam);
            var pt = new Point(info.pt.X, info.pt.Y);
            double sx = ScaleX(), sy = ScaleY();
            var boardRect = new Rect(Left * sx, Top * sy, ActualWidth * sx, ActualHeight * sy);
            if (!boardRect.Contains(pt) && !_renaming && DateTime.UtcNow >= _outsideCloseSuppressUntil)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!IsVisible || _closeStarted) return;
                    Logger.Info("中键点击板外收起收纳板");
                    CloseBoard();
                });
            }
        }
        return SystemBridge.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>模板内的 ItemsPanel 惰性解析（x:Name 在模板里不生成字段，运行时从可视树找）。</summary>
    IconGridPanel? _panelCache;
    IconGridPanel? IconPanel => _panelCache ??= FindDescendant<IconGridPanel>(GridHost);

    public BoardWindow()
    {
        InitializeComponent();
        FontFamily = new FontFamily(SettingsStore.Instance.GetString("general.font", "Microsoft YaHei"));
        // 材质（2026-10-06 口径：**收纳板启用系统模糊**）：
        // 收纳板的材质层正好铺满整个窗口矩形，是唯一适合系统模糊的窗口——模糊由 DWM 在窗口背后
        // 实时合成，本进程零抓屏、零位图、零延迟，移动/缩放/动画期什么都不用做。
        // 代价：系统模糊铺满窗口矩形且**形状裁不住**（四条路实测都不通），圆角外会露出一圈"被糊的桌面"；
        // 这一圈由 MaterialBackground 的**四角羽化补丁**用板底色按径向渐隐磨掉（模糊没开时补丁自动静默）。
        // （用户曾在"圆角优先"与"玻璃感"之间来回一次——两个方向都只差这一行布尔量。）
        Material.UseNativeBlur = true;
        ApplyTheme();
        LoadCustomBg();
        LoadBoardSize();

        _iconOrder = StateStore.Instance.GetIconOrder();
        _model.SetRemoved(StateStore.Instance.GetRemovedItems()); // 右键"移除"名单（刷新不会找回，2026-08-29）
        _virtualItems = StateStore.Instance.GetVirtualItems();    // 仅收纳板"快捷方式"（批次六）

        // 常驻控件（1.4/1.5）；动画参数统一 AnimTuning（步骤 03）
        // 齿轮（批次十二修订 B）：触发后完整播放，播放中忽略二次触发
        _gearCycle.Configure(
            done =>
            {
                Anim.Run(GearIcon, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.Linear);
                Anim.Run(GearRotate, RotateTransform.AngleProperty, null, 15, AnimTuning.GearSpin, EaseStyle.EaseInOutQuad, onDone: done);
            },
            done =>
            {
                Anim.Run(GearIcon, OpacityProperty, null, 0.85, AnimTuning.Micro, EaseStyle.Linear);
                Anim.Run(GearRotate, RotateTransform.AngleProperty, null, 0, AnimTuning.GearSpin, EaseStyle.EaseInOutQuad, onDone: done);
            },
            () => GearBtn.IsMouseOver);
        GearBtn.MouseEnter += (_, _) => _gearCycle.OnEnter();
        GearBtn.MouseLeave += (_, _) => _gearCycle.OnLeave();
        GearBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; GearClicked?.Invoke(); };
        // 垃圾桶（清单二·任务6）：悬停盖向上微开（HoverCycle 完整播放）；单击打开系统回收站
        // 垃圾桶（清单二·任务6；修订A·4.4）：悬停盖向上微开+侧向开盖过渡，灰色圆角底板过渡浮现
        _trashCycle.Configure(
            done =>
            {
                Anim.Run(TrashLidShift, TranslateTransform.YProperty, null, -TrashLidHoverLift, AnimTuning.Micro, EaseStyle.EaseInOutQuad, onDone: done);
                Anim.Run(TrashLidTilt, RotateTransform.AngleProperty, null, -TrashLidHoverTilt, AnimTuning.Micro, EaseStyle.EaseInOutQuad); // 开盖过渡
                ShowTrashPlate(false);
            },
            done =>
            {
                Anim.Run(TrashLidShift, TranslateTransform.YProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInOutQuad, onDone: done);
                Anim.Run(TrashLidTilt, RotateTransform.AngleProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
                HideTrashPlate();
            },
            () => TrashBtn.IsMouseOver);
        TrashBtn.MouseEnter += (_, _) => _trashCycle.OnEnter();
        TrashBtn.MouseLeave += (_, _) => _trashCycle.OnLeave();
        TrashBtn.MouseLeftButtonUp += OnTrashClick;
        // 关闭红点（批次十二重做，原文任务9）：hover 红点中心过渡浮现黑叉号 + 红色亮度提高；
        // 画刷在构造器换成非冻结实例，供 ColorAnimation 过渡（命中区=整个 26px 按钮容器）
        CloseDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57));
        CloseBtn.MouseEnter += (_, _) =>
        {
            Anim.Run(CloseX, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.Linear);
            AnimateCloseDot(Color.FromRgb(0xFF, 0x83, 0x77)); // 提亮
        };
        CloseBtn.MouseLeave += (_, _) =>
        {
            Anim.Run(CloseX, OpacityProperty, null, 0, AnimTuning.Micro, EaseStyle.Linear);
            AnimateCloseDot(Color.FromRgb(0xFF, 0x5F, 0x57));
        };
        CloseBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseBoard(); };
        ToolTipService.SetToolTip(CloseBtn, I18n.Tr("board.close"));
        ToolTipService.SetToolTip(GearBtn, I18n.Tr("board.settings"));

        // 空态文案（7.4）
        EmptyHint.Text = I18n.Tr("board.empty");
        EmptyHint.Foreground = new SolidColorBrush(
            ThemeResolver.IsDark() ? Color.FromRgb(0xB8, 0xB8, 0xB8) : Color.FromRgb(0x5F, 0x5F, 0x5F));

        // 网格事件（选中/双击/拖动/右键）
        GridHost.ItemsSource = _model.Items; // 绑定可见集合（缺了它一个容器都不会生成）
        GridHost.MouseLeftButtonDown += OnGridPress;
        GridHost.MouseMove += OnGridMove;
        GridHost.MouseLeftButtonUp += OnGridRelease;
        GridHost.LostMouseCapture += (_, _) => EndDrag();
        Root.MouseRightButtonUp += OnRootRightClick;

        // 拖文件入板（2026-08-29 批次七）：悬停 >100ms 实时腾出空位预览落点；
        // 不再显示绿膜/复制角标等"可互动"提示
        Root.AllowDrop = true;
        // 拖入腾位（批次七/批次十一）：Enter 兼作 Over 处理（实测 OLE 拖动经过板时 Over 事件
        // 大量缺席、只剩 Enter/Leave 风暴）；板内 Leave=命中震荡误报，仅记日志不取消预览，
        // 光标真出板矩形才取消腾位并补位恢复——否则"腾位→误Leave补位→再腾位"循环不止。
        Root.DragEnter += OnRootDragFileOver;
        Root.DragOver += OnRootDragFileOver;
        Root.DragLeave += (_, _) => OnRootDragLeaveProbe();
        Root.Drop += OnDropFiles;

        // 滚轮平滑滚动（2026-08-29）：Preview 截获滚轮，目标偏移经渲染帧插值缓动
        Scroller.PreviewMouseWheel += OnSmoothWheel;
        Scroller.ScrollChanged += (_, _) => { if (!_scrollAnimating) _scrollTarget = Scroller.VerticalOffset; };
        GridHost.MouseLeave += (_, _) => { HideAllReveals(); HideHoverTip(); }; // 圆形渐显：指针离板全隐（2026-10-02）；悬停文件名提示同步收起

        // center 模式：空白区按住拖动移动窗口（2026-08-29 额外要求 1）
        Root.MouseLeftButtonDown += OnBlankDragMove;
        Root.MouseMove += OnBlankDragMoveUpdate;
        Root.MouseLeftButtonUp += OnBlankDragMoveEnd;

        // 尺寸落盘防抖
        _sizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SizeSaveDebounceMs) };
        _sizeSaveTimer.Tick += (_, _) =>
        {
            _sizeSaveTimer.Stop();
            SaveSizeNow();
        };

        // 拖入腾位预览（批次七）：悬停区域 >100ms 才腾出空位
        _dropHoleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _dropHoleTimer.Tick += (_, _) =>
        {
            _dropHoleTimer.Stop();
            if (_dropHole == _dropHolePending) return;
            _dropHole = _dropHolePending;
            ApplyDropPreview();
            Logger.Info($"拖入腾位预览：格 {_dropHole}");
        };

        // 指示灯轮询（3.6；间隔自适应：冲刺/活跃/空闲三档 AnimTuning.RunningPoll*；07 步移入后台线程）。
        // 2026-10-05 清单06任务1：原先恒 2s 一拍——"程序开得快时三点等合并"与"程序关了灯还亮着"
        // 都要干等下一拍，改按态势调档（见 ApplyPollCadence）。
        _runningTimer = new DispatcherTimer { Interval = Anim.Ms(AnimTuning.RunningPollMs) };
        _runningTimer.Tick += (_, _) => { if (IsVisible) PollRunning(); };
        _runningTimer.Start();

        // 桌面监视
        _scanner = new DesktopScanner();
        _scanner.RefreshRequested += () => _ = RescanAsync();
        // 图标渐次回填（07 §4）：后台单消费者提完一枚就广播一次，这里只把对应条目的
        // IconVersion 推一格 → IconItem 重载那一枚图标（缓存路径不变，靠版本号触发）
        DesktopScanner.IconReady += OnIconReady;
        _ = RescanAsync();

        // 分类页（步骤 04 · 2026-09-25 改版）：顶部按钮行装配、视图切换过滤、拖入加入
        CatTabBar.Initialize(_model, Root);
        CatTabBar.SelectionChanged += OnCategorySelected;
        CatTabBar.MembersChanged += () =>
        {
            if (IsFiltered) ApplyCategoryFilter(); // 当前视图成员变化 → 重建过滤（勿在未过滤态白做）
        };
        ApplyCategoryAvailability();

        // 重命名分类时点编辑按钮外任意处=退出编辑（清单三·任务8）：
        // Preview 隧道事件先于所有命中逻辑；命中点不在编辑按钮内才收尾（提交，与失焦同口径）
        Root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!CatTabBar.IsEditing) return;
            if (!CatTabBar.IsEditHit(e.GetPosition(Root)))
                CatTabBar.EndEditing();
        };

        SettingsStore.Instance.SettingsChanged += OnSettingChanged;
        ShortcutRouter.Register(this, EscLayer.Board);
        // 语言热切换（步骤 05）：重刷静态文案（tooltip/空态提示；菜单文案点击时经 I18n.Tr 现取无残留）。
        // 2026-10-03：全局语言切换带过渡动画——板可见时快照交叉淡化（同主题切换 ThemeFade）
        I18n.LanguageChanged += OnLanguageChangedWithFade;
        ThemeAuto.DarkChanged += OnAutoDarkChanged; // auto 模式时段翻转（步骤 05）
        // 中键点击板身以外区域 → 收起收纳板（2026-08-29 批次八；批次七的左键失焦收起会打断
        // 从资源管理器发起的 OLE 拖入——拖动一起步就失焦，板直接没了。中键无此冲突）。
        // 全局低级鼠标钩子实现，钩子随开板安装/关板卸载；弹窗等主动让焦场景仍经 SuppressOutsideClose 抑制。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_renaming || Keyboard.FocusedElement is TextBox) return; // 重命名中的 Esc 归 TextBox
            e.Handled = ShortcutRouter.EscPressed(); // 7.3：设置窗>收纳板（分类页不占 Esc 层，2026-09-25 改版）
        };
        Closed += (_, _) =>
        {
            GlassPopup.CloseAll();
            DesktopScanner.IconReady -= OnIconReady; // 静态事件必须成对退订（07 步审计）
            UninstallOutsideCloseHook();
            SettingsStore.Instance.SettingsChanged -= OnSettingChanged;
            I18n.LanguageChanged -= OnLanguageChangedWithFade;
            ThemeAuto.DarkChanged -= OnAutoDarkChanged; // 静态事件必须成对退订：否则窗口被事件源钉住无法回收
            CatTabBar.Detach(); // 分类顶栏同样订阅了 I18n.LanguageChanged（静态事件），关闭时解绑
            ShortcutRouter.Unregister(this);
            _runningTimer.Stop();
            _sizeSaveTimer.Stop();
            _layoutMergeTimer?.Stop(); // 07 §4：布局合并计时器（16ms）
            _layoutMergeTimer = null;
            _scanner?.Dispose();
            _scanner = null;
        };
    }

    void OnAutoDarkChanged(bool dark) => ApplyThemeWithFade(); // auto 时段翻转：整板快照交叉淡化

    /// <summary>
    /// 01b 提示原为"BoardWindow 恒置顶"；2026-08-29 用户反馈改为**收纳板显示优先级跟随悬浮窗**：
    /// 读 advanced.dockPriority（low=不置顶，medium/high=置顶），设置切换时经 OnSettingChanged 实时生效。
    /// </summary>
    protected override void ApplyTopmostStrategy() => ApplyBoardPriority();

    /// <summary>重新应用收纳板 z 序跟随悬浮窗优先级（Alt+Tab 强制开板关闭后回落用，任务7）。</summary>
    public void ApplyBoardPriority()
    {
        string p = SettingsStore.Instance.GetString("advanced.dockPriority", "low");
        bool topmost = !string.Equals(p, "low", StringComparison.OrdinalIgnoreCase);
        Topmost = topmost;
        Logger.Info($"收纳板 z 序跟随悬浮窗优先级 = {p}（Topmost={topmost}）");
    }

    // ---- 全屏前台退让（07 §8 方案 B，2026-10-05 用户裁决）----

    bool _fullScreenRetreat;

    /// <summary>
    /// 进入所选屏真全屏前台（游戏/全屏视频）时让置顶中的收纳板临时退层，避免压在满屏内容上；
    /// 全屏退出后按 <c>advanced.dockPriority</c> 重新落定（不写死 true，low 档仍保持不置顶）。
    /// 由 <c>App.OnFullScreenTick</c> 每秒轮询驱动（仅当优先级档要求置顶且有窗口可见时才检测）。
    /// </summary>
    public void SetFullScreenRetreat(bool retreat)
    {
        if (_fullScreenRetreat == retreat) return;
        _fullScreenRetreat = retreat;
        if (retreat)
        {
            Topmost = false;
            Logger.Info("全屏前台：收纳板临时退层（Topmost=false）");
        }
        else
        {
            ApplyBoardPriority(); // 恢复由设置档决定的值
            Logger.Info("全屏退出：收纳板恢复优先级档");
        }
    }

    // ---- 分类页（步骤 04 · 2026-09-25 改版：顶部按钮行 + 板内视图切换）----
    // 分类视图=主网格过滤（BoardModel.ViewFilter），不是新面板：交互逻辑与主板天然一致（原文 L23）。
    // 过滤视图内 Row/Col 仅作显示紧凑排列，iconOrder（全局顺序）不受影响、不写盘。

    CategoryModel? _currentCategory; // null/Id=="all"=主板全部内容

    bool IsFiltered => _currentCategory is { Id: not "all" };

    void OnCategorySelected(CategoryModel cat)
    {
        _currentCategory = cat;
        ApplyCategoryFilter();
    }

    void ApplyCategoryFilter()
    {
        var cat = _currentCategory;
        if (cat is { Id: not "all" })
        {
            var members = new HashSet<string>(cat.Items, StringComparer.OrdinalIgnoreCase);
            BoardItemType? type = cat.Id switch
            {
                "apps" => BoardItemType.Apps,
                "files" => BoardItemType.Files,
                "folders" => BoardItemType.Folders,
                _ => null,
            };
            _model.ViewFilter = it => (type != null && it.Type == type) || members.Contains(it.Name);
            _model.Reconcile(); // ViewFilter 非自动对账（成员/切换两路都要重建可见集合）
            EmptyHint.Text = I18n.Tr("cat.empty");
            _scrollTarget = 0;
            Scroller.ScrollToVerticalOffset(0); // 切换后滚动回顶部（04 §5.3）
        }
        else
        {
            _currentCategory = null;
            _model.ViewFilter = null;
            _model.Reconcile();
            foreach (var it in _model.All) // 还原全局单元格（过滤视图改写过 Row/Col 显示值）
            {
                if (_iconOrder.TryGetValue(it.Name, out var rc)) { it.Row = rc.R; it.Col = rc.C; }
                else { it.Row = -1; it.Col = -1; }
            }
            EmptyHint.Text = I18n.Tr("board.empty");
        }
        Relayout();
        // 切换交叉淡化（04 §5.3 / 00 §7：150ms）
        Anim.Run(GridHost, OpacityProperty, 0, 1, AnimTuning.CategorySwitch, EaseStyle.EaseInOutQuad);
    }

    /// <summary>advanced.categoryDisabled（原文 L49）：整个按钮行隐藏（含"全部"与"更多"）；
    /// 隐藏前先切回"全部"（主板本身即全部内容，不留空行）。动画过渡在 CatTabBar.SetDisabled。</summary>
    void ApplyCategoryAvailability()
    {
        bool disabled = SettingsStore.Instance.GetBool("advanced.categoryDisabled", false);
        if (disabled && IsFiltered)
            CatTabBar.SelectAll(); // 触发 SelectionChanged → 过滤复位
        CatTabBar.SetDisabled(disabled);
    }

    /// <summary>分类视图内新增项（外部拖入的虚拟项）：追加到**全局**序列尾（iconOrder），
    /// 不走 CompactSequence——过滤视图重排会丢失未显示项的顺序。</summary>
    void AppendGlobalOrder(BoardItem item)
    {
        int cols = Math.Max(1, _cols);
        int n = _iconOrder.Count;
        _iconOrder[item.Name] = (n / cols, n % cols);
        SaveIconOrder();
    }

    // ---- 开合（7.1/7.2）----

    /// <summary>开板动画闸门的截止时间（批次八）：首开最多等 1.5s，扫描异常时照常开板。</summary>
    DateTime _prewarmDeadline = DateTime.MinValue;
    bool _populateDrained; // 首载分批已灌完（闸门条件之一）
    bool _batchPending;    // 首载分批灌入进行中（重开时区分"没有待灌批次"与"闩锁未补"）

    // ---- 首载就绪（清单二·任务13）：悬浮窗加载态的放行信号 ----

    bool _firstLoadReadyFired;
    bool _firstLoadPopulated; // 首载分批已灌完（一次性事实，不复位——区别于开板闸门消耗性的 _populateDrained）

    /// <summary>首载是否就绪（首扫完成 + 无待灌批次 + 首载灌完）。App 开板闸门用。
    /// 注意 _populateDrained 是开板动画闸门的一次性闩锁（TryBeginOpenAnimation 会消耗置回），
    /// 不能拿来当"首载就绪"依据，否则第二次开板永远不就绪（自检实测踩坑）。</summary>
    public bool IsFirstLoadReady => _firstLoadDone && !_batchPending && _firstLoadPopulated;

    /// <summary>首载就绪后触发一次（分批灌完回调首次满足时；扫描异常走 ForceFirstLoadReady 兜底）。</summary>
    public event Action? FirstLoadReady;

    void CheckFirstLoadReady()
    {
        if (_firstLoadReadyFired || !IsFirstLoadReady) return;
        _firstLoadReadyFired = true;
        Logger.Info("首载就绪（FirstLoadReady 事件）");
        FirstLoadReady?.Invoke();
    }

    /// <summary>兜底：扫描异常等导致分批永不灌完时放行（防悬浮窗加载态永久卡住）。</summary>
    void ForceFirstLoadReady()
    {
        if (_firstLoadReadyFired) return;
        _populateDrained = true;
        _firstLoadPopulated = true;
        _batchPending = false;
        CheckFirstLoadReady();
    }

    /// <summary>
    /// 开板动画闸门（2026-08-29 批次八首开卡顿）：首开等"首扫完成 + 网格面板就绪 +
    /// 分批灌完"再启动动画——容器生成与图标解码全部发生在动画开始之前，动画全程无卡帧；
    /// 构造/扫描由 App 在悬浮窗消散期（~500ms）并行预执行。1.5s 兜底防扫描异常拖死开板。
    /// </summary>
    DateTime _openAnimRequestAt = DateTime.MinValue; // 诊断：本次开板请求时刻（测闸门等待时长）

    DispatcherTimer? _gatePollTimer; // 开板闸门轮询（复用单实例，避免等待期反复 new）

    DispatcherTimer GatePollTimer()
    {
        if (_gatePollTimer != null) return _gatePollTimer;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            TryBeginOpenAnimation();
        };
        _gatePollTimer = t;
        return t;
    }

    void TryBeginOpenAnimation()
    {
        // _closeStarted：关板动画已启动（含开板中途 Esc 排队后开跑）——绝不再启动开板动画，
        // 否则开/关两套动画同时驱动 Root 的 RenderTransform/Opacity 互相清除（步骤 03 排队语义）
        if (!IsVisible || !_openAnimPending || _closeStarted) return;
        bool ready = _firstLoadDone && IconPanel != null && _populateDrained;
        if (!ready && DateTime.UtcNow < _prewarmDeadline)
        {
            // 计时器轮询而非同优先级重排队：Loaded 优先级自排队会饿死 Background 的分批插入。
            // 单实例复用（2026-10-05 性能）：原实现每 25ms 丢弃一个 DispatcherTimer 再新建，
            // 闸门等待期最高 1.5s ≈ 60 个/秒的纯 GC 抖动。
            GatePollTimer().Start();
            return;
        }
        Logger.Info($"开板动画启动：闸门等待 {(DateTime.UtcNow - _openAnimRequestAt).TotalMilliseconds:F0}ms"
            + $"（首载={_firstLoadDone} 分批灌完={_populateDrained}）");
        _populateDrained = false;
        _openAnimPending = false;
        ClampSizeToWorkarea();
        Reposition(); // DPI 就绪后校正锚边/居中
        // 首载布局补跑（2026-08-30 批次十一，修"第一次开板图标全叠在 (0,0)"）：
        // 消散期预构造时（批次八）窗口未 Show、模板未应用，首扫分批回调里的 Relayout
        // 全部因 IconPanel=null 早退，格子分配从未执行——容器生成时 Col=-1 全部落到 (0,0)。
        // 此刻闸门已确认 IconPanel 就绪，补一次 Relayout 完成行列分配（含 iconOrder 恢复），
        // 并清 IconGridPanel 的落位记忆，让图标直接落位而非从 (0,0) 飞入。
        if (IconPanel != null)
        {
            IconPanel.ResetLayout();
            Relayout();
        }
        InstallOutsideCloseHook(); // 中键点板外收起（批次八）
        PlayOpenAnimation();
    }

    // ---- 开合预设装配（步骤 03）：注册表 + 动画期输入遮罩 + 关板请求排队 ----

    /// <summary>本次开板生效的预设 Id（关闭沿用同一预设——"预设切换在关闭状态下生效"）。</summary>
    string? _openPresetId;

    /// <summary>开合预设动画进行中（输入遮罩挂着；期间 Esc/托盘的关板请求排队，防同属性动画打架）。</summary>
    bool _boardAnimRunning;
    Action? _queuedClose;
    Border? _animGuard;

    /// <summary>
    /// 动画期全屏透明遮罩（03 §3"Board 加全屏透明遮罩捕获输入，防连点状态错乱"）：
    /// Preview 隧道事件置 Handled，把左/右键与滚轮全部吞掉——Root 的右键菜单、网格拖动、
    /// 缩放热区在动画期间一律不可触发；动画结束即摘除。
    /// </summary>
    void AttachAnimGuard()
    {
        if (_animGuard != null) return;
        var guard = new Border { Background = Brushes.Transparent };
        guard.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        guard.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true;
        guard.PreviewMouseRightButtonDown += (_, e) => e.Handled = true;
        guard.PreviewMouseRightButtonUp += (_, e) => e.Handled = true;
        guard.PreviewMouseWheel += (_, e) => e.Handled = true;
        Panel.SetZIndex(guard, 10000);
        Root.Children.Add(guard);
        _animGuard = guard;
    }

    void DetachAnimGuard()
    {
        if (_animGuard == null) return;
        Root.Children.Remove(_animGuard);
        _animGuard = null;
    }

    /// <summary>播开板动画：先复位 Root 可见性（OpenFor 置 0 防首帧闪现，纯平移类预设不碰 Opacity），
    /// 挂遮罩并记录本次预设；完成后摘遮罩并放行排队中的关板请求。</summary>
    void PlayOpenAnimation()
    {
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 1;
        AttachAnimGuard();
        _boardAnimRunning = true;
        var preset = BoardAnimPresets.Current();
        _openPresetId = preset.Id;
        Logger.Info($"开板动画预设 = {preset.Id}（方向 {EffectiveDirection()}）");
        preset.Open(this, DockCenterPhysical(), EffectiveDirection(), OnOpenAnimDone);
    }

    void OnOpenAnimDone()
    {
        _boardAnimRunning = false;
        DetachAnimGuard();
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 1; // 双保险：预设内部已复位，此处兜底防属性残留
        var queued = _queuedClose;
        _queuedClose = null;
        queued?.Invoke();
    }

    public void OpenFor(FloatingDock dock)
    {
        _dock = dock;
        if (IsVisible)
        {
            Activate();
            return;
        }
        LoadBoardSize();
        RebuildResizeThumbs();
        LoadCustomBg();
        ApplyTheme();
        // 拖入文件夹描边硬复位（2026-10-05 清单06任务3）：拖动中途关板时描边可能停在淡出中，
        // 开板前一律清零，避免"上次的描边残留在某个文件夹图标上"
        _dropFolderIcon = null;
        FolderDropRing.BeginAnimation(OpacityProperty, null);
        FolderDropRing.Opacity = 0;
        FolderDropRing.Visibility = Visibility.Collapsed;
        Root.Opacity = 0; // 等定位校正后由开合动画淡入，避免首帧闪现
        Reposition();
        Show();
        Activate(); // 出现期间激活【1.1】
        // 首帧防闪现（Root.Opacity=0）→ 布局就绪后校正定位并播开合动画。
        // 不能用 OnContentRendered：它只触发一次，第二次打开会永远停在透明态。
        _prewarmDeadline = DateTime.UtcNow.AddSeconds(1.5); // 首开等预热（批次八），上限兜底
        // 闸门闩锁补位（2026-08-30 修"第二次起开板延迟 1.5s"）：_populateDrained 是一次性闩锁，
        // 首开通过闸门时被消耗（置回 false），而只有 RescanAsync（watcher 触发/手动刷新）会再置 true——
        // 第二次及以后开板若桌面无文件变化就没人补位，闸门只能干等 1.5s 兜底才启动动画。
        // 分批灌入只在首载发生（ReconcileBatched 的唯一调用点），非首载开板时没有待灌批次，闸门即刻满足；
        // 首载分批仍在灌时开板则保持闩锁关闭，等灌完回调再放行（动画不与容器生成叠加）。
        if (_firstLoadDone && !_batchPending)
            _populateDrained = true;
        _openAnimRequestAt = DateTime.UtcNow; // 诊断：闸门等待时长起点
        _openAnimPending = true;
        TryBeginOpenAnimation();
    }

    /// <summary>关板。revive=false 供程序整体退出路径用：板滑出后不复现悬浮窗（2026-08-30 批次十），
    /// 离场编排由 App.ExitWithFade 收尾。开板动画进行中的关板请求排队（动画完成后执行），
    /// 避免开/关两套动画同时驱动 Root 的 RenderTransform/Opacity 互相清除。</summary>
    public void CloseBoard(Action? onDone = null, bool revive = true)
    {
        if (!IsVisible || _closeStarted)
        {
            if (_closeStarted) { onDone?.Invoke(); }
            return;
        }
        if (_boardAnimRunning)
        {
            var previous = _queuedClose;
            _queuedClose = () => CloseBoard(onDone, revive);
            if (previous != null) _queuedClose = () => { previous(); CloseBoard(onDone, revive); };
            return;
        }
        _closeStarted = true;
        EndDrag();
        RemoveGhost();
        GlassPopup.CloseAll(); // 关板即关菜单（批次四：弹出层不随板隐藏自动关闭）
        AttachAnimGuard();
        _boardAnimRunning = true;
        var preset = BoardAnimPresets.Find(_openPresetId) ?? BoardAnimPresets.Current();
        preset.Close(this, DockCenterPhysical(), EffectiveDirection(), () =>
        {
            _boardAnimRunning = false;
            DetachAnimGuard();
            Root.RenderTransform = null; // 预设内已复位，此处兜底
            _closeStarted = false;
            UninstallOutsideCloseHook(); // 中键钩子随板卸载（批次八）
            Hide();
            SystemBridge.TrimWorkingSet(); // 板关闭即整理工作集（内存优化：无交互期不占峰值内存）
            onDone?.Invoke();
            var dock = _dock;
            if (dock != null)
            {
                dock.SetBoardSession(false);
                if (revive)
                    dock.Revive(); // 收纳板关闭→悬浮窗消散倒放复现【L4】
            }
        });
    }

    public bool TryHandleEsc()
    {
        if (!IsVisible) return false;
        CloseBoard();
        return true;
    }

    // ---- 定位与尺寸（1.2/1.3；2026-08-29 重构：锚边贴屏幕边缘、center 自由模式、角部斜向缩放、board.pos.* 位置记忆）----

    /// <summary>
    /// 有效方向（2026-08-29 批次六）：advanced.popupDirection=auto（新默认）时**按悬浮窗位置推断**——
    /// 2026-10-02 判定范围可调（advanced.dockEdgeRange 0~100%）：屏幕两侧边缘判定带合计占该轴的百分比，
    /// 悬浮窗中心处于中央 (100−p)% 区域内→center（居中"果冻"弹出），否则判给最近的一条边。
    /// 25% 时=悬浮窗位于屏幕高/宽中央 3/4 区域才判居中。固定值 up/down/left/right/center 仍可直接指定。
    /// </summary>
    public string EffectiveDirection()
    {
        string raw = SettingsStore.Instance.GetString("advanced.popupDirection", "auto").Trim().ToLowerInvariant();
        if (raw is "up" or "down" or "left" or "right" or "center") return raw;
        var c = DockCenterPhysical();
        var b = ScreenBounds();
        double nLeft = (c.X - b.Left) / Math.Max(b.Width, 1);
        double nRight = (b.Right - c.X) / Math.Max(b.Width, 1);
        double nTop = (c.Y - b.Top) / Math.Max(b.Height, 1);
        double nBottom = (b.Bottom - c.Y) / Math.Max(b.Height, 1);
        double edge = Math.Clamp(SettingsStore.Instance.GetDouble("advanced.dockEdgeRange", 40.243902439024396), 0, 100) / 200.0; // 单侧边缘带
        if (nLeft > edge && nRight > edge && nTop > edge && nBottom > edge) return "center";
        double min = Math.Min(Math.Min(nLeft, nRight), Math.Min(nTop, nBottom));
        if (min == nRight) return "right";
        if (min == nLeft) return "left";
        if (min == nBottom) return "up"; // 贴屏幕下边缘 → 从下边滑出
        return "down";
    }

    /// <summary>悬浮窗三板组中心（物理像素）；悬浮窗未注入时用落盘 dock.pos + 窗口尺寸估算。</summary>
    Point DockCenterPhysical()
    {
        if (_dock != null)
            return _dock.DockCenterPhysical();
        var b = ScreenBounds();
        var pos = StateStore.Instance.GetPoint2("dock.pos");
        double sizePx = Math.Clamp(SettingsStore.Instance.GetDouble("display.dockSize", 58.7), 40, 96) + 72; // 窗口=板边长+72（01 决策 15）
        double x = pos is { } p ? p.X : b.Left + (b.Width - sizePx) / 2;
        double y = pos is { } q ? q.Y : b.Top + 16;
        return new Point(x + sizePx / 2, y + sizePx / 2);
    }

    /// <summary>全窗口径统一走有效方向（auto 时随悬浮窗位置；各处一次开板会话内取值一致）。</summary>
    string Direction => EffectiveDirection();
    bool IsCenterDirection => Direction == "center";

    double ScaleX() => DpiScaleX > 0 ? DpiScaleX : (_dock?.DpiScaleX ?? 1.0);
    double ScaleY() => DpiScaleY > 0 ? DpiScaleY : (_dock?.DpiScaleY ?? 1.0);

    // 布局补跑前的 ActualWidth/Height 还是**上一帧**的旧值（设 Width/Height 只排队布局，清单04任务16）：
    // PinAnchorEdge/Reposition/ClampIntoScreen 若读 Actual，锚定边就按旧尺寸回贴 → 边缘在两帧间
    // 来回横跳（改尺寸/贴边切换时的弹跳闪烁根因之一）。像素折算一律用刚写入的目标值，NaN 兜底 Actual。
    double WCur => double.IsNaN(Width) ? ActualWidth : Width;
    double HCur => double.IsNaN(Height) ? ActualHeight : Height;

    /// <summary>所选屏完整边界（物理像素；锚边贴屏幕边缘而非工作区/任务栏边缘，2026-08-29）。</summary>
    System.Drawing.Rectangle ScreenBounds() => Screens.SelectedScreen().Bounds;

    /// <summary>按方向读记忆尺寸（board.size.&lt;direction&gt;，含 center），默认 1334×806。
    /// 持久化口径=可见尺寸；窗口矩形=可见+锚边延伸（ApplyAnchorExtension）。</summary>
    void LoadBoardSize()
    {
        var saved = StateStore.Instance.GetPoint2($"board.size.{Direction}");
        Width = (saved is { } size && size.X > 0 ? size.X : DefaultW) + AnchorOutsetDipX;
        Height = (saved is { } s2 && s2.Y > 0 ? s2.Y : DefaultH) + AnchorOutsetDipY;
        ApplyAnchorExtension();
    }

    /// <summary>锚边延伸量（物理 px 折 DIP；center=0）。延伸方向=锚边对侧屏外方向。</summary>
    double AnchorOutsetDipX => Direction is "left" or "right" ? AnchorOutsetPx / ScaleX() : 0;
    double AnchorOutsetDipY => Direction is "up" or "down" ? AnchorOutsetPx / ScaleY() : 0;

    /// <summary>
    /// 锚边延伸落地（清单02任务17）：窗口矩形 Width/Height 已含延伸量（LoadBoardSize/ApplyResize
    /// 统一口径），这里只把 Root 内容按"延伸量−1px 搭接保险"反向内边距平移，可见内容位置不变。
    /// DPI 变化后经 Reposition 重入（DIP 折算随当前屏缩放）。
    /// </summary>
    void ApplyAnchorExtension()
    {
        double coverX = Math.Max(0, AnchorOutsetDipX * (AnchorOutsetPx - AnchorKeepCoverPx) / AnchorOutsetPx);
        double coverY = Math.Max(0, AnchorOutsetDipY * (AnchorOutsetPx - AnchorKeepCoverPx) / AnchorOutsetPx);
        Root.Margin = Direction switch
        {
            "down" => new Thickness(0, coverY, 0, 0),   // 顶边贴屏顶：窗口向上延伸，内容下移
            "up" => new Thickness(0, 0, 0, coverY),     // 底边贴屏底：窗口向下延伸，内容上移
            "left" => new Thickness(coverX, 0, 0, 0),   // 左边贴屏左：窗口向左延伸，内容右移
            "right" => new Thickness(0, 0, coverX, 0),  // 右边贴屏右：窗口向右延伸，内容左移
            _ => new Thickness(0),                      // center：无延伸
        };
    }

    /// <summary>尺寸上限=所选屏完整边界（板可覆盖任务栏；含延伸量口径）；min 160×107（可见，需求③）。</summary>
    void ClampSizeToWorkarea()
    {
        var b = ScreenBounds();
        Width = Math.Min(Width, b.Width / ScaleX() + AnchorOutsetDipX);
        Height = Math.Min(Height, b.Height / ScaleY() + AnchorOutsetDipY);
        Width = Math.Max(Width, MinBoardW + AnchorOutsetDipX);
        Height = Math.Max(Height, MinBoardH + AnchorOutsetDipY);
    }

    /// <summary>
    /// 托盘"复位"（07 §5）：除悬浮窗回默认位外，触发一次**全量刷新**——重建式拉取
    /// （RescanRebuildAsync：复位移除名单、逐个果冻重排），把"桌面与列表不一致"一把拉平。
    /// </summary>
    public void RequestFullRefresh() => _ = RescanRebuildAsync();

    /// <summary>桌面重建（资源管理器重启，07 §2）后重新对账：差量刷新，保留格序与滚动位置。</summary>
    public void RequestRescan() => _ = RescanAsync();

    /// <summary>
    /// 显示设置变化（07 §3 热插拔/分辨率）：重新按所选屏夹回尺寸与位置。
    /// 指定屏被拔掉时由 App 先把 display.screen 改回 auto，故此处 ScreenBounds 已是回退后的屏。
    /// </summary>
    public void OnDisplayChanged()
    {
        if (!IsVisible) return;
        ClampSizeToWorkarea();
        Reposition();
        InvalidateRevealCache();
        Logger.Info("显示设置变化：收纳板尺寸/位置已重新校验夹回");
    }

    /// <summary>
    /// 落位（2026-08-29）：锚边贴所选屏对应边缘（up=下边缘贴屏幕下边缘，以此类推；center=屏幕居中）；
    /// 用户在相同方向调整过位置（board.pos.direction）则恢复其位置，否则默认屏幕横向居中
    /// （额外要求 2：位置不受悬浮窗影响，仅锚定轴由方向决定）。
    /// </summary>
    void Reposition()
    {
        ApplyAnchorExtension(); // 方向/DPI 可能已变：先重铺延伸量与内容内边距（清单02任务17）
        var b = ScreenBounds();
        double sx = ScaleX(), sy = ScaleY();
        double wPx = WCur * sx, hPx = HCur * sy;
        if (wPx <= 0 || hPx <= 0) return;

        double x, y;
        var saved = StateStore.Instance.GetPoint2($"board.pos.{Direction}"); // 用户调整后的位置记忆
        if (saved is { } pos)
        {
            x = pos.X;
            y = pos.Y;
        }
        else if (IsCenterDirection)
        {
            x = b.Left + (b.Width - wPx) / 2;
            y = b.Top + (b.Height - hPx) / 2;
        }
        else
        {
            x = b.Left + (b.Width - wPx) / 2; // 默认屏幕横向居中
            y = Direction switch
            {
                "down" => b.Top - AnchorOutsetPx,
                "left" => b.Top + (b.Height - hPx) / 2,
                "right" => b.Top + (b.Height - hPx) / 2,
                _ => b.Bottom - hPx + AnchorOutsetPx, // up：贴屏幕下边缘并留数像素在屏外
            };
            if (Direction == "left") x = b.Left;
            if (Direction == "right") x = b.Right - wPx;
        }
        x = Math.Clamp(x, b.Left, Math.Max(b.Left, b.Right - wPx));
        y = Math.Clamp(y, b.Top, Math.Max(b.Top, b.Bottom - hPx));
        Left = x / sx;
        Top = y / sy;
        PinAnchorEdge();
        ApplyAnchorShadow();
    }

    /// <summary>锚定轴强制贴合屏幕边缘并留 AnchorOutsetPx 像素在屏外（批次四）；center 不动。</summary>
    void PinAnchorEdge()
    {
        if (IsCenterDirection) return;
        var b = ScreenBounds();
        double sx = ScaleX(), sy = ScaleY();
        double wPx = WCur * sx, hPx = HCur * sy;
        switch (Direction)
        {
            case "down": Top = (b.Top - AnchorOutsetPx) / sy; break;
            case "left": Left = (b.Left - AnchorOutsetPx) / sx; break;
            case "right": Left = (b.Right - wPx + AnchorOutsetPx) / sx; break;
            default: Top = (b.Bottom - hPx + AnchorOutsetPx) / sy; break; // up
        }
    }

    /// <summary>锚边过渡阴影（2026-08-29）：贴屏幕边缘一侧由重到轻；center 无锚边→隐藏。</summary>
    void ApplyAnchorShadow()
    {
        if (IsCenterDirection)
        {
            AnchorShadow.Visibility = Visibility.Collapsed;
            return;
        }
        AnchorShadow.Visibility = Visibility.Visible;
        Brush Grad(Color top, Color bottom) => new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        var heavy = Color.FromArgb(0x4A, 0, 0, 0);
        var none = Color.FromArgb(0, 0, 0, 0);
        switch (Direction)
        {
            case "down":
                AnchorShadow.VerticalAlignment = VerticalAlignment.Top;
                AnchorShadow.ClearValue(Border.WidthProperty);
                AnchorShadow.HorizontalAlignment = HorizontalAlignment.Stretch;
                AnchorShadow.Height = 44;
                AnchorShadow.CornerRadius = new CornerRadius(0, 0, 12, 12);
                AnchorShadow.Background = Grad(heavy, none); // 顶边（贴屏幕上边缘）最重，顶两角直角
                break;
            case "left":
                AnchorShadow.HorizontalAlignment = HorizontalAlignment.Left;
                AnchorShadow.VerticalAlignment = VerticalAlignment.Stretch;
                AnchorShadow.ClearValue(Border.HeightProperty);
                AnchorShadow.Width = 44;
                AnchorShadow.CornerRadius = new CornerRadius(0, 12, 12, 0);
                var lh = new LinearGradientBrush(heavy, none, new Point(0, 0), new Point(1, 0)); // 左边最重
                AnchorShadow.Background = lh;
                break;
            case "right":
                AnchorShadow.HorizontalAlignment = HorizontalAlignment.Right;
                AnchorShadow.VerticalAlignment = VerticalAlignment.Stretch;
                AnchorShadow.ClearValue(Border.HeightProperty);
                AnchorShadow.Width = 44;
                AnchorShadow.CornerRadius = new CornerRadius(12, 0, 0, 12);
                var rh = new LinearGradientBrush(none, heavy, new Point(0, 0), new Point(1, 0)); // 右边最重
                AnchorShadow.Background = rh;
                break;
            default: // up：底边贴屏幕下边缘
                AnchorShadow.VerticalAlignment = VerticalAlignment.Bottom;
                AnchorShadow.HorizontalAlignment = HorizontalAlignment.Stretch;
                AnchorShadow.ClearValue(Border.WidthProperty);
                AnchorShadow.Height = 44;
                AnchorShadow.CornerRadius = new CornerRadius(12, 12, 0, 0);
                AnchorShadow.Background = Grad(none, heavy); // 底边最重，底两角直角
                break;
        }
    }

    /// <summary>空白 Thumb 模板：默认模板自带白底 chrome，不受 Background=Transparent 控制。</summary>
    static ControlTemplate CreateTransparentThumbTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var template = new ControlTemplate(typeof(Thumb));
        template.VisualTree = factory;
        return template;
    }

    /// <summary>
    /// 构建当前方向的缩放热区（1.3 + 2026-08-29 角部斜向缩放）：
    /// 锚定方向的锚边不可调；相邻可调边交汇处加 14×14 角部热区（斜向双向调尺寸）。
    /// </summary>
    void RebuildResizeThumbs()
    {
        foreach (var thumb in _thumbs)
            Root.Children.Remove(thumb);
        _thumbs.Clear();

        void Add(string edge, bool horizontal)
        {
            var thumb = new Thumb
            {
                Template = CreateTransparentThumbTemplate(),
                Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
            };
            switch (edge)
            {
                case "top":
                    thumb.Height = ResizeHotZone;
                    thumb.HorizontalAlignment = HorizontalAlignment.Stretch;
                    thumb.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case "bottom":
                    thumb.Height = ResizeHotZone;
                    thumb.HorizontalAlignment = HorizontalAlignment.Stretch;
                    thumb.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case "left":
                    thumb.Width = ResizeHotZone;
                    thumb.VerticalAlignment = VerticalAlignment.Stretch;
                    thumb.HorizontalAlignment = HorizontalAlignment.Left;
                    break;
                case "right":
                    thumb.Width = ResizeHotZone;
                    thumb.VerticalAlignment = VerticalAlignment.Stretch;
                    thumb.HorizontalAlignment = HorizontalAlignment.Right;
                    break;
            }
            thumb.DragDelta += (_, e) => ApplyResize(new[] { edge }, e.HorizontalChange, e.VerticalChange);
            thumb.DragStarted += (_, _) => BeginResizeChrome();
            thumb.DragCompleted += (_, _) => EndResizeChrome();
            Root.Children.Add(thumb);
            _thumbs.Add(thumb);
        }

        void AddCorner(string edge1, string edge2)
        {
            bool nwse = (edge1 == "top" && edge2 == "left") || (edge1 == "bottom" && edge2 == "right");
            var thumb = new Thumb
            {
                Template = CreateTransparentThumbTemplate(),
                Width = 14,
                Height = 14,
                Cursor = nwse ? Cursors.SizeNWSE : Cursors.SizeNESW,
            };
            switch (edge1 + edge2)
            {
                case "topleft": thumb.HorizontalAlignment = HorizontalAlignment.Left; thumb.VerticalAlignment = VerticalAlignment.Top; break;
                case "topright": thumb.HorizontalAlignment = HorizontalAlignment.Right; thumb.VerticalAlignment = VerticalAlignment.Top; break;
                case "bottomleft": thumb.HorizontalAlignment = HorizontalAlignment.Left; thumb.VerticalAlignment = VerticalAlignment.Bottom; break;
                case "bottomright": thumb.HorizontalAlignment = HorizontalAlignment.Right; thumb.VerticalAlignment = VerticalAlignment.Bottom; break;
            }
            thumb.DragDelta += (_, e) => ApplyResize(new[] { edge1, edge2 }, e.HorizontalChange, e.VerticalChange);
            thumb.DragStarted += (_, _) => BeginResizeChrome();
            thumb.DragCompleted += (_, _) => EndResizeChrome();
            Root.Children.Add(thumb);
            _thumbs.Add(thumb);
        }

        switch (Direction)
        {
            case "down":
                Add("bottom", false); Add("left", true); Add("right", true);
                AddCorner("bottom", "left"); AddCorner("bottom", "right");
                break;
            case "left":
                Add("right", true); Add("top", false); Add("bottom", false);
                AddCorner("top", "right"); AddCorner("bottom", "right");
                break;
            case "right":
                Add("left", true); Add("top", false); Add("bottom", false);
                AddCorner("top", "left"); AddCorner("bottom", "left");
                break;
            case "center": // 自由模式：四边+四角全可调
                Add("top", false); Add("bottom", false); Add("left", true); Add("right", true);
                AddCorner("top", "left"); AddCorner("top", "right");
                AddCorner("bottom", "left"); AddCorner("bottom", "right");
                break;
            default: // up
                Add("top", false); Add("left", true); Add("right", true);
                AddCorner("top", "left"); AddCorner("top", "right");
                break;
        }
    }

    /// <summary>
    /// 缩放：被拖的边（1~2 条，角部=两条）各自移动；min 160×107（需求③），max=所选屏完整边界；
    /// 锚定轴随后强制回贴屏幕边缘；防抖持久化 board.size.&lt;direction&gt; 与 board.pos.&lt;direction&gt;。
    /// </summary>
    void ApplyResize(string[] edges, double dx, double dy)
    {
        bool hasTop = edges.Contains("top"), hasBottom = edges.Contains("bottom");
        bool hasLeft = edges.Contains("left"), hasRight = edges.Contains("right");
        var b = ScreenBounds();
        double maxW = b.Width / ScaleX() + AnchorOutsetDipX, maxH = b.Height / ScaleY() + AnchorOutsetDipY;

        double newW = WCur, newH = HCur;
        if (hasRight) newW = WCur + dx;
        else if (hasLeft) newW = WCur - dx;
        if (hasBottom) newH = HCur + dy;
        else if (hasTop) newH = HCur - dy;
        newW = Math.Clamp(newW, MinBoardW + AnchorOutsetDipX, maxW); // 窗口矩形口径：min/max 均含锚边延伸
        newH = Math.Clamp(newH, MinBoardH + AnchorOutsetDipY, maxH);

        double left = Left, top = Top;
        if (hasLeft) left = Left + (WCur - newW);
        if (hasTop) top = Top + (HCur - newH);

        Width = newW;
        Height = newH;
        Left = left;
        Top = top;
        PinAnchorEdge();
        ClampIntoScreen();
        RelayoutThrottled(); // 07 §4 热点 3：DragDelta 逐帧全量 Relayout → 30ms 节流（落定后 EndResizeChrome 补一次）
        _sizeSaveTimer.Stop();
        _sizeSaveTimer.Start();
    }

    // 缩放期 Relayout 降频（07 §4 遗留热点 3）：拖动一次鼠标可打出几十个 DragDelta，每个都全量
    // 重算格位分配 + 落盘比对（`changed` 判定）纯属浪费——缩放期行列本就冻结（_layoutViewportFrozen），
    // 格位分配不变。30ms 节流保留"轻微跟随"的观感，最终一致性由 EndResizeChrome 的 Relayout 兜底。
    long _lastResizeRelayoutTick;
    const int ResizeRelayoutIntervalMs = 30;

    void RelayoutThrottled()
    {
        long now = Environment.TickCount64;
        if (now - _lastResizeRelayoutTick < ResizeRelayoutIntervalMs) return;
        _lastResizeRelayoutTick = now;
        Relayout();
    }

    // ---- 缩放中的图标冻结与边缘模糊（清单二·任务10，替代旧"四边渐变条"批次六方案）----
    // 调整尺寸期间行列不重排（图标被窗口边缘裁切属预期）；四边改为整窗柔和模糊软化裁切边界，
    // 出现/消失都有过渡（00 §10 过渡铁律）；结束后解锁并 Relayout，图标经 IconGridPanel 缓动过渡到新位置。

    double? _layoutViewportFrozen; // 缩放开始时冻结的网格视口宽（冻结行列分配）
    bool _resizing;
    System.Windows.Media.Effects.BlurEffect? _resizeBlur; // 缩放期整窗模糊（EndResizeChrome 动画回 0 后摘除）

    void BeginResizeChrome()
    {
        _resizing = true;
        _layoutViewportFrozen = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Math.Max(0, ActualWidth - BoardPadLeft - BoardInnerPad);
        if (_resizeBlur == null)
        {
            _resizeBlur = new System.Windows.Media.Effects.BlurEffect { Radius = 0 };
            Root.Effect = _resizeBlur;
        }
        Anim.Run(_resizeBlur, System.Windows.Media.Effects.BlurEffect.RadiusProperty, null, 6,
            AnimTuning.ResizeBlur, EaseStyle.EaseInOutQuad);
    }

    void EndResizeChrome()
    {
        if (!_resizing) return;
        _resizing = false;
        _layoutViewportFrozen = null;
        var blur = _resizeBlur;
        if (blur != null)
        {
            Anim.Run(blur, System.Windows.Media.Effects.BlurEffect.RadiusProperty, null, 0,
                AnimTuning.ResizeBlur, EaseStyle.EaseInOutQuad,
                onDone: () =>
                {
                    if (!ReferenceEquals(Root.Effect, blur)) return; // 期间又开了一轮缩放：别摘新 Effect
                    Root.Effect = null;
                    _resizeBlur = null;
                });
        }
        _sizeSaveTimer.Stop();
        SaveSizeNow();
        Relayout(); // 解锁后重排 → 图标平滑过渡到新行列
    }

    /// <summary>关闭红点颜色过渡（批次十二）：ColorAnimation 挂在非冻结 SolidColorBrush 上。</summary>
    void AnimateCloseDot(Color to)
    {
        if (CloseDot.Fill is not SolidColorBrush brush || brush.IsFrozen) return;
        var anim = new ColorAnimation(to, Anim.Ms(AnimTuning.ChromeHover))
        {
            EasingFunction = AnimTuning.Map(EaseStyle.EaseInOutQuad),
        };
        Cship.Ui.Animations.AnimationClock.Apply(anim);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    /// <summary>按四角独立半径裁剪（旧的"自定义背景层"方案；2026-10-04 错误修复⑤后不再需要——
    /// 背景图改走 Border 圆角画刷绘制裁切，本方法保留给将来确需 Clip 的场景）。</summary>
    static void ApplyRoundedClip(FrameworkElement element, CornerRadius c)
    {
        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        double tl = c.TopLeft, tr = c.TopRight, br = c.BottomRight, bl = c.BottomLeft;
        double w = size.Width, h = size.Height;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(tl, 0), true, true);
            ctx.LineTo(new Point(w - tr, 0), true, false);
            ctx.ArcTo(new Point(w, tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(w, h - br), true, false);
            ctx.ArcTo(new Point(w - br, h), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(bl, h), true, false);
            ctx.ArcTo(new Point(0, h - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0, tl), true, false);
            ctx.ArcTo(new Point(tl, 0), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        element.Clip = geo;
    }

    /// <summary>窗口自由轴不出所选屏完整边界；锚定轴由 PinAnchorEdge 负责（含出屏 outset，不在此夹回）。</summary>
    void ClampIntoScreen()
    {
        var b = ScreenBounds();
        double sx = ScaleX(), sy = ScaleY();
        double wPx = WCur * sx, hPx = HCur * sy;
        bool pinX = Direction is "left" or "right";
        bool pinY = Direction is "up" or "down";
        double x = Left * sx, y = Top * sy;
        if (!pinX)
        {
            var nx = Math.Clamp(x, b.Left, Math.Max(b.Left, b.Right - wPx));
            if (Math.Abs(nx - x) > 0.5) Left = nx / sx;
        }
        if (!pinY)
        {
            var ny = Math.Clamp(y, b.Top, Math.Max(b.Top, b.Bottom - hPx));
            if (Math.Abs(ny - y) > 0.5) Top = ny / sy;
        }
    }

    /// <summary>缩放/拖动落盘：尺寸 + 用户调整后的位置（board.pos.&lt;direction&gt;，额外要求 2）。
    /// 尺寸持久化口径=可见尺寸（窗口矩形扣锚边延伸，清单02任务17）。</summary>
    void SaveSizeNow()
    {
        try
        {
            StateStore.Instance.SetPoint2($"board.size.{Direction}",
                (int)Math.Round(Width - AnchorOutsetDipX), (int)Math.Round(Height - AnchorOutsetDipY));
            StateStore.Instance.SetPoint2($"board.pos.{Direction}", (int)Math.Round(Left), (int)Math.Round(Top));
        }
        catch (Exception ex)
        {
            Logger.Warn($"保存收纳板尺寸/位置失败：{ex.Message}");
        }
        // 材质（2026-10-06 图层堆叠口径）：几何变化对材质**零成本**——模糊由 DWM 在窗口背后实时合成，
        // 本进程既无位图也无取景变换，故这里没有任何"落定后重抓/重裁"的动作。
    }

    // ---- 标题栏区域拖动移动窗口（2026-08-29 批次七收窄：仅标题栏带 34px 内可拖）----
    // up/down 仅横移、left/right 仅竖移、center 自由；锚定轴由 PinAnchorEdge 持续回贴，
    // 移动结果按方向记忆 board.pos.<direction>。板身空白区拖动不再移动窗口。

    const double TitleBandDragHeight = 34; // 标题栏带高度（可拖动区）
    Point? _movePress;
    Point _movePressWindow;

    void OnBlankDragMove(object sender, MouseButtonEventArgs e)
    {
        if (_renaming) return;
        if (e.GetPosition(Root).Y > TitleBandDragHeight) return;             // 仅标题栏区域
        if (FindAncestor<CategoryTabBar>(e.OriginalSource as DependencyObject) != null) return; // 分类按钮行留给按钮逻辑（04）
        if (HitIcon(e.OriginalSource as DependencyObject) != null) return;   // 图标区留给图标逻辑
        if (IsInChrome(e.OriginalSource as DependencyObject)) return;        // 按钮/垃圾桶
        if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) != null) return; // 缩放热区
        // 清掉上一次松手回弹的动画（动画持有 Left/Top 会吞掉拖动的直接赋值）
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        _movePress = PhysicalCursor();
        _movePressWindow = new Point(Left, Top);
        // 2026-10-06（图层堆叠口径）：拖动期间材质**无事可做**——模糊由 DWM 在窗口背后实时合成，
        // 板拖到哪，它底下就是那一刻的真实桌面（无快照、无取景变换、无刷新循环）。
        Root.CaptureMouse();
        e.Handled = true;
    }

    void OnBlankDragMoveUpdate(object sender, MouseEventArgs e)
    {
        if (_movePress == null || !Root.IsMouseCaptured) return;
        var cur = PhysicalCursor();
        double dx = (cur.X - _movePress.Value.X) / ScaleX();
        double dy = (cur.Y - _movePress.Value.Y) / ScaleY();
        switch (Direction)
        {
            case "up":
            case "down":
                Left = _movePressWindow.X + dx; // 上下滑出：仅横移
                break;
            case "left":
            case "right":
                Top = _movePressWindow.Y + dy; // 左右滑出：仅竖移
                break;
            default: // center：自由
                Left = _movePressWindow.X + dx;
                Top = _movePressWindow.Y + dy;
                break;
        }
        PinAnchorEdge(); // 拖动过程中锚定轴持续贴合（up/down 拖横移时高度轴不动，保险起见回贴）
    }

    void OnBlankDragMoveEnd(object sender, MouseButtonEventArgs e)
    {
        if (_movePress == null) return; // 捕获意外丢失等异常路径：材质无需收尾（系统模糊与几何无关）
        _movePress = null;
        if (Root.IsMouseCaptured) Root.ReleaseMouseCapture();
        AnimateClampIntoScreen(); // 出屏部分动画缓回（2026-10-02），落定后落盘
        e.Handled = true;
    }

    /// <summary>
    /// 拖动松手回弹（2026-10-02）：自由轴超出屏幕的部分以缓出动画收回到屏内
    /// （锚定轴在拖动中已由 PinAnchorEdge 持续贴齐，无需处理）；无越界则直接落盘。
    /// 时长按回弹距离折算（≤AnimTuning.BoardSnapBack），小幅越界快收、大幅慢回。
    /// </summary>
    void AnimateClampIntoScreen()
    {
        var b = ScreenBounds();
        double sx = ScaleX(), sy = ScaleY();
        double wPx = WCur * sx, hPx = HCur * sy;
        bool pinX = Direction is "left" or "right";
        bool pinY = Direction is "up" or "down";
        double x = Left * sx, y = Top * sy;
        double tx = pinX ? x : Math.Clamp(x, b.Left, Math.Max(b.Left, b.Right - wPx));
        double ty = pinY ? y : Math.Clamp(y, b.Top, Math.Max(b.Top, b.Bottom - hPx));
        double dist = Math.Max(Math.Abs(tx - x), Math.Abs(ty - y));
        if (dist < 0.5)
        {
            SaveSizeNow();
            return;
        }
        double dur = AnimTuning.BoardSnapBack * Math.Clamp(dist / 120.0, 0.35, 1.0);
        Action done = SaveSizeNow;
        if (pinY)
            Anim.Run(this, LeftProperty, null, tx / sx, dur, EaseStyle.EaseOutCubic, done); // 仅横移
        else
        {
            Anim.Run(this, LeftProperty, null, tx / sx, dur, EaseStyle.EaseOutCubic);
            Anim.Run(this, TopProperty, null, ty / sy, dur, EaseStyle.EaseOutCubic, done);
        }
    }

    // ---- 网格布局（1.1 / 2.2 / 任务 6 的 iconOrder；批次七改版：线性序列紧凑布局）----
    // 布局=线性序列（row-major）：任何增删/拖动/刷新后按当前视觉顺序紧凑重排——
    // 被移除/删除项留下的空位由后方图标顺移补齐，插入/腾位同理（IconGridPanel 缓动呈现）。

    /// <summary>当前视觉顺序序列（Row/Col 升序；未分配格的按集合顺序缀后）。</summary>
    List<BoardItem> VisualSequence()
        => _model.Items.Where(it => it.Row >= 0)
            .OrderBy(it => it.Row).ThenBy(it => it.Col)
            .Concat(_model.Items.Where(it => it.Row < 0))
            .ToList();

    /// <summary>
    /// 分类视图的显示顺序：按**全局序**（iconOrder 单元格）对过滤子集排序，无记录的缀后。
    /// 遍历可见集合 <see cref="BoardModel.Items"/>（源集合含被"移除"项，STEP_LOG 旧步骤04 缺陷 3 的教训）。
    /// </summary>
    IEnumerable<BoardItem> FilteredSequence()
        => _model.Items.Where(it => _iconOrder.TryGetValue(it.Name, out var rc) && rc.R >= 0 && rc.C >= 0)
            .OrderBy(it => _iconOrder[it.Name].R).ThenBy(it => _iconOrder[it.Name].C)
            .Concat(_model.Items.Where(it => !_iconOrder.TryGetValue(it.Name, out var rc2) || rc2.R < 0 || rc2.C < 0));

    /// <summary>把序列紧凑写入单元格（i → (i/cols, i%cols)）并覆盖 iconOrder 落盘。</summary>
    void CompactSequence(List<BoardItem> seq)
    {
        int cols = Math.Max(1, _cols);
        var cells = new Dictionary<string, (int R, int C)>(seq.Count);
        for (int i = 0; i < seq.Count; i++)
        {
            var it = seq[i];
            it.Row = i / cols;
            it.Col = i % cols;
            cells[it.Name] = (it.Row, it.Col);
        }
        _iconOrder = cells; // 只保留可见项（顺带清理已删除项的残留键）
        SaveIconOrder();
    }

    /// <summary>把项移动到目标线性格号：序列先摘除再插回目标位，后方图标腾/补位（2026-08-29 批次七）。</summary>
    void MoveItemToCell(BoardItem dragged, int cellLinear)
    {
        var seq = VisualSequence();
        seq.Remove(dragged);
        int cols = Math.Max(1, _cols);
        int at = seq.FindIndex(it => it.Row * cols + it.Col >= cellLinear);
        if (at < 0) seq.Add(dragged);
        else seq.Insert(at, dragged);
        CompactSequence(seq);
        Relayout();
    }

    /// <summary>
    /// 单元格分配（批次七）：先按 iconOrder 恢复既有布局，再按视觉顺序**紧凑重排**——
    /// 空位一律补齐；变化才落盘，防止无谓写盘。
    /// </summary>
    void Relayout()
    {
        if (IconPanel == null) return; // 模板未实例化（窗口未显示）时跳过
        InvalidateRevealCache(); // 格位/顺序可能已变：hoverReveal 清单下次使用前重建（07 §4 热点 2）
        double iconSize = Math.Clamp(SettingsStore.Instance.GetDouble("display.iconSize", 67.1), 32, 96);
        bool labels = SettingsStore.Instance.GetBool("display.showLabels", false); // 默认不显示文件名（2026-08-29）
        double colSpacing = Math.Clamp(SettingsStore.Instance.GetDouble("display.colSpacing", 10.9), 0, 32);
        double rowSpacing = Math.Clamp(SettingsStore.Instance.GetDouble("display.rowSpacing", 0), 0, 32);
        double cellH = IconItem.TopPad + iconSize + IconItem.IndicatorStrip + IconItem.LabelSpace(labels); // 底部 8px=指示灯预留条（2026-10-02）；文件名行数随 display.labelLines（2026-10-03）

        IconPanel.CellWidth = iconSize;
        IconPanel.CellHeight = cellH;
        IconPanel.ColSpacing = colSpacing;
        IconPanel.RowSpacing = rowSpacing;

        double viewport = _layoutViewportFrozen ?? Scroller.ViewportWidth; // 缩放中冻结（批次六：图标行列不动）
        if (viewport <= 0) viewport = Math.Max(0, ActualWidth - BoardPadLeft - BoardInnerPad);
        double pitch = iconSize + colSpacing;
        _cols = Math.Max(1, (int)((viewport + colSpacing) / pitch));
        int cols = Math.Max(1, _cols);

        if (IsFiltered)
        {
            // 分类视图（04 · 2026-09-25 改版）：过滤子集在视图内紧凑排列，只改 Row/Col 显示值，
            // **不写 iconOrder**——全局顺序不受分类切换影响（iconOrder 是唯一权威，04 §7.1）
            int i = 0;
            foreach (var it in FilteredSequence())
            {
                it.Row = i / cols;
                it.Col = i % cols;
                i++;
            }
            EmptyHint.Visibility = _firstLoadDone && _model.VisibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            IconPanel.InvalidateMeasure();
            return;
        }

        foreach (var item in _model.Items)
        {
            if (_iconOrder.TryGetValue(item.Name, out var rc)
                && rc.R >= 0 && rc.C >= 0 && rc.C < cols)
            {
                item.Row = rc.R;
                item.Col = rc.C;
            }
            else if (item.Row >= 0 && item.Col >= cols)
            {
                item.Row = -1;
                item.Col = -1; // 列数缩了：回炉重排
            }
        }

        var seq = VisualSequence();
        bool changed = seq.Count != _iconOrder.Count;
        for (int i = 0; i < seq.Count; i++)
        {
            var it = seq[i];
            var cell = (R: i / cols, C: i % cols);
            if (!_iconOrder.TryGetValue(it.Name, out var old) || old.R != cell.R || old.C != cell.C)
                changed = true;
            it.Row = cell.R;
            it.Col = cell.C;
        }
        if (changed)
            CompactSequence(seq);

        // 空态仅在首扫完成后判定（分批加载过程中不闪"空"提示）
        EmptyHint.Visibility = _firstLoadDone && _model.VisibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        IconPanel.InvalidateMeasure();
    }

    // ---- 扫描与刷新（2.1）----

    /// <summary>图标渐次回填（07 §4）：后台提完一枚图标 → 回 UI 线程把对应条目的 IconVersion 推一格，
    /// IconItem 据此重新解码（缓存路径不变，不能只靠 IconPath 变化触发）。</summary>
    void OnIconReady(string name)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnIconReady(name)));
            return;
        }
        var item = _model.All.FirstOrDefault(it =>
            string.Equals(it.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item == null) return; // 该条目已不在源集合（已删除/移除）
        item.IconVersion++;
    }

    async System.Threading.Tasks.Task RescanAsync()
    {
        if (_scanBusy || _scanner == null) return;
        _scanBusy = true;
        try
        {
            var virtualSnapshot = _virtualItems.ToList(); // 后台构建期间的 UI 写入隔离
            var items = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = _scanner.Scan();
                list.AddRange(BuildVirtualBoardItems(virtualSnapshot));
                return list;
            });
            // 文件消失预演（2026-08-30 批次十）：板可见时，对账前先给"将消失的可见项"播
            // 200ms 缩小淡出，播完再真正移除——文件消失有过渡、后方缓动补位，而不是瞬间蒸发。
            // 板隐藏/首载期间不等待（首载容器本就未生成，无所谓动画）。
            if (IsVisible && _firstLoadDone)
            {
                var incoming = new HashSet<string>(
                    items.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
                var vanishing = _model.Items.Where(it => !incoming.Contains(it.Name)).ToList();
                bool played = false;
                foreach (var it in vanishing)
                {
                    var icon = IconOf(it);
                    if (icon == null) continue;
                    icon.PlayDeleteFade(null);
                    played = true;
                }
                if (played)
                    await System.Threading.Tasks.Task.Delay(220); // 等淡出播完再对账移除
            }
            _model.SetSource(items);
            Logger.Info($"桌面扫描：源 {items.Count} 项");
            if (!_firstLoadDone)
            {
                _firstLoadDone = true;
                // 首载一律分批（16/批，Background 优先级）：容器生成摊到多帧，
                // 开板动画不再被一次性全量生成卡住（2026-08-29 批次七修"第一次打开卡"）
                _batchPending = true;
                _model.ReconcileBatched(16, _ =>
                {
                    Relayout();
                    if (_model.Items.Count >= _model.LastBatchTarget)
                    {
                        _populateDrained = true;    // 分批灌完（开板动画闸门条件之一，批次八）
                        _firstLoadPopulated = true; // 首载事实：不复位（任务13）
                        _batchPending = false;
                        CheckFirstLoadReady();      // 首载就绪 → 悬浮窗加载点散开（任务13）
                    }
                });
                if (_model.LastBatchTarget == 0)
                {
                    // 空桌面：一个批次都不会排，回调永远不来——直接视为灌完（任务13兜底）
                    _populateDrained = true;
                    _firstLoadPopulated = true;
                    _batchPending = false;
                    CheckFirstLoadReady();
                }
            }
            else
            {
                _model.Reconcile(); // 差量：复用实例 + Move，滚动/选中不跳【验收】
                _populateDrained = true; // 非首载无分批：闸门条件即刻满足（否则每次开板白等兜底时长）
            }
            CheckFirstLoadReady(); // 兜底覆盖：桌面无可见项时 LastBatchTarget 可能为 0，走不到批量回调
            PollRunning();
            Relayout();
            CatTabBar.OnSourceChanged(); // 文件消失自动从所有分类清除（04 §4.3）
            Logger.Info($"收纳板陈列：可见 {_model.VisibleCount} 项");
        }
        catch (Exception ex)
        {
            Logger.Error("桌面扫描失败", ex);
            ForceFirstLoadReady(); // 扫描异常也放行首载（任务13：防悬浮窗加载态永久卡住）
        }
        finally
        {
            _scanBusy = false;
        }
    }

    // ---- 刷新=重新从桌面拉取（清单三·任务12）：右键"刷新"专用的重建式刷新 ----
    // 后台扫描合成最终列表 → **渲染前先算好顺序**（iconOrder 格序在前的按 R,C 升序，
    // 无记录的按当前排序配置缀后）→ UI 线程逐个 Items.Add（间隔约 20ms 让帧），每个新容器
    // 经 BoardItem.JellyPending 一次性标志触发果冻入场。watcher 差量刷新（RescanAsync 的
    // Reconcile 路径）与首载分批（ReconcileBatched）保持不变。

    bool _rebuildRendering;

    async System.Threading.Tasks.Task RescanRebuildAsync()
    {
        if (_scanBusy || _rebuildRendering || _scanner == null) return; // 防重入（_scanBusy 沿用）
        _scanBusy = true;
        _rebuildRendering = true;
        try
        {
            var virtualSnapshot = _virtualItems.ToList(); // 后台构建期间的 UI 写入隔离
            var items = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = _scanner.Scan();
                list.AddRange(BuildVirtualBoardItems(virtualSnapshot)); // 虚拟项合成为最终列表（并集语义）
                return list;
            });
            _model.SetSource(items);
            _populateDrained = true; // 非首载：开板闸门即刻满足（同 RescanAsync 非首载分支）

            // 刷新=重新从桌面拉取（修订B语义）：**复位移除名单**——桌面文件在"全部"重新上架。
            // removedItems 只含桌面文件名（虚拟项移除即彻底删除，不进名单），天然满足
            // "刷新复活只对'全部'分类项和桌面文件生效"；分类成员登记不受影响（不影响 a/b）。
            // watcher 差量刷新（RescanAsync）不复位——只有用户点"刷新"才复活。
            if (_model.Removed.Count > 0)
            {
                int revived = _model.Removed.Count;
                StateStore.Instance.SetRemovedItems(new List<string>());
                _model.SetRemoved(new List<string>());
                Logger.Info($"刷新复位移除名单：{revived} 项在\"全部\"重新上架");
            }

            // 渲染前先算好顺序（出场顺序即最终顺序）：iconOrder 有记录的按 R,C 升序，
            // 无记录的按排序配置（DesiredSequence 已应用过滤+排序）缀后；OrderBy 稳定
            var desired = _model.DesiredSequence();
            bool HasOrder(BoardItem it) => _iconOrder.TryGetValue(it.Name, out var rc) && rc.R >= 0 && rc.C >= 0;
            var ordered = desired.Where(HasOrder)
                .OrderBy(it => _iconOrder[it.Name].R).ThenBy(it => _iconOrder[it.Name].C)
                .Concat(desired.Where(it => !HasOrder(it)))
                .ToList();

            var filter = _model.ViewFilter; // 过滤视图重建：只渲染视图内项（顺序仍按全局序）

            // 逐个渲染时**先按填充序分配单元格**（左上原点、行主序：第 1 个落 (0,0)、第 2 个 (0,1)…），
            // 容器生成即落在自己的格位——不再全部叠在第一行第一列（修订A·问题1，IconGridPanel
            // 对 Row/Col=-1 的容器一律按 (0,0) 落位）；全部渲染完成后 Relayout 按最终顺序缓动排序
            double iconSize = Math.Clamp(SettingsStore.Instance.GetDouble("display.iconSize", 67.1), 32, 96);
            double colSpacing = Math.Clamp(SettingsStore.Instance.GetDouble("display.colSpacing", 10.9), 0, 32);
            double viewport = _layoutViewportFrozen ?? Scroller.ViewportWidth;
            if (viewport <= 0) viewport = Math.Max(0, ActualWidth - BoardPadLeft - BoardInnerPad);
            int fillCols = Math.Max(1, (int)((viewport + colSpacing) / (iconSize + colSpacing)));

            _model.Items.Clear();
            int index = 0;
            foreach (var it in ordered)
            {
                if (filter != null && !filter(it)) continue;
                it.Row = index / fillCols;
                it.Col = index % fillCols;
                index++;
                it.JellyPending = true; // DataContextChanged 时消费 → PlayJellyAppear
                _model.Items.Add(it);
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(AnimTuning.JellyRebuildStepMs));
            }
            if (IsFiltered)
                ApplyCategoryFilter(); // 重建期间切过视图则自愈（含 Reconcile 归位）
            else
                Relayout();
            PollRunning();
            CatTabBar.OnSourceChanged();
            Logger.Info($"刷新重建：源 {items.Count} 项，移除名单 {_model.Removed.Count} 项，逐个果冻渲染 {_model.VisibleCount} 项");
        }
        catch (Exception ex)
        {
            Logger.Error("刷新重建失败", ex);
        }
        finally
        {
            _scanBusy = false;
            _rebuildRendering = false;
        }
    }

    // ---- 指示灯（3.6）----

    // 轮询节奏（2026-10-05 清单06任务1）：冲刺档（刚打开项目后 4s 内）300ms → 活跃档（有运行项）700ms
    // → 空闲档 2s。越灵敏=进程枚举/文件占用检测越频繁，故只在"有事发生"时提速。
    DateTime _pollBurstUntil = DateTime.MinValue;

    /// <summary>刚经板打开项目 → 进冲刺档并立即重排下一拍（不必等当前这一拍走完），
    /// 让"程序起得快"的项目在 ~0.3s 内被检出 → 三点尽早合并为指示灯。</summary>
    void BoostRunningPoll()
    {
        _pollBurstUntil = DateTime.UtcNow.AddMilliseconds(AnimTuning.RunningPollBurstHoldMs);
        ApplyPollCadence(forceNow: true);
    }

    /// <summary>按当前态势选档；forceNow=立即重排下一拍（改写 Interval 不会让已计时的那一拍提前）。</summary>
    void ApplyPollCadence(bool forceNow = false)
    {
        double ms = DateTime.UtcNow < _pollBurstUntil ? AnimTuning.RunningPollBurstMs
            : _model.All.Any(it => it.IsRunning) ? AnimTuning.RunningPollActiveMs
            : AnimTuning.RunningPollMs;
        var interval = Anim.Ms(ms);
        if (_runningTimer.Interval == interval)
        {
            if (forceNow) { _runningTimer.Stop(); _runningTimer.Start(); }
            return;
        }
        _runningTimer.Interval = interval;
        if (forceNow)
        {
            _runningTimer.Stop();
            _runningTimer.Start();
        }
    }

    void PollRunning()
    {
        var snapshot = _model.All.ToList(); // UI 线程快照：枚举与 IO 全在后台，不碰模型
        // 进程枚举放后台线程（批次八：约几十毫秒的枚举不该占用开板动画的 UI 线程帧）
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            HashSet<string> fullPaths, names;
            try
            {
                (fullPaths, names) = FileOps.RunningProcesses();
            }
            catch (Exception ex)
            {
                Logger.Warn($"运行状态检测失败：{ex.Message}");
                return;
            }
            // 文件占用检测也是磁盘 IO（2026-10-02）：一并留在后台线程。
            // 节流（2026-10-05 性能）：独占打开 = 一次磁盘 IO（还常被杀软拦一层），空闲档每 2s
            // 一拍 × 每个文件项 = 持续 IO；6s 内且 mtime 未变的项沿用上次结果（打开/关闭不改
            // mtime，故这里只节流"重复探测频率"，语义仍是"最近 6s 内探测到的占用态"）。
            var busyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in snapshot)
                if (it.Type == BoardItemType.Files && IsFileInUseThrottled(it))
                    busyFiles.Add(it.Path);
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsVisible) return;
                try
                {
                    foreach (var item in snapshot)
                    {
                        bool run = item.Type switch
                        {
                            BoardItemType.Apps => MatchExe(fullPaths, names, item),
                            // 文件夹按路径前缀 + "刚经板打开过"（清单05任务5：资源管理器开文件夹不产生
                            // 该路径下的进程，只靠前缀检测永远等不到三点合并）
                            BoardItemType.Folders => AnyProcessUnder(fullPaths, item.Path) || RecentlyOpened(item.Path),
                            // 文件（2026-10-02）：被占用（独占打开失败）或刚经板打开过 → 亮
                            _ => busyFiles.Contains(item.Path) || RecentlyOpened(item.Path),
                        };
                        if (item.IsRunning != run)
                            item.IsRunning = run;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"运行状态检测失败：{ex.Message}");
                }
                ApplyPollCadence(); // 按这拍的结果重选档（有运行项→活跃档，全灭→空闲档）
            });
        });
    }

    bool RecentlyOpened(string path)
    {
        if (!_recentlyOpened.TryGetValue(path, out var t)) return false;
        if ((DateTime.UtcNow - t).TotalMilliseconds < RecentOpenedHoldMs) return true;
        _recentlyOpened.TryRemove(path, out _); // 过期即清：原实现只增不减，长跑无界堆积
        return false;
    }

    // ---- 文件占用探测节流（性能 · 2026-10-05）----
    const long BusyRecheckMs = 6000;
    sealed class BusyProbe
    {
        public long MtimeTicks;
        public long CheckedAtMs;
        public bool Busy;
    }
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, BusyProbe> _busyProbes
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>占用探测节流：同一文件 6s 内且 mtime 未变则复用上次结果（后台线程调用）。
    /// 桌面文件项数量有界，超过阈值淘汰最旧一半兜底。</summary>
    bool IsFileInUseThrottled(BoardItem item)
    {
        long nowMs = Environment.TickCount64;
        long mtime = item.Mtime.Ticks;
        if (_busyProbes.TryGetValue(item.Path, out var p)
            && p.MtimeTicks == mtime && nowMs - p.CheckedAtMs < BusyRecheckMs)
            return p.Busy;
        bool busy = FileOps.IsFileInUse(item.Path);
        _busyProbes[item.Path] = new BusyProbe { MtimeTicks = mtime, CheckedAtMs = nowMs, Busy = busy };
        if (_busyProbes.Count > 512)
            foreach (var key in _busyProbes.OrderBy(kv => kv.Value.CheckedAtMs)
                         .Take(_busyProbes.Count / 2).Select(kv => kv.Key).ToList())
                _busyProbes.TryRemove(key, out _);
        return busy;
    }

    static string EffectiveExePath(BoardItem item)
    {
        if (item.Type == BoardItemType.Apps)
        {
            var ext = Path.GetExtension(item.Path)?.ToLowerInvariant();
            if (ext is ".exe" or ".bat" or ".cmd") return item.Path;
            if (ext == ".lnk")
            {
                var t = Path.GetExtension(item.TargetPath ?? "")?.ToLowerInvariant();
                return t == ".exe" ? item.TargetPath ?? "" : "";
            }
        }
        return "";
    }

    static bool MatchExe(HashSet<string> fullPaths, HashSet<string> names, BoardItem item)
    {
        var path = EffectiveExePath(item);
        if (string.IsNullOrEmpty(path)) return false;
        return fullPaths.Contains(path) || names.Contains(Path.GetFileName(path));
    }

    static bool AnyProcessUnder(HashSet<string> fullPaths, string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        string prefix = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
        return fullPaths.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // ---- 鼠标：选中 / 双击打开 / 拖动换位与拖删（3.8 / 5 / 6）----

    IconItem? HitIcon(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is IconItem icon)
                return icon.RenameActive ? null : icon;
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    IconItem? IconOf(BoardItem item)
    {
        if (IconPanel == null) return null;
        foreach (var child in IconPanel.Children)
        {
            if (child is ContentPresenter cp)
            {
                var icon = FindDescendant<IconItem>(cp);
                if (icon?.Item == item) return icon;
            }
        }
        return null;
    }

    static T? FindDescendant<T>(DependencyObject root) where T : class
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            var deep = FindDescendant<T>(child);
            if (deep != null) return deep;
        }
        return null;
    }

    void OnGridPress(object sender, MouseButtonEventArgs e)
    {
        if (_renaming) return;
        var icon = HitIcon(e.OriginalSource as DependencyObject);
        var item = icon?.Item;
        if (item == null) return;
        if (e.ClickCount >= 2)
        {
            RemoveGhost();
            EndDrag(); // 首击若已升级为拖动（微位移>阈值），双击直接作废拖态并打开（2026-08-29 修"双击无效"）
            if (DoubleClickOpenEnabled(item))
                OpenItem(item, icon); // 双击打开（开关开启时生效，3.8）
            e.Handled = true;
            return;
        }
        // 单击=选中（无视觉描边，2026-08-28 用户反馈取消蓝框）
        _pressItem = item;
        _pressScreen = PhysicalCursor();
        _phase = DragPhase.Pressing;
        GridHost.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>"双击打开"开关对该项是否开启（关闭=单击即打开，2026-10-02 用户要求）。</summary>
    bool DoubleClickOpenEnabled(BoardItem item)
        => item.Type == BoardItemType.Folders
            ? SettingsStore.Instance.GetBool("advanced.dblOpenFolders", true)
            : SettingsStore.Instance.GetBool("advanced.dblOpenItems", true);

    void OnGridMove(object sender, MouseEventArgs e)
    {
        UpdateReveal(e.GetPosition(GridHost)); // hoverReveal（3.5）
        UpdateHoverTip(e.GetPosition(GridHost)); // 悬停文件名提示（2026-10-04 错误修复④）

        if (_phase == DragPhase.None || _pressItem == null || !GridHost.IsMouseCaptured) return;
        var cursor = PhysicalCursor();
        if (_phase == DragPhase.Pressing)
        {
            bool locked = SettingsStore.Instance.GetBool("advanced.lockIconDrag", false);
            if (locked)
                return; // 锁定图标拖动：禁一切拖动（含换位与拖删）【补充决策】
            if (Math.Abs(cursor.X - _pressScreen.X) > DragThresholdPx
                || Math.Abs(cursor.Y - _pressScreen.Y) > DragThresholdPx)
            {
                _phase = DragPhase.Dragging;
                var icon = IconOf(_pressItem);
                if (icon == null) { EndDrag(); return; }
                StartGhost(_pressItem, icon, e.GetPosition(Root));
            }
        }
        if (_phase == DragPhase.Dragging)
        {
            MoveGhost(e.GetPosition(Root));
            SetTrashHighlight(IsOverTrash(e.GetPosition(Root))); // 悬停垃圾桶高亮+微放大+危险色
            CatTabBar.SetDragHover(e.GetPosition(Root)); // 悬停分类按钮高亮（04 §4，松开=加入）
            UpdateFolderDropTarget(_pressItem, e.GetPosition(GridHost), e.GetPosition(Root)); // 悬停文件夹中心圈（清单06任务3）
        }
    }

    void OnGridRelease(object sender, MouseButtonEventArgs e)
    {
        var phase = _phase;
        var item = _pressItem;
        bool overTrash = _trashHot; // 释放捕获会同步走 EndDrag 清掉高亮，先快照
        var dropFolder = _dropFolderIcon?.Item; // 同上：中心圈目标也要在 EndDrag 清描边前快照（清单06任务3）
        var cursor = PhysicalCursor();
        if (GridHost.IsMouseCaptured)
            GridHost.ReleaseMouseCapture(); // 同步触发 LostMouseCapture → EndDrag（复位拖态，ghost 保留）
        if (phase != DragPhase.Dragging || item == null)
        {
            // 未拖动的单击释放：该类型的"双击打开"开关关闭时=单击即打开（2026-10-02 用户要求）
            if (phase == DragPhase.Pressing && item != null && !DoubleClickOpenEnabled(item))
                OpenItem(item, IconOf(item));
            RemoveGhost();
            return;
        }
        if (overTrash)
        {
            RemoveGhost();
            DeleteViaTrash(item); // 拖进垃圾桶=删除（回收站）
        }
        else if (dropFolder != null)
        {
            // 落在文件夹图标的中心圈内=移入该文件夹（2026-10-05 清单06任务3）；圈外不会有描边，
            // dropFolder 为 null，落到下面的换位/移出语义
            DropIntoFolder(item, dropFolder);
        }
        else if (CatTabBar.CategoryAt(e.GetPosition(Root)) is { } dropCat)
        {
            // 拖到顶部分类按钮上松开=加入该分类（04 §4，复制语义：仅成员登记，
            // 原图标留在板网格不动，同一文件可属多个分类）。
            // 过滤视图补充移出语义（清单三·任务14）：拖到其他分类=加入目标并从当前分类移出；
            // 拖回当前分类自身=无效回弹；"全部"视图行为不变（纯加入）。
            var srcCat = _currentCategory;
            if (IsFiltered && srcCat is { Id: not "all" } fromCat)
            {
                if (dropCat.Id == fromCat.Id)
                {
                    ReturnGhostToOrigin(RemoveGhost); // 拖回自身：无效，拖影回弹
                }
                else
                {
                    RemoveGhost();
                    CatTabBar.AddToCategory(dropCat, item);
                    LeaveCurrentCategory(item, fromCat); // 视图内淡出 → 从当前分类移出（板/全局不受影响）
                }
            }
            else
            {
                RemoveGhost();
                CatTabBar.AddToCategory(dropCat, item);
            }
        }
        else if (IsOutsideBoard(cursor))
        {
            if (IsFiltered && _currentCategory is { Id: not "all" } fromCat2)
            {
                // 过滤视图内拖出板外=仅从当前分类移出（清单三·任务14）：
                // RemoveFromBoard 是全局移除（removedItems 名单），语义错误，不在此使用
                RemoveGhost();
                LeaveCurrentCategory(item, fromCat2);
            }
            else
            {
                // 拖出板外松开=移除（仅取消显示，不删源文件；2026-08-29 批次六）。
                // 拖影 200ms 淡出即消失动画，播完才移除模型+后方缓动补位（2026-08-30 批次十）；
                // 原图标自拖动起已淡出为透明，无需再播移除动画（animate:false）
                FadeGhostAway(() => RemoveFromBoard(item, animate: false));
            }
        }
        else
        {
            var posInGrid = e.GetPosition(GridHost);
            // 网格内换位/插入（任务 6）：主板直接改全局序列；分类视图走"成员槽位重排"
            // （2026-10-05 清单05任务6：过滤视图原先禁止拖动换位，用户要求与"全部"一致可拖可换）
            if (IsValidDrop(item, posInGrid))
            {
                RemoveGhost();
                DropToCell(item, posInGrid);
            }
            else
            {
                ReturnGhostToOrigin(RemoveGhost); // 无效位置：拖影 200ms 过渡回原位（2026-08-29）
            }
        }
    }

    /// <summary>复活已移除项（清单三修订B语义）：移出 removedItems 名单并放回可见集合。
    /// 触发面=重新拖入（此处）与"刷新"重建式拉取（RescanRebuildAsync 复位名单）。</summary>
    void ReviveRemovedItem(BoardItem item)
    {
        var removed = StateStore.Instance.GetRemovedItems();
        if (removed.Remove(item.Name))
        {
            StateStore.Instance.SetRemovedItems(removed.ToList());
            _model.RestoreName(item.Name); // Reconcile → Pass 放行 → 重新进入可见集合
            Logger.Info($"拖入复活（移除项重新上架）：{item.Name}");
        }
    }

    /// <summary>从当前过滤分类移出（清单三·任务14）：图标先播缩小淡出（视图内消失过渡），
    /// 播完才移除成员（MembersChanged → Reconcile 移除+后方补位）；板/全局/其他分类不受影响。</summary>
    void LeaveCurrentCategory(BoardItem item, CategoryModel fromCat)
    {
        var icon = IconOf(item);
        if (icon != null)
            icon.PlayDeleteFade(() => Dispatcher.BeginInvoke(() => CatTabBar.RemoveMember(fromCat, item.Name)));
        else
            CatTabBar.RemoveMember(fromCat, item.Name);
    }

    /// <summary>释放点是否在收纳板窗口（物理矩形）之外——拖出板外松开=移除（批次六）。</summary>
    bool IsOutsideBoard(Point cursorPhysical)
    {
        double sx = ScaleX(), sy = ScaleY();
        var rect = new Rect(Left * sx, Top * sy, ActualWidth * sx, ActualHeight * sy);
        return !rect.Contains(cursorPhysical);
    }

    /// <summary>目标格是否有效（换位/插入）：在网格内且不是原地。</summary>
    bool IsValidDrop(BoardItem dragged, Point posInGrid)
    {
        if (IconPanel == null) return false;
        double cw = IconPanel.CellWidth, ch = IconPanel.CellHeight;
        double cs = IconPanel.ColSpacing, rs = IconPanel.RowSpacing;
        int c = (int)Math.Floor(posInGrid.X / (cw + cs));
        int r = (int)Math.Floor((posInGrid.Y + rs / 2) / (ch + rs));
        if (r < 0 || c < 0) return false; // 拖出网格=无效
        c = Math.Min(c, _cols - 1);
        return !(r == dragged.Row && c == dragged.Col);
    }

    /// <summary>拖动状态复位（松开/捕获丢失共用）；ghost 交给动画或 RemoveGhost 收尾。</summary>
    void EndDrag()
    {
        _phase = DragPhase.None;
        _pressItem = null;
        SetTrashHighlight(false);
        CatTabBar.SetDragHover(null); // 分类按钮拖入高亮一并清除
        HideFolderRing();             // 拖入文件夹描边淡出一并清除（2026-10-05 清单06任务3）
    }

    /// <summary>移除拖影并还原原图标显示。</summary>
    void RemoveGhost()
    {
        if (_ghostPopup != null)
        {
            _ghostPopup.IsOpen = false;
            _ghostPopup.Child = null;
            _ghostPopup = null;
        }
        _ghost = null;
        if (_dragIcon != null)
        {
            // 还原整个图标（含文件名）：淡入过渡（2026-08-30 批次十，替代瞬现）；
            // HandoffBehavior 默认替换——若淡出未完即松手，从当前值平滑续播
            Anim.Run(_dragIcon, OpacityProperty, null, 1, AnimTuning.GhostFade, EaseStyle.EaseInOutQuad);
            _dragIcon = null;
        }
    }

    /// <summary>拖出板外移除：拖影在松开处淡出收尾，完成后回调（2026-08-30 批次十加回调：
    /// 拖影淡出即该项的"消失动画"，移除模型与后方补位等淡出播完再动）。</summary>
    void FadeGhostAway(Action? done = null)
    {
        if (_ghost == null) { RemoveGhost(); done?.Invoke(); return; }
        var ghost = _ghost;
        Anim.Run(ghost, OpacityProperty, null, 0, AnimTuning.GhostReturn, EaseStyle.EaseInQuad,
            onDone: () => { RemoveGhost(); done?.Invoke(); });
    }

    static Point PhysicalCursor()
    {
        var p = WinForms.Cursor.Position;
        return new Point(p.X, p.Y);
    }

    // ---- 拖影（5：半透明拖影跟随；2026-08-29：拖动时原位置隐藏原图标+文件名，拖到无效位置
    //      松开→拖影平滑过渡回原位；批次八：拖影搬进独立 Popup 弹出层——它有自己的顶层 HWND，
    //      可渲染到收纳板窗口之外，"拖出到板外的图标"全程可见）----

    Popup? _ghostPopup; // 拖影宿主（顶层弹出层，跟随光标、可越出窗口边界）
    Point _ghostOrigin; // 拖动起点图标原位（Root 坐标），用于回弹

    void StartGhost(BoardItem item, IconItem icon, Point posInRoot)
    {
        double iconSize = Math.Clamp(SettingsStore.Instance.GetDouble("display.iconSize", 67.1), 32, 96);
        var border = new Border
        {
            Opacity = 0.65,
            Width = iconSize,
            Height = iconSize,
        };
        if (!string.IsNullOrEmpty(item.IconPath) && File.Exists(item.IconPath))
        {
            // 拖影位图走 IconBitmapCache 共享（同路径单实例；192px 解码，原全尺寸解码的内存峰值消除）
            var bmp = IconBitmapCache.GetOrLoad(item.IconPath, 192);
            if (bmp != null)
                border.Child = new Image { Source = bmp, Stretch = Stretch.Uniform };
        }
        _ghost = border;
        _ghostOrigin = icon.IconVisual.TranslatePoint(new Point(0, 0), Root); // 原位（含 hover 抬升）
        _ghostPopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = true, // 不抢鼠标捕获（拖动捕获在 GridHost 上）
            Placement = PlacementMode.RelativePoint,
            PlacementTarget = Root,
            PopupAnimation = PopupAnimation.None,
            IsHitTestVisible = false,
            Child = border,
        };
        _ghostPopup.IsOpen = true;
        MoveGhost(posInRoot);
        // 原位置整项淡出（图标+文件名，2026-08-29 批次六"拖动时暂时隐藏被拖文件的文件名"；
        // 2026-08-30 批次十：零时长瞬隐改淡出过渡）；用动画不写本地值（配合 hoverReveal）
        Anim.Run(icon, OpacityProperty, null, 0, AnimTuning.GhostFade, EaseStyle.EaseInOutQuad);
        _dragIcon = icon;
    }

    IconItem? _dragIcon;

    /// <summary>无效位置松开：拖影过渡回原位再收尾（2026-08-29）。Popup 偏移双轴各一条动画，
    /// 全部到齐才收尾（Anim.Parallel 计数）。</summary>
    void ReturnGhostToOrigin(Action done)
    {
        if (_ghostPopup == null || _ghost == null) { done(); return; }
        var popup = _ghostPopup;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(popup, Popup.HorizontalOffsetProperty, null, _ghostOrigin.X, AnimTuning.GhostReturn, EaseStyle.EaseInOutQuad, ok),
            ok => Anim.Run(popup, Popup.VerticalOffsetProperty, null, _ghostOrigin.Y, AnimTuning.GhostReturn, EaseStyle.EaseInOutQuad, ok),
        }, done);
    }

    void MoveGhost(Point posInRoot)
    {
        if (_ghostPopup == null || _ghost == null) return;
        _ghostPopup.HorizontalOffset = posInRoot.X - _ghost.Width / 2;
        _ghostPopup.VerticalOffset = posInRoot.Y - _ghost.Height / 2;
    }

    bool IsOverTrash(Point posInRoot)
    {
        var tl = TrashBtn.TranslatePoint(new Point(0, 0), Root);
        var rect = new Rect(tl, new Size(TrashBtn.ActualWidth, TrashBtn.ActualHeight));
        return rect.Contains(posInRoot);
    }

    // 垃圾桶盖开合（清单二·任务6；修订A·4.4）：悬停向上微开+侧向开盖（HoverCycle 完整播放）；
    // 图标拖到其上时盖向上+侧向微开（铰链感）、灰色圆角底板转深灰
    readonly Animations.HoverCycle _trashCycle = new();
    const double TrashLidHoverLift = 2.0;
    const double TrashLidHoverTilt = 8.0;  // 悬停开盖角度（修订A·4.4，绕铰链侧开）
    const double TrashLidHotLift = 3.0;
    const double TrashLidHotTilt = -14.0;

    /// <summary>垃圾桶底板颜色：悬停=浅灰、图标拖入=深灰（修订A·4.4），主题自适应。</summary>
    Color TrashPlateColor(bool hot) => hot
        ? Color.FromArgb(0x59, 0x00, 0x00, 0x00)
        : (ThemeResolver.IsDark() ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x9E, 0x9E, 0x9E));

    /// <summary>底板过渡浮现（修订A·4.4）；已在显示中则只做颜色过渡（悬停灰↔拖入深灰）。</summary>
    void ShowTrashPlate(bool hot)
    {
        if (TrashPlate.Visibility != Visibility.Visible)
        {
            TrashPlate.Background = new SolidColorBrush(TrashPlateColor(hot));
            TrashPlate.Visibility = Visibility.Visible;
            TrashPlate.Opacity = 0;
        }
        else
        {
            AnimateTrashPlateColor(TrashPlateColor(hot));
        }
        Anim.Run(TrashPlate, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
    }

    /// <summary>底板过渡淡出；回调内复核状态防"刚又悬停/拖入"误隐藏。</summary>
    void HideTrashPlate()
    {
        Anim.Run(TrashPlate, OpacityProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInOutQuad,
            onDone: () =>
            {
                if (!TrashBtn.IsMouseOver && !_trashHot)
                    TrashPlate.Visibility = Visibility.Collapsed;
            });
    }

    void AnimateTrashPlateColor(Color to)
    {
        if (TrashPlate.Background is SolidColorBrush brush && !brush.IsFrozen)
        {
            var anim = new ColorAnimation(to, Anim.Ms(AnimTuning.Micro))
            {
                EasingFunction = AnimTuning.Map(EaseStyle.EaseInOutQuad),
            };
            Cship.Ui.Animations.AnimationClock.Apply(anim);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
        else
        {
            TrashPlate.Background = new SolidColorBrush(to);
        }
    }

    void SetTrashHighlight(bool hot)
    {
        if (_trashHot == hot) return;
        _trashHot = hot;
        TrashHalo.Visibility = hot ? Visibility.Visible : Visibility.Collapsed;
        var brush = new SolidColorBrush(hot
            ? (Color)ColorConverter.ConvertFromString("#FF6B6B")
            : (ThemeResolver.IsDark() ? Color.FromRgb(0xB8, 0xB8, 0xB8) : Color.FromRgb(0x5F, 0x5F, 0x5F)));
        TrashBody.Fill = brush;
        TrashLid.Fill = brush;
        TrashBody.Opacity = hot ? 1 : 0.8;
        TrashLid.Opacity = hot ? 1 : 0.8;
        // 底板：拖入=深灰；拖离后按指针位置回落悬停灰或淡出（修订A·4.4）
        if (hot) ShowTrashPlate(true);
        else if (TrashBtn.IsMouseOver) ShowTrashPlate(false);
        else HideTrashPlate();
        if (hot)
        {
            Anim.Run(TrashLidShift, TranslateTransform.YProperty, null, -TrashLidHotLift, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
            Anim.Run(TrashLidTilt, RotateTransform.AngleProperty, null, TrashLidHotTilt, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
        }
        else
        {
            // 拖离落回：若悬停盖开在播，HoverCycle 播完按指针位置补账，不在此强行打断
            Anim.Run(TrashLidTilt, RotateTransform.AngleProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
            if (!TrashBtn.IsMouseOver)
                Anim.Run(TrashLidShift, TranslateTransform.YProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
        }
    }

    void OnTrashClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            SuppressOutsideClose(); // 资源管理器窗口抢焦点，不触发"板外收起"
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn($"打开回收站失败：{ex.Message}");
        }
    }

    void DeleteViaTrash(BoardItem item)
    {
        SuppressOutsideClose(); // 删除失败时的系统错误对话框不应触发"板外收起"
        // 缩小淡出动画先行，删除进回收站放后台；成功后立即对账移除
        IconOf(item)?.PlayDeleteFade(null);
        var path = item.Path;
        bool virtualItem = item.IsVirtual;
        _ = System.Threading.Tasks.Task.Run(() => FileOps.RecycleDelete(path))
            .ContinueWith(t =>
            {
                bool ok = t.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    if (ok)
                    {
                        if (virtualItem)
                        {
                            _virtualItems.RemoveAll(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));
                            SaveVirtualItems();
                        }
                        _model.RemoveItem(item);
                    }
                    Relayout();
                });
            });
    }

    // ---- 拖到文件夹图标中心 → 移入该文件夹（2026-10-05 清单06任务3）----

    IconItem? _dropFolderIcon; // 当前命中的文件夹目标（指针在其中心圈内）

    /// <summary>
    /// 拖到文件夹图标"中心圈"内 → 目标图标四周浮出粗描边（移入提示）。
    /// 中心圈 = 以图标**原始中心**为圆心、图标宽度 × AnimTuning.FolderDropRadiusRatio（=1/3）为半径的圆；
    /// 圈外一律没有这套逻辑（描边淡出，松开时仍走原有换位/移出/拖删语义）。圆心取图标格位原点
    /// 推算（IconItem 自身原点不含 hover 上跳的渲染变换），拖动中指针掠过目标也不会让判定圈跟着抖。
    /// </summary>
    void UpdateFolderDropTarget(BoardItem dragged, Point posInGrid, Point posInRoot)
    {
        var target = FindAncestor<IconItem>(GridHost.InputHitTest(posInGrid) as DependencyObject);
        var folder = target?.Item;
        if (folder == null || ReferenceEquals(folder, dragged)
            || folder.Type != BoardItemType.Folders || !Directory.Exists(folder.Path))
        {
            HideFolderRing(); // 拖动项自身/非文件夹/文件夹已不存在 → 不算目标
            return;
        }
        var origin = target!.TranslatePoint(new Point(0, 0), Root);
        if (double.IsNaN(origin.X) || double.IsNaN(origin.Y)) { HideFolderRing(); return; }
        double size = target.ActualWidth; // 格宽 = display.iconSize（Relayout 装配），也是"图标宽度"口径
        double radius = size * AnimTuning.FolderDropRadiusRatio;
        double dx = posInRoot.X - (origin.X + size / 2);
        double dy = posInRoot.Y - (origin.Y + IconItem.TopPad + size / 2);
        if (Math.Sqrt(dx * dx + dy * dy) > radius)
        {
            HideFolderRing();
            return;
        }
        ShowFolderRing(target, origin, size);
    }

    /// <summary>描边几何：WPF Border 的描边画在布局框内侧，故外框取"图标 + 间距 + 线宽"时，
    /// 笔画恰好落在距图标 FolderRingGapPx 处（描边不与图标贴合）。颜色随主题反相（黑主题白/白主题黑），
    /// 出现为渐变（先归零再渐入，首帧不闪）。</summary>
    void ShowFolderRing(IconItem icon, Point origin, double size)
    {
        bool changed = !ReferenceEquals(_dropFolderIcon, icon);
        _dropFolderIcon = icon;
        if (changed)
        {
            double inset = AnimTuning.FolderRingGapPx + AnimTuning.FolderRingStrokePx;
            double outer = size + 2 * inset;
            FolderDropRing.Width = outer;
            FolderDropRing.Height = outer;
            FolderDropRing.CornerRadius = new CornerRadius(outer * 0.24);
            FolderDropRing.BorderThickness = new Thickness(AnimTuning.FolderRingStrokePx);
            FolderDropRing.BorderBrush = new SolidColorBrush(ThemeResolver.IsDark() ? Colors.White : Colors.Black);
            Canvas.SetLeft(FolderDropRing, origin.X - inset);
            Canvas.SetTop(FolderDropRing, origin.Y + IconItem.TopPad - inset);
        }
        if (FolderDropRing.Visibility != Visibility.Visible)
        {
            FolderDropRing.Visibility = Visibility.Visible;
            FolderDropRing.Opacity = 0; // 出现渐变起点
        }
        Anim.Run(FolderDropRing, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
    }

    /// <summary>描边渐隐（离开中心圈/拖动结束）；已无目标则无操作。回调里复核当前目标，
    /// 防"渐隐途中又进圈（含换到另一个文件夹）"被误收成 Collapsed。</summary>
    void HideFolderRing()
    {
        if (_dropFolderIcon == null) return;
        _dropFolderIcon = null;
        Anim.Run(FolderDropRing, OpacityProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInOutQuad,
            onDone: () =>
            {
                if (_dropFolderIcon == null)
                    FolderDropRing.Visibility = Visibility.Collapsed;
            });
    }

    /// <summary>
    /// 拖到文件夹图标中心圈内松手 = 把该项对应的源文件/文件夹移入目标文件夹。
    /// 原图标先缩小淡出，移入成功后从模型摘除 → Relayout 紧凑补位（不留空位、原图标消失）；
    /// 移入失败（被占用/无权限/把文件夹拖进自身或子目录）→ 图标还原淡入并提示，板内容不动。
    /// 同名冲突由 FileOps.MoveInto 自动 "(2)" 递增，**绝不覆盖**。
    /// </summary>
    void DropIntoFolder(BoardItem dragged, BoardItem folder)
    {
        SuppressOutsideClose(); // 文件占用/权限的系统提示框不应触发"板外收起"
        RemoveGhost();
        HideFolderRing();
        string src = dragged.Path;
        bool virtualItem = dragged.IsVirtual;
        var icon = IconOf(dragged);
        icon?.PlayDeleteFade(null);
        Logger.Info($"拖入文件夹：{dragged.Name} → {folder.Name}");
        _ = System.Threading.Tasks.Task.Run(() => FileOps.MoveInto(src, folder.Path))
            .ContinueWith(t =>
            {
                string? dest = t.Result;
                Dispatcher.BeginInvoke(() =>
                {
                    if (dest == null)
                    {
                        icon?.PlayAppear(); // 失败：还原已被淡出的原图标（淡入 + 微放大）
                        MessageBox.Show(this, I18n.Tr("msg.move.failed"), "Cship",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (virtualItem)
                    {
                        // 虚拟项的登记路径已随文件移走 → 从清单摘除（与垃圾桶删除同口径）
                        _virtualItems.RemoveAll(v => string.Equals(v.Path, src, StringComparison.OrdinalIgnoreCase));
                        SaveVirtualItems();
                    }
                    _recentlyOpened.TryRemove(src, out _);
                    _openCooldown.Remove(src);
                    _model.RemoveItem(dragged);
                    Relayout();                  // 补位：后方图标紧凑顺移，不留空位
                    CatTabBar.OnSourceChanged(); // 移走的项已不存在 → 分类成员清理
                });
            });
    }

    void DropToCell(BoardItem dragged, Point posInGrid)
    {
        // 拖到哪个格就"移动插入"到哪个位置（后方图标腾位/补位顺移，2026-08-29 批次七序列化布局）；
        // CompactSequence 内已落盘。分类视图走成员槽位重排（清单05任务6）。
        int cell = CellIndexAt(posInGrid);
        if (IsFiltered)
            MoveItemInFilteredView(dragged, cell);
        else
            MoveItemToCell(dragged, cell);
    }

    /// <summary>
    /// 分类视图内换位（2026-10-05 清单05任务6）：过滤子集在视图内重排——只把这批"成员"在
    /// **全局序**里占用的槽位重排，非成员项的相对顺序与槽位一律不动（iconOrder 是唯一权威，04 §7.1）。
    /// 落点口径与主板 MoveItemToCell 一致：摘除拖动项后按线性格号插入。
    /// </summary>
    void MoveItemInFilteredView(BoardItem dragged, int cellLinear)
    {
        var members = FilteredSequence().ToList();
        int from = members.IndexOf(dragged);
        if (from < 0) return;
        members.RemoveAt(from);
        members.Insert(Math.Clamp(cellLinear, 0, members.Count), dragged);

        // 全局序：iconOrder 有记录的按 (R,C) 升序在前，其余按来源集合顺序缀后（与 Relayout 同口径）
        var global = _iconOrder.OrderBy(kv => kv.Value.R).ThenBy(kv => kv.Value.C)
            .Select(kv => kv.Key).ToList();
        var seen = new HashSet<string>(global, StringComparer.OrdinalIgnoreCase);
        foreach (var it in _model.All)
            if (seen.Add(it.Name)) global.Add(it.Name);

        // 成员槽位=全局序中属于本视图的格位；把重排后的成员顺序放回这些槽位
        var memberNames = new HashSet<string>(members.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        var slots = new List<int>();
        for (int i = 0; i < global.Count; i++)
            if (memberNames.Contains(global[i])) slots.Add(i);
        int n = Math.Min(slots.Count, members.Count);
        for (int i = 0; i < n; i++)
            global[slots[i]] = members[i].Name;

        int cols = Math.Max(1, _cols);
        var cells = new Dictionary<string, (int R, int C)>(global.Count);
        for (int i = 0; i < global.Count; i++)
            cells[global[i]] = (i / cols, i % cols);
        _iconOrder = cells;
        SaveIconOrder();
        Relayout(); // 过滤视图 Relayout 按 FilteredSequence（全局序过滤）紧凑排列 → 视图内呈现新顺序
        Logger.Info($"分类视图换位：{dragged.Name} → 视图格 {cellLinear}（成员槽位重排，全局序已落盘）");
    }

    void SaveIconOrder()
    {
        try
        {
            StateStore.Instance.SetIconOrder(_iconOrder);
        }
        catch (Exception ex)
        {
            Logger.Warn($"保存 iconOrder 失败：{ex.Message}");
        }
    }

    // ---- 打开（3.7/3.8；2026-10-05 清单05任务5：恢复"三点循环到打开完成"的打开动画）----

    readonly Dictionary<string, DateTime> _openCooldown = new(StringComparer.OrdinalIgnoreCase);
    const double OpenCooldownMs = 1500; // 同一文件打开冷却（2026-08-29：防连点重复打开/动画叠加）

    /// <summary>淘汰已过冷却期的条目；极端情况下（全是新鲜项）再按时间清一半（UI 线程调用）。</summary>
    void PruneOpenCooldown()
    {
        if (_openCooldown.Count <= 256) return;
        var now = DateTime.UtcNow;
        foreach (var key in _openCooldown
                     .Where(kv => (now - kv.Value).TotalMilliseconds > OpenCooldownMs)
                     .Select(kv => kv.Key).ToList())
            _openCooldown.Remove(key);
        if (_openCooldown.Count > 512)
            foreach (var key in _openCooldown.OrderBy(kv => kv.Value).Take(256).Select(kv => kv.Key).ToList())
                _openCooldown.Remove(key);
    }

    // "正在使用或打开"指示灯的最近打开记录（2026-10-02）：文档类程序（如记事本）加载后
    // 未必长期持有文件句柄，经板打开过的文件至少亮一段时间，占用检测（IsFileInUse）接管后续。
    // 2026-10-05 清单05任务5：文件夹一并登记——资源管理器打开文件夹不产生"路径前缀下的进程"，
    // 只靠 AnyProcessUnder 检测不到，三点动画就永远等不到合并时机（文件夹打开也是"打开完成"）。
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _recentlyOpened = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>"刚经板打开过"的保持时长（2026-10-05 清单06任务1：10s → 4s）。
    /// 该标记兼两职——① 三点动画的合并时机（必须覆盖"检测不到运行态"的文件/文件夹）；
    /// ② 指示灯的点亮依据（文件夹只能靠它）。首拍冲刺轮询 0.3s 内就能吃到①，
    /// 故保持时长可以压到 4s：文件/文件夹关闭后指示灯不必再亮满 10s。</summary>
    const double RecentOpenedHoldMs = 4000;

    /// <summary>调用方已按 dblOpenItems/dblOpenFolders 开关决定单击/双击可达此处；
    /// 开关关闭=单击即打开（2026-10-02 用户要求）。</summary>
    void OpenItem(BoardItem item, IconItem? icon)
    {
        SuppressOutsideClose(3000); // 启动的应用会夺焦：3s 内不失焦收起（批次七）

        // 1.5s 冷却：同一文件在冷却期内忽略再次打开（2026-08-29）
        if (_openCooldown.TryGetValue(item.Path, out var last)
            && (DateTime.UtcNow - last).TotalMilliseconds < OpenCooldownMs)
            return;

        _openCooldown[item.Path] = DateTime.UtcNow;
        PruneOpenCooldown(); // 冷却表清理（原实现只增不减，长跑随打开次数无界增长）
        if (item.Type is BoardItemType.Files or BoardItemType.Folders)
            _recentlyOpened[item.Path] = DateTime.UtcNow; // "刚经板打开过就亮"的打开路径（文件/文件夹）
        BoostRunningPoll(); // 2026-10-05 清单06任务1：打开后进冲刺档，三点尽早等到"打开完成"合并
        bool animated = icon != null && SettingsStore.Instance.GetBool("advanced.bounceOpen", true);
        Logger.Info($"打开项目：{item.Name}（动效={animated}）");

        try
        {
            bool ok = FileOps.Open(item.Path);
            if (!animated)
                return;
            if (ok)
            {
                icon!.PlayOpenPop(); // ① 点击即时反馈：图标短缩弹一下（1→0.8→1，220ms）
                icon!.PlayOpening(); // ② 指示灯位三点循环 → IsRunning 转亮时合并为指示灯（与启动并行）
            }
            else
                icon!.AbortOpening(); // 打开失败：三点淡出，不留等待态
        }
        catch (Exception ex)
        {
            Logger.Warn($"打开项目异常：{ex.Message}");
        }
    }

    // ---- hoverReveal（3.5）----

    // ---- 滚轮平滑滚动（2026-08-29）：滚轮只推进目标偏移，渲染帧按比例插值逼近（缓出观感）----

    double _scrollTarget;
    bool _scrollAnimating;

    void OnSmoothWheel(object sender, MouseWheelEventArgs e)
    {
        if (Scroller.ScrollableHeight <= 0) return;
        _scrollTarget = Math.Clamp(_scrollTarget - e.Delta, 0, Scroller.ScrollableHeight);
        if (!_scrollAnimating)
        {
            _scrollAnimating = true;
            CompositionTarget.Rendering += OnScrollFrame;
        }
        e.Handled = true;
    }

    void OnScrollFrame(object? sender, EventArgs e)
    {
        double diff = _scrollTarget - Scroller.VerticalOffset;
        if (Math.Abs(diff) < AnimTuning.ScrollFrameEpsilon)
        {
            Scroller.ScrollToVerticalOffset(_scrollTarget);
            _scrollAnimating = false;
            CompositionTarget.Rendering -= OnScrollFrame;
            return;
        }
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + diff * AnimTuning.ScrollFrameLerp); // 每帧按比例逼近，约 120ms 收敛
    }

    // ---- 拖文件入板（2026-08-29 批次七；清单三·任务15 改版桌面文件语义）----
    // 桌面已有文件拖入=移动到落点格（同内部拖拽）；拖到分类按钮=加入该分类；
    // 非桌面文件拖入=仅收纳板"快捷方式"（state.virtualItems 持久化）。
    // **实时腾位**：拖拽悬停在板身某区域 >100ms 后，该区域后的图标先行腾出一个空位
    // （预览落点）；拖到别的区域→空位迁移；拖动取消/失败→空位恢复。预览只动 Row/Col 不写 iconOrder。
    // 不显示绿膜/系统"复制+"角标（DragOver 报 Move 效果，无加号提示）。
    // 并集语义（任务15）：虚拟项先入主网格再入分类、"全部"恒含一切新增图标——AddVirtualItem/
    // AddToCategory 既有路径天然满足，复核未破坏。

    int? _dropHole;                 // 当前预览空位（线性格号）
    int _dropHolePending = -1;      // 待生效的悬停区域
    readonly DispatcherTimer _dropHoleTimer;
    int _hystCell = -1;             // 迟滞格号（拖入腾位判定用，-1=未初始化）
    const double CellHysteresisPx = 14; // 格边界迟滞带（约格宽 20%）

    void OnRootDragFileOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move; // Move=系统光标无"复制+"小角标（批次七取消可互动提示）
        e.Handled = true;
        if (IsFiltered)
            return; // 分类视图：不做腾位预览（过滤视图的格号≠全局序列，Drop 统一追加到全局尾）
        int cell = CellIndexAtHyst(e.GetPosition(GridHost));
        if (_dropHole == cell) return;                       // 空位已在当前区域
        if (_dropHolePending == cell && _dropHoleTimer.IsEnabled) return; // 已在计时
        _dropHolePending = cell;
        _dropHoleTimer.Stop();
        _dropHoleTimer.Start(); // 悬停超 100ms 才腾位
    }

    /// <summary>
    /// DragLeave 探针（2026-08-30 批次十一）：实测拖动经过板时 Leave 高频误报（光标明明在板内，
    /// OLE 命中在板与"非目标"间震荡）——只有光标物理位置真在板矩形之外才取消腾位补位恢复；
    /// 板内 Leave 记日志（含 WindowFromPoint 探针：OLE 眼中的目标窗口）但保持预览。
    /// </summary>
    void OnRootDragLeaveProbe()
    {
        var p = PhysicalCursor();
        bool outside = IsOutsideBoard(p);
        string target = "";
        try
        {
            var hwnd = SystemBridge.WindowFromPoint(new SystemBridge.POINT { X = (int)p.X, Y = (int)p.Y });
            var sb = new System.Text.StringBuilder(64);
            SystemBridge.GetClassName(hwnd, sb, 64);
            SystemBridge.GetWindowThreadProcessId(hwnd, out uint pid);
            target = $"ole目标={sb} pid={pid}";
        }
        catch { /* 探针失败不影响主流程 */ }
        Logger.Info($"拖入：DragLeave 光标=({(int)p.X},{(int)p.Y}) 板外={outside} 板rect=({Left * ScaleX():F0},{Top * ScaleY():F0},{ActualWidth * ScaleX():F0}x{ActualHeight * ScaleY():F0}) {target}");
        if (outside)
            CancelDropPreview();
        // 板内 Leave：预览与计时保持，等下一次 Over/Enter/Drop 接管
    }

    /// <summary>
    /// 带迟滞带的格号判定（仅拖入腾位预览用，2026-08-30 批次十）：
    /// 原判定对格边界零容错——鼠标在相邻格边界附近微抖 1px，格号就在两格间跳变，
    /// 每 100ms 空位迁移一次，边界图标被反复"腾位→补位"循环。迟滞带内保持原格；
    /// 深入新格超过 CellHysteresisPx 才切换；跨多格（快速移动）不受迟滞限制。
    /// Drop 落点仍用精确的 CellIndexAt。
    /// </summary>
    int CellIndexAtHyst(Point posInGrid)
    {
        int raw = CellIndexAt(posInGrid);
        if (_hystCell < 0 || raw == _hystCell) { _hystCell = raw; return raw; }
        int cols = Math.Max(1, _cols);
        int rawC = raw % cols, rawR = raw / cols;
        int oldC = _hystCell % cols, oldR = _hystCell / cols;
        if (Math.Abs(rawC - oldC) + Math.Abs(rawR - oldR) == 1 && IconPanel != null) // 仅相邻格需要迟滞
        {
            if (rawC != oldC) // 列相邻
            {
                double pitch = IconPanel.CellWidth + IconPanel.ColSpacing;
                double boundary = (Math.Min(rawC, oldC) + 1) * pitch;
                bool deep = rawC > oldC
                    ? posInGrid.X >= boundary + CellHysteresisPx
                    : posInGrid.X <= boundary - CellHysteresisPx;
                if (!deep) return _hystCell;
            }
            else // 行相邻（CellIndexAt 行判定带 +rs/2 偏移，逻辑边界相应换算）
            {
                double pitch = IconPanel.CellHeight + IconPanel.RowSpacing;
                double boundary = (Math.Min(rawR, oldR) + 1) * pitch - IconPanel.RowSpacing / 2;
                bool deep = rawR > oldR
                    ? posInGrid.Y >= boundary + CellHysteresisPx
                    : posInGrid.Y <= boundary - CellHysteresisPx;
                if (!deep) return _hystCell;
            }
        }
        _hystCell = raw;
        return raw;
    }

    void ApplyDropPreview()
    {
        if (_dropHole == null || IconPanel == null) return;
        int cols = Math.Max(1, _cols);
        int hole = _dropHole.Value;
        int idx = 0;
        foreach (var it in VisualSequence())
        {
            if (idx == hole) idx++; // 跳过空位：其后图标整体后移一格
            it.Row = idx / cols;
            it.Col = idx % cols;
            idx++;
        }
        IconPanel.InvalidateMeasure(); // 缓动腾位；预览态不写 iconOrder
        Logger.Info($"腾位预览应用：洞={hole}");
    }

    void CancelDropPreview()
    {
        _dropHoleTimer.Stop();
        _dropHolePending = -1;
        _hystCell = -1; // 迟滞格号随预览会话重置（批次十）
        if (_dropHole == null) return;
        Logger.Info($"腾位预览取消（DragLeave）：洞={_dropHole}");
        _dropHole = null;
        Relayout(); // iconOrder 未动 → 图标回流恢复原位
    }

    void OnDropFiles(object sender, DragEventArgs e)
    {
        _dropHoleTimer.Stop();
        _hystCell = -1; // 迟滞格号随预览会话重置（批次十）
        int hole = IsFiltered ? int.MaxValue : _dropHole ?? CellIndexAt(e.GetPosition(GridHost));
        _dropHole = null; // 已落座的空位即最终位置，图标无需再动
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        int added = 0;
        foreach (var path in paths)
        {
            var dropCat = CatTabBar.CategoryAt(e.GetPosition(Root));
            // 桌面已有文件（清单三·任务15）：拖到分类按钮=加入该分类；拖到空白=移动到落点格
            // （同内部拖拽语义）。旧"拖入忽略"分支删除——桌面文件拖入被吞是感知失效的根因。
            if (IsDesktopPath(path))
            {
                var item = _model.All.FirstOrDefault(it =>
                    string.Equals(it.Path, path, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    // 不在陈列源（源集合恒为桌面全量，此处仅极端时序兜底）
                    Logger.Info($"拖入忽略（桌面文件不在陈列源）：{path}");
                    continue;
                }
                if (_model.Removed.Contains(item.Name))
                    ReviveRemovedItem(item); // 重新拖入=复活（修订B：先把移除项放回"全部"，再走加入/移动）
                if (dropCat != null)
                {
                    CatTabBar.AddToCategory(dropCat, item);
                    continue;
                }
                if (IsFiltered)
                {
                    AppendGlobalOrder(item); // 过滤视图：不动过滤序列，挪到全局尾（回"全部"可见）
                    Logger.Info($"拖入收纳（过滤视图）：{item.Name} 追加全局尾");
                }
                else
                {
                    MoveItemToCell(item, hole + added); // 移动到落点格（CompactSequence 内落盘）
                    Logger.Info($"拖入收纳（桌面文件移动）：{item.Name} → 格 {hole + added}");
                }
                continue;
            }
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            AddVirtualItem(path, hole + added); // 多文件按落点顺序连排；分类视图=int.MaxValue 追加
            if (dropCat != null)
            {
                var added2 = _model.All.FirstOrDefault(it =>
                    string.Equals(it.Path, path, StringComparison.OrdinalIgnoreCase));
                if (added2 != null)
                    CatTabBar.AddToCategory(dropCat, added2);
            }
            added++;
        }
        if (added > 0)
            Logger.Info($"拖入收纳：新增 {added} 个仅收纳板快捷方式");
        e.Handled = true;
    }

    /// <summary>路径是否已在桌面（用户/公共桌面）——拖这些入板=无效操作。</summary>
    bool IsDesktopPath(string path)
    {
        foreach (var dir in new[] { DesktopScanner.UserDesktopDir, DesktopScanner.CommonDesktopDir })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var full = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var cmp = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(cmp, full, StringComparison.OrdinalIgnoreCase)
                || cmp.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>板内坐标 → 线性格号（row-major；超出按边界夹回）。</summary>
    int CellIndexAt(Point posInGrid)
    {
        if (IconPanel == null || _cols <= 0) return int.MaxValue;
        double cw = IconPanel.CellWidth, ch = IconPanel.CellHeight;
        double cs = IconPanel.ColSpacing, rs = IconPanel.RowSpacing;
        int c = Math.Max(0, (int)Math.Floor(posInGrid.X / (cw + cs)));
        int r = Math.Max(0, (int)Math.Floor((posInGrid.Y + rs / 2) / (ch + rs)));
        c = Math.Min(c, _cols - 1);
        return r * _cols + c;
    }

    /// <summary>
    /// 拖入文件 → 板内快捷方式（07 §4 遗留热点 1：原实现在 **UI 线程**做 COM 解析（.lnk 目标）
    /// + Shell 图标提取 + 写盘，拖入一批文件时整板卡住）。现改为：构建（COM/IO）在后台线程，
    /// 回 UI 线程只做"挂模型 + 落格位 + 落盘"（这些必须碰 WPF 对象）。
    /// 提取本身由 <see cref="DesktopScanner"/> 的串行闸门兜住（一批文件不会并发打 shell）。
    /// </summary>
    async void AddVirtualItem(string path, int cellLinear)
    {
        BoardItem item;
        try
        {
            item = await System.Threading.Tasks.Task.Run(() => DesktopScanner.BuildFromPath(path));
        }
        catch (Exception ex)
        {
            Logger.Warn($"拖入收纳失败（后台构建）{path}：{ex.Message}");
            return;
        }
        try
        {
            item.IsVirtual = true;
            // 重名（现有项/移除名单——移除按名字过滤会把同名虚拟项藏掉）→ "名称 (2)" 递增
            var taken = new HashSet<string>(_model.All.Select(it => it.Name), StringComparer.OrdinalIgnoreCase);
            taken.UnionWith(_model.Removed);
            string name = item.Name, baseName = name;
            int n = 2;
            while (taken.Contains(name))
                name = $"{Path.GetFileNameWithoutExtension(baseName)} ({n++}){Path.GetExtension(baseName)}";
            item.Name = name;
            _virtualItems.Add((path, ""));
            SaveVirtualItems();
            _model.AddItem(item); // 源集合+可见集合（尾部），随后移动到落点
            if (IsFiltered)
                AppendGlobalOrder(item); // 分类视图：追加全局序尾，不重排（iconOrder 权威不被过滤视图破坏）
            else
                MoveItemToCell(item, cellLinear);
            Logger.Info($"拖入收纳（仅收纳板快捷方式）：{path}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"拖入收纳失败 {path}：{ex.Message}");
        }
    }

    List<BoardItem> BuildVirtualBoardItems(List<(string Path, string Name)> snapshot)
    {
        var list = new List<BoardItem>(snapshot.Count);
        foreach (var (path, name) in snapshot)
        {
            try
            {
                var item = DesktopScanner.BuildFromPath(path);
                if (!string.IsNullOrEmpty(name)) item.Name = name;
                item.IsVirtual = true;
                list.Add(item);
            }
            catch (Exception ex)
            {
                Logger.Warn($"收纳板虚拟项构建失败 {path}：{ex.Message}");
            }
        }
        return list;
    }

    void SaveVirtualItems()
    {
        try
        {
            StateStore.Instance.SetVirtualItems(_virtualItems);
        }
        catch (Exception ex)
        {
            Logger.Warn($"保存 virtualItems 失败：{ex.Message}");
        }
    }

    // ---- 右键"移除"（2026-08-29）：从当前视图隐藏，state.removedItems 持久化。
    // 修订B语义：移除=隐藏而非删除——重新拖入或点"刷新"（重建式拉取复位名单）都会复活；
    // 分类成员登记不受移除影响（_source 恒保留，OnSourceChanged 的 live 集合含移除项）。
    // 仅收纳板的虚拟项被移除/删除=彻底从 virtualItems 清单中删除（无复活）。
    // 语义（2026-08-29 批次六）：移除=仅取消显示；删除（垃圾桶）=取消显示+删源文件（回收站）。
    // 仅收纳板的虚拟项被移除/删除=彻底从 virtualItems 清单中删除。
    // 2026-08-30 批次十：移除先播 200ms 缩小淡出（消失过渡），播完才移除模型、后方缓动补位。

    /// <summary>移除（带消失过渡）：animate=true 时先播缩小淡出再移除模型；
    /// 拖出板外路径原图标已随拖动淡出透明（拖影淡出即消失动画），传 false 跳过。</summary>
    void RemoveFromBoard(BoardItem item, bool animate = true)
    {
        if (animate)
        {
            var icon = IconOf(item);
            if (icon != null)
            {
                icon.PlayDeleteFade(() => Dispatcher.BeginInvoke(() => FinishRemoveFromBoard(item)));
                return;
            }
        }
        FinishRemoveFromBoard(item);
    }

    void FinishRemoveFromBoard(BoardItem item)
    {
        if (item.IsVirtual)
        {
            _virtualItems.RemoveAll(v => string.Equals(v.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            SaveVirtualItems();
            _model.RemoveItem(item);
            Relayout(); // 后方图标缓动补位
            CatTabBar.OnSourceChanged(); // 分类成员联动（04 §4.3）
            return;
        }
        var removed = StateStore.Instance.GetRemovedItems();
        removed.Add(item.Name);
        StateStore.Instance.SetRemovedItems(removed.ToList());
        _model.RemoveName(item.Name);
        Relayout();
        CatTabBar.OnSourceChanged(); // "移除"不触发 rescan，分类清理在此联动
    }

    // hoverReveal 的图标清单缓存（07 §4 遗留热点 2）：原实现每次 MouseMove 都对全部容器做
    // FindDescendant 递归 + TranslatePoint。现改为维护一份 List<IconItem>，只在"容器集合变化"
    // （Relayout 之后）或缓存不完整（模板尚未应用）时重建。SetReveal 端的短路（批次九）保留。
    List<IconItem>? _revealIcons;
    int _revealIconChildCount = -1;
    bool _revealCacheDirty = true;

    /// <summary>容器集合可能已变（Relayout/对账/重建后调用）：下次 UpdateReveal 重建清单。</summary>
    void InvalidateRevealCache() => _revealCacheDirty = true;

    static readonly List<IconItem> EmptyRevealIcons = new();

    /// <summary>当前网格内的图标清单（缓存；不完整时不缓存，下次重建）。</summary>
    List<IconItem> RevealIcons()
    {
        var panel = IconPanel;
        if (panel == null) return EmptyRevealIcons;
        int n = panel.Children.Count;
        if (_revealIcons != null && !_revealCacheDirty && _revealIconChildCount == n)
            return _revealIcons;

        var list = new List<IconItem>(n);
        bool complete = true;
        foreach (var child in panel.Children)
        {
            if (child is not ContentPresenter cp) continue;
            var icon = FindDescendant<IconItem>(cp);
            if (icon == null) { complete = false; continue; } // 模板未应用：本轮不缓存
            list.Add(icon);
        }
        if (complete)
        {
            _revealIcons = list;
            _revealIconChildCount = n;
            _revealCacheDirty = false;
        }
        return list;
    }

    void UpdateReveal(Point posInGrid)
    {
        if (IconPanel == null) return;
        if (!string.Equals(SettingsStore.Instance.GetString("display.iconStyle", "original"),
                "hoverReveal", StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var icon in RevealIcons())
        {
            if (icon.Item == null) continue;
            var origin = icon.TranslatePoint(new Point(0, 0), GridHost);
            if (double.IsNaN(origin.X) || double.IsNaN(origin.Y)) continue;
            // 圆形渐显（2026-10-02 重做）：以指针为圆心、RevealRadius 为半径的圈内才显示，
            // 圆缘渐隐=虚化裁切图标；传指针在图标区内坐标给 OpacityMask 定心
            var posInIcon = new Point(posInGrid.X - origin.X, posInGrid.Y - origin.Y);
            // 圆心到图标矩形最近点 ≤ 半径=圆圈与该图标相交（否则整枚隐藏）
            bool inside = DistanceToRect(posInIcon, icon.ActualWidth, icon.ActualHeight) <= RevealRadius;
            icon.SetReveal(inside, posInIcon, RevealRadius);
        }
    }

    /// <summary>点到矩形的最短距离（指针在图标格内也算命中圈：半径以图标边缘为基准而非中心）。</summary>
    static double DistanceToRect(Point p, double w, double h)
    {
        double dx = Math.Max(Math.Max(-p.X, p.X - w), 0);
        double dy = Math.Max(Math.Max(-p.Y, p.Y - h), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>指针离开网格：全部图标回隐藏态（圆形渐显无"最后悬停残影"）。</summary>
    void HideAllReveals()
    {
        if (IconPanel == null) return;
        foreach (var icon in RevealIcons())
            icon.SetReveal(false, default, RevealRadius);
    }

    // ---- 悬停文件名提示（2026-10-04 错误修复④）----
    // 旧实现把气泡画在 IconItem 的图标格内（居中盖住图标、带淡入淡出、贴板边还要渐隐遮罩，
    // 且格内没有"图标上方"的空间）。新实现=板层单例浮层（TipLayer/HoverTip）：
    //   · 悬停目标图标时**立即**显示（无出现/消失动画）；
    //   · 位置在目标图标正上方（含 hover 上跳位移），不遮挡目标图标；贴板顶时钳进板内；
    //   · 完整名字不裁剪（超长在板宽内换行）；应用程序不显示后缀（BoardItem.HoverName）；
    //   · 底色完全不透明（主题色反相：暗=白底深字 / 亮=深底浅字）。
    // hoverReveal 下只有"已显形"的图标才出提示（错误修复③：悬停文件名随图标一起被隐藏）。

    IconItem? _tipIcon;
    string? _tipText;
    double _tipMeasuredW, _tipMeasuredH;
    double _tipMeasuredMaxW = double.NaN; // 上次测量时的可用宽度（NaN=需重测）

    void UpdateHoverTip(Point posInGrid)
    {
        if (!SettingsStore.Instance.GetBool("display.hoverLabels", true)) { HideHoverTip(); return; }
        if (IconPanel == null) { HideHoverTip(); return; }

        var icon = FindAncestor<IconItem>(GridHost.InputHitTest(posInGrid) as DependencyObject);
        var item = icon?.Item;
        if (icon == null || item == null || !icon.IsRevealed) { HideHoverTip(); return; }

        if (!ReferenceEquals(icon, _tipIcon) || !string.Equals(_tipText, item.HoverName, StringComparison.Ordinal))
        {
            _tipIcon = icon;
            _tipText = item.HoverName;
            HoverTipText.Text = _tipText;
            _tipMeasuredMaxW = double.NaN; // 内容变了：强制重测
        }
        double tipMaxW = Math.Max(80, Root.ActualWidth - 16); // 板宽内换行，不越出板
        // 2026-10-05 清单06任务2（根因）：WPF 里 Collapsed 元素 Measure 恒得 DesiredSize=0×0
        // （实测 collapsed 0×0 / hidden 154.8×21.2 / visible 同）。旧实现先量后显形，首帧按 0 尺寸
        // 定位——left 落在图标水平中心、top 紧贴图标顶，观感就是"文件名跑到图标右上角"；要等指针
        // 再动一次（跳起动画期间的重命中）重新量到真尺寸才"复原到上方"。此处先转 Hidden
        // （不渲染但正常参与量算）量出真实尺寸并定位，再显形——同一帧内完成，不会看到中间态。
        // 性能（2026-10-05）：测量只在"文本或可用宽度变化"时做——MouseMove 每帧都量一次是
        // 典型的强制同步布局热点，而位置移动并不改变 DesiredSize。
        if (_tipMeasuredMaxW != tipMaxW)
        {
            HoverTip.MaxWidth = tipMaxW;
            if (HoverTip.Visibility != Visibility.Visible)
                HoverTip.Visibility = Visibility.Hidden;
            HoverTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _tipMeasuredW = HoverTip.DesiredSize.Width;
            _tipMeasuredH = HoverTip.DesiredSize.Height;
            _tipMeasuredMaxW = tipMaxW;
        }
        double tw = _tipMeasuredW, th = _tipMeasuredH;

        var origin = icon.TranslatePoint(new Point(0, 0), Root); // 已折算渲染变换（含 hover 上跳）
        if (double.IsNaN(origin.X) || double.IsNaN(origin.Y)) { HideHoverTip(); return; }
        double top = origin.Y - AnimTuning.IconLiftPx - th - 4; // 按"跳起后"的位置摆放，留 4px 间隙
        double left = origin.X + icon.ActualWidth / 2 - tw / 2;
        top = Math.Max(4, top);
        left = Math.Clamp(left, 4, Math.Max(4, Root.ActualWidth - tw - 4));
        Canvas.SetLeft(HoverTip, left);
        Canvas.SetTop(HoverTip, top);
        if (HoverTip.Visibility != Visibility.Visible)
            HoverTip.Visibility = Visibility.Visible; // 立即显示：无出现动画
    }

    void HideHoverTip()
    {
        _tipIcon = null;
        _tipText = null;
        _tipMeasuredMaxW = double.NaN; // 下次显示重新测量（避免用到已失效的尺寸缓存）
        if (HoverTip.Visibility != Visibility.Collapsed)
            HoverTip.Visibility = Visibility.Collapsed; // 立即消失：无消失动画
    }

    /// <summary>提示配色（完全不透明，主题反相保证可读）。</summary>
    void ApplyHoverTipTheme(bool dark)
    {
        HoverTip.Background = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x30, 0x30, 0x30));
        HoverTipText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Color.FromRgb(0xF2, 0xF2, 0xF2));
    }

    // ---- 右键菜单（任务 4）----

    void OnRootRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!IsVisible) return;
        var icon = HitIcon(e.OriginalSource as DependencyObject);
        var pos = e.GetPosition(Root);
        if (icon?.Item != null)
            ShowItemMenu(icon.Item, pos, CatTabBar.RemoveActionFor(icon.Item)); // 分类视图追加"从此分类移除"（04 §4.3）
        else if (!IsInChrome(e.OriginalSource as DependencyObject))
            ShowBlankMenu(pos);
        e.Handled = true;
    }

    /// <summary>命中右上角/右下角常驻控件时不弹菜单。</summary>
    bool IsInChrome(DependencyObject? source)
        => FindAncestor<Grid>(source) is { Name: "GearBtn" or "CloseBtn" or "TrashBtn" };

    static T? FindAncestor<T>(DependencyObject? source) where T : class
    {
        while (source != null)
        {
            if (source is T hit) return hit;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    void ShowBlankMenu(Point pos)
    {
        var (key, asc) = _model.CurrentSort;
        var sortItems = new List<PopupMenuItem>
        {
            new() { Header = I18n.Tr("sort.name"), Checked = key == BoardModel.SortKey.Name, OnClick = () => SetSort(BoardModel.SortKey.Name, asc) },
            new() { Header = I18n.Tr("sort.size"), Checked = key == BoardModel.SortKey.Size, OnClick = () => SetSort(BoardModel.SortKey.Size, asc) },
            new() { Header = I18n.Tr("sort.type"), Checked = key == BoardModel.SortKey.Type, OnClick = () => SetSort(BoardModel.SortKey.Type, asc) },
            new() { Header = I18n.Tr("sort.mtime"), Checked = key == BoardModel.SortKey.Mtime, OnClick = () => SetSort(BoardModel.SortKey.Mtime, asc) },
            PopupMenuItem.Separator(),
            new() { Header = I18n.Tr("sort.asc"), Checked = asc, OnClick = () => SetSort(key, true) },
            new() { Header = I18n.Tr("sort.desc"), Checked = !asc, OnClick = () => SetSort(key, false) },
        };
        var menu = new List<PopupMenuItem>
        {
            new() { Header = I18n.Tr("board.sort"), SubItems = sortItems },
            PopupMenuItem.Separator(),
            new()
            {
                Header = I18n.Tr("sort.auto"),
                OnClick = () =>
                {
                    _iconOrder.Clear(); // 切回自动布局【补充决策】
                    SaveIconOrder();
                    ResetCellsToUnassigned(); // 同 SetSort：旧格位记忆会让重排原样弹回（2026-10-01）
                    Relayout();
                },
            },
            new() { Header = I18n.Tr("board.refresh"), OnClick = () => _ = RescanRebuildAsync() }, // 清单三·任务12：重建式刷新
            PopupMenuItem.Separator(),
            new()
            {
                Header = I18n.Tr("board.new"),
                SubItems = new List<PopupMenuItem>
                {
                    new() { Header = I18n.Tr("new.folder"), OnClick = () => FileOps.CreateFolder(DesktopScanner.UserDesktopDir) },
                    new() { Header = I18n.Tr("new.textfile"), OnClick = () => FileOps.CreateTextFile(DesktopScanner.UserDesktopDir) },
                    // 2026-10-05 清单06任务4：改为**系统选择窗口**选目标（原先弹小窗手填路径，已废弃）。
                    // 系统"打开"对话框选文件（筛选=所有文件、可逐层进入文件夹）；"选择文件夹"对话框选文件夹——
                    // 单个 Windows 对话框无法同时"可选中文件"与"可选中文件夹"（FOS_PICKFOLDERS 二选一），
                    // 故拆成两行，覆盖"所有文件与文件夹"。
                    new() { Header = I18n.Tr("new.shortcut"), OnClick = () => CreateShortcutByPick(pickFolder: false) },
                    new() { Header = I18n.Tr("new.shortcut.folder"), OnClick = () => CreateShortcutByPick(pickFolder: true) },
                },
            },
        };
        GlassPopup.Show(Root, pos, menu);
    }

    void SetSort(BoardModel.SortKey key, bool asc)
    {
        _iconOrder.Clear(); // 改排序 = 回自动布局（iconOrder 与排序互斥）【补充决策】
        SaveIconOrder();
        _model.SetSort(key, asc);
        ResetCellsToUnassigned(); // 清旧格位记忆：否则 Relayout 按旧位置快照回写，排序等于没发生（2026-10-01）
        Relayout();
    }

    /// <summary>把全部项格位打回未分配（Row/Col=-1）：Relayout 的 VisualSequence 对未分配项
    /// 按模型集合顺序（=当前过滤+排序结果）缀后，从而让"清 iconOrder"真正落到新排序的布局。</summary>
    void ResetCellsToUnassigned()
    {
        foreach (var it in _model.Items)
        {
            it.Row = -1;
            it.Col = -1;
        }
    }

    /// <param name="extra">分类页视图追加的动作项（"从此分类移除"，04 §3.4；null=不追加），
    /// 插在"移除"之后。</param>
    void ShowItemMenu(BoardItem item, Point pos, PopupMenuItem? extra = null)
    {
        var menu = new List<PopupMenuItem>
        {
            new() { Header = I18n.Tr("menu.open"), OnClick = () => OpenItem(item, IconOf(item)) },
            new() { Header = I18n.Tr("menu.location"), OnClick = () => { SuppressOutsideClose(); FileOps.RevealInExplorer(item.Path); } },
            new() { Header = I18n.Tr("menu.pin"), OnClick = () => { SuppressOutsideClose(); FileOps.PinToStart(item.Path); } },
            PopupMenuItem.Separator(),
            new() { Header = I18n.Tr("menu.copy"), OnClick = () => FileOps.CopyToClipboard(new[] { item.Path }) },
            new() { Header = I18n.Tr("menu.rename"), OnClick = () => BeginRename(item) },
            new() { Header = I18n.Tr("menu.remove"), OnClick = () => RemoveFromBoard(item) }, // 2026-08-29：不再显示（非删除）
            // 2026-10-05 清单06任务4：新增"删除"=取消显示+删源文件（回收站，00 §10.1 语义），
            // 与"移除"（仅取消显示）并列摆放，避免误点；实现复用垃圾桶删除链路（缩小淡出 + 回收站 + 摘除）
            new() { Header = I18n.Tr("menu.delete"), OnClick = () => DeleteViaTrash(item) },
            PopupMenuItem.Separator(),
            new() { Header = I18n.Tr("menu.properties"), OnClick = () => { SuppressOutsideClose(); FileOps.ShowProperties(item.Path); } },
        };
        if (extra != null)
        {
            int idx = menu.FindIndex(m => !m.IsSeparator && m.Header == I18n.Tr("menu.remove"));
            menu.Insert(idx >= 0 ? idx + 1 : menu.Count - 1, extra);
        }
        GlassPopup.Show(Root, pos, menu);
    }

    /// <summary>
    /// 新建快捷方式的目标选择（2026-10-05 清单06任务4）：从"手填路径输入框"改为**系统选择窗口**。
    /// pickFolder=false → 系统"打开"对话框（筛选"所有文件"，文件夹在列表内可逐层进入，
    /// 校验关闭以便直接填入文件夹路径时同样成立）；true → 系统"选择文件夹"对话框。
    /// 两者都拿到绝对路径后再建 .lnk（落桌面 → watcher 刷新可见【4.2】）。
    /// </summary>
    async void CreateShortcutByPick(bool pickFolder)
    {
        SuppressOutsideClose(); // 系统对话框夺焦，不触发"板外收起"
        string? target = pickFolder ? PickFolder() : PickFile(); // 对话框必须在 UI 线程
        if (string.IsNullOrEmpty(target)) return;
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            SuppressOutsideClose();
            MessageBox.Show(this, I18n.Tr("msg.shortcut.invalid"), "Cship",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // 07 §4：建 .lnk 走 WScript.Shell COM + 写盘，挪到后台线程（完成后 watcher 自动刷新可见）
        string? created;
        try
        {
            created = await System.Threading.Tasks.Task.Run(() => FileOps.CreateShortcut(target));
        }
        catch (Exception ex)
        {
            Logger.Warn($"创建快捷方式失败（后台）{target}：{ex.Message}");
            created = null;
        }
        if (created == null)
        {
            SuppressOutsideClose();
            MessageBox.Show(this, I18n.Tr("msg.shortcut.failed"), "Cship",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>系统"打开"对话框：筛选=所有文件（不限定类型），文件夹可逐层进入。</summary>
    static string? PickFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = I18n.Tr("new.shortcut.title"),
            Filter = I18n.Tr("new.shortcut.filter"),
            CheckFileExists = false,
            ValidateNames = false,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>系统"选择文件夹"对话框（Vista+ 现代选择器）。</summary>
    static string? PickFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = I18n.Tr("new.shortcut.folder"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    void BeginRename(BoardItem item)
    {
        var icon = IconOf(item);
        if (icon == null) return;
        _renaming = true;
        icon.StartRename((ok, newName) =>
        {
            _renaming = false;
            if (!ok) return;
            newName = newName.Trim();
            if (newName.Length == 0 || newName == item.Name) return;
            if (!FileOps.IsValidName(newName))
            {
                SuppressOutsideClose();
                MessageBox.Show(this, I18n.Tr("msg.rename.invalid"), "Cship", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (item.IsVirtual)
            {
                // 仅收纳板的虚拟项：改名只改显示名（不动源文件，state.virtualItems 持久化）
                if (_iconOrder.Remove(item.Name))
                {
                    _iconOrder[newName] = (item.Row, item.Col);
                }
                item.Name = newName;
                var idx = _virtualItems.FindIndex(v => string.Equals(v.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    _virtualItems[idx] = (item.Path, newName);
                    SaveVirtualItems();
                }
                SaveIconOrder();
                return;
            }
            if (!FileOps.Rename(item.Path, newName))
            {
                SuppressOutsideClose();
                MessageBox.Show(this, I18n.Tr("msg.rename.exists"), "Cship", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_iconOrder.Remove(item.Name)) // 换位记录随改名迁移
            {
                _iconOrder[newName] = (item.Row, item.Col);
                SaveIconOrder();
            }
            // 桌面文件已改名，watcher 约 0.5s 内刷新；这里立即同步实例避免闪跳
            item.Name = newName;
            item.Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(item.Path) ?? "", newName);
        });
    }

    // ---- 自定义背景图（1.1 L3）----

    /// <summary>
    /// 自定义背景图解码缓存（步骤 06 §5：修"每次开板重解码"）。
    /// 键 =（绝对路径 + crop + 不透明度），并用文件最后写入时间做有效性校验——
    /// 设置变更（换图/换裁剪/改不透明度）或文件被替换才重解，其余情况直接命中缓存。
    /// 缓存值均已 Freeze，可跨线程/跨次开板复用。
    /// </summary>
    static readonly Dictionary<string, (DateTime Written, ImageSource Src)> BgDecodeCache = new();

    void LoadCustomBg()
    {
        try
        {
            var element = SettingsStore.Instance.GetRawValue("personal.boardBg");
            string? image = null;
            bool hasCrop = false;
            double cx = 0, cy = 0, cw = 0, ch = 0;
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String)
                    image = img.GetString();
                if (element.TryGetProperty("crop", out var crop) && crop.ValueKind == JsonValueKind.Object)
                    hasCrop = TryNum(crop, "x", out cx) && TryNum(crop, "y", out cy)
                              && TryNum(crop, "w", out cw) && TryNum(crop, "h", out ch);
            }
            double opacity = Math.Clamp(SettingsStore.Instance.GetDouble("personal.boardBgOpacity", 100), 0, 100);
            if (string.IsNullOrWhiteSpace(image))
            {
                CustomBgHost.Visibility = Visibility.Collapsed;
                BgShade.Visibility = Visibility.Collapsed;
                return;
            }
            if (!Path.IsPathRooted(image))
                image = Path.Combine(Paths.AssetsDir(), image);
            if (!File.Exists(image))
            {
                CustomBgHost.Visibility = Visibility.Collapsed;
                BgShade.Visibility = Visibility.Collapsed;
                return;
            }
            DateTime written = File.GetLastWriteTimeUtc(image);
            string key = $"{image}|{(hasCrop ? $"{cx:0.##},{cy:0.##},{cw:0.##},{ch:0.##}" : "-")}|{opacity:0.##}";
            ImageSource? source = null;
            if (BgDecodeCache.TryGetValue(key, out var hit) && hit.Written == written)
            {
                source = hit.Src;
                Logger.Info($"自定义背景命中解码缓存（不重复解码）：{Path.GetFileName(image)}");
            }
            else
            {
                var (bmp, decodeScale) = DecodeBackground(image);
                ImageSource built = bmp;
                if (hasCrop && cw > 0 && ch > 0)
                {
                    // crop 矩形是原图像素坐标；DecodeBackground 按屏幕宽封顶缩了解码尺寸时同步换算，
                    // 显示结果与"全尺寸解码后裁剪"一致（内存优化：背景图是潜在最大单体，4K 图全解码 ≈33MB）
                    var cropped = new CroppedBitmap(bmp, ScaleCropRect(bmp, decodeScale, cx, cy, cw, ch));
                    cropped.Freeze();
                    built = cropped;
                }
                source = built;
                BgDecodeCache[key] = (written, built);
                Logger.Info($"自定义背景解码完成并缓存：{Path.GetFileName(image)}（缓存 {BgDecodeCache.Count} 项）");
            }
            CustomBgHost.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill }; // 圆角 Border 画刷：绘制即被圆角裁切（错误修复⑤）
            // 背景图不透明度（清单02任务9）：0~100%，独立于板底 personal.boardOpacity
            CustomBgHost.Opacity = opacity / 100.0;
            CustomBgHost.Visibility = Visibility.Visible;
            BgShade.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Logger.Warn($"自定义背景加载失败：{ex.Message}");
            CustomBgHost.Visibility = Visibility.Collapsed;
            BgShade.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>背景图解码（内存优化 · 2026-09-27）：解码宽封顶=所选屏物理像素宽
    /// （背景显示永远不会超过一块屏，超过部分是纯浪费）。用 BitmapDecoder+DelayCreation
    /// 只读文件头拿原图尺寸（不整图解码），算出缩放因子供 crop 矩形换算；小图原样解码。</summary>
    (BitmapImage Bmp, double Scale) DecodeBackground(string path)
    {
        double origW = 0;
        try
        {
            var probe = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            if (probe.Frames.Count > 0)
                origW = probe.Frames[0].PixelWidth;
        }
        catch { /* 探测失败按未知尺寸处理 */ }
        int capW = Math.Max(800, Screens.SelectedScreen().Bounds.Width);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (origW > capW)
            bmp.DecodePixelWidth = capW;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        double scale = origW > 0 ? bmp.PixelWidth / origW : 1.0;
        return (bmp, scale);
    }

    /// <summary>把原图像素坐标的 crop 矩形按解码缩放因子换算到解码位图坐标（夹回边界）。</summary>
    static Int32Rect ScaleCropRect(BitmapImage bmp, double scale, double cx, double cy, double cw, double ch)
    {
        int x = Math.Clamp((int)Math.Round(cx * scale), 0, Math.Max(0, bmp.PixelWidth - 1));
        int y = Math.Clamp((int)Math.Round(cy * scale), 0, Math.Max(0, bmp.PixelHeight - 1));
        int w = Math.Clamp((int)Math.Round(cw * scale), 1, bmp.PixelWidth - x);
        int h = Math.Clamp((int)Math.Round(ch * scale), 1, bmp.PixelHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    static bool TryNum(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var v)
               && v.ValueKind == JsonValueKind.Number
               && v.TryGetDouble(out value);
    }

    // ---- 主题（00 §9）----

    /// <summary>语言热切换后重刷静态文案（步骤 05）。</summary>
    void RefreshTexts()
    {
        ToolTipService.SetToolTip(CloseBtn, I18n.Tr("board.close"));
        ToolTipService.SetToolTip(GearBtn, I18n.Tr("board.settings"));
        if (EmptyHint.Text.Length > 0)
            EmptyHint.Text = IsFiltered ? I18n.Tr("cat.empty") : I18n.Tr("board.empty");
    }

    /// <summary>全局语言切换过渡（2026-10-03）：板可见时先截当前观感快照盖顶 → 重刷文案 →
    /// 快照淡出（与主题切换 ThemeFade 同机制）；板不可见时直接刷新。</summary>
    void OnLanguageChangedWithFade()
    {
        if (IsVisible && !_boardAnimRunning && Root.ActualWidth > 1)
            ThemeFade.Transition(this, RefreshTexts);
        else
            RefreshTexts();
    }

    /// <summary>
    /// 配置导入热应用（步骤 05）：重载 iconOrder / 分类 / 自定义背景并重排当前视图。
    /// settings 键已由 ConfigPort.Apply 经广播各自生效，此处只收 state 与整段重载。
    /// </summary>
    /// <summary>
    /// 导入配置热应用（步骤 05 §4；2026-10-05 补齐移除名单与板内快捷方式）：重载 iconOrder /
    /// 移除名单 / 快捷方式 / 分类 / 背景。前三者中，iconOrder 与移除名单只影响对账（Relayout 即可）；
    /// 快捷方式属于**源集合**内容，必须重扫才能纳入，故追加一次 RescanAsync——它不复位移除名单
    /// （复位是"刷新"的语义，见 RescanRebuildAsync），导入进来的名单得以保留。
    /// </summary>
    public void ApplyImportedConfig()
    {
        _iconOrder = StateStore.Instance.GetIconOrder();
        _virtualItems = StateStore.Instance.GetVirtualItems();
        _model.SetRemoved(StateStore.Instance.GetRemovedItems());
        LoadCustomBg();
        CatTabBar.ReloadCategories();
        Relayout();
        _ = RescanAsync();
        Logger.Info("收纳板已重载导入配置（iconOrder/移除名单/快捷方式/分类/背景）");
    }

    void ApplyTheme()
    {
        bool dark = ThemeResolver.IsDark();
        // 步骤 06：材质档接管板底绘制来源（模拟通道；三档切换即时生效）
        Material.Material = SettingsStore.Instance.GetString("personal.material", MaterialEngine.None);
        Material.SetTheme(dark);
        Material.SetBoardOpacity(SettingsStore.Instance.GetDouble("personal.boardOpacity", 91)); // 板底不透明度（清单02任务7）
        // L3 之上的 10% 主题色遮罩（§5）：保证自定义背景图上的文字可读
        BgShade.Background = SettingsPalette.Freeze(dark ? "#1AFFFFFF" : "#1A1B1B1B");
        var chrome = new SolidColorBrush(dark ? Color.FromRgb(0xB8, 0xB8, 0xB8) : Color.FromRgb(0x5F, 0x5F, 0x5F));
        TrashBody.Fill = chrome; // 垃圾桶盖/身同色（清单二·任务6：图标拆两块后主题同步两块）
        TrashLid.Fill = chrome;
        GearIcon.Fill = chrome;

        // 标题栏带已于批次十二整体移除（原文任务5）：顶部 34px 仅作按钮容纳区与拖动热区

        // 分类页（04 · 2026-09-25 改版）：顶部按钮行按钮配色跟随主题与材质
        CatTabBar.ApplyTheme(dark);
        ApplyHoverTipTheme(dark); // 悬停文件名提示：完全不透明底随主题反相（错误修复④）
        ApplyDirectionCorners();
    }

    /// <summary>
    /// 主题切换带快照过渡（步骤 03 ThemeFade）：板可见且无开合动画在播时，
    /// 截当前观感 → ApplyTheme 换装 → 快照 300ms 淡出（交叉淡化、无闪白）；
    /// 板隐藏/动画中/首载未渲染时退化为直接换装。
    /// </summary>
    void ApplyThemeWithFade()
    {
        if (IsVisible && !_boardAnimRunning && Root.ActualWidth > 1)
            ThemeFade.Transition(this, ApplyTheme);
        else
            ApplyTheme();
    }

    // ---- 设置实时联动（3.1）----

    void OnSettingChanged(SettingChangedArgs args)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSettingChanged(args));
            return;
        }
        switch (args.Key)
        {
            case "advanced.popupDirection":
                LoadBoardSize();
                RebuildResizeThumbs();
                ClampSizeToWorkarea();
                Reposition();
                ApplyDirectionCorners(); // 方向变了：锚边圆角随之改（此前只在主题切换时应用，切方向后圆角不更新）
                break;
            case "display.iconSize":
            case "display.rowSpacing":
            case "display.colSpacing":
            case "display.showLabels":
            case "display.labelLines": // 文件名行数（一行/两行/3 行文字，2026-10-04）
                // 07 §4 热点 4：三个滑块拖动时逐帧广播（每个 tick 全板 RefreshContainers + Relayout
                // + 按新尺寸重解码图标）→ 16ms 合并（≈60Hz 上限），拖动跟手且不重复全量刷新。
                ScheduleLayoutRefresh();
                break;
            case "display.iconOpacity":
            case "display.iconStyle":
            case "advanced.iconMask":
            case "display.hoverLabels":
                RefreshContainers();
                HideHoverTip(); // 悬停文件名开关：关掉时立即收起提示（错误修复④）
                break;
            case "advanced.showApps":
                _model.ShowApps = SettingsStore.Instance.GetBool("advanced.showApps", true);
                Relayout();
                break;
            case "advanced.showFiles":
                _model.ShowFiles = SettingsStore.Instance.GetBool("advanced.showFiles", true);
                Relayout();
                break;
            case "advanced.showFolders":
                _model.ShowFolders = SettingsStore.Instance.GetBool("advanced.showFolders", true);
                Relayout();
                break;
            case "advanced.runningIndicator":
                RefreshContainers();
                PollRunning();
                break;
            case "advanced.categoryDisabled":
                ApplyCategoryAvailability(); // 顶栏按钮行整体隐藏（04 §6.1，2026-09-25 改版）
                break;
            case "advanced.catShowApps":
            case "advanced.catShowFiles":
            case "advanced.catShowFolders":
                CatTabBar.RefreshTabs(); // 子开关：显隐对应内置分类按钮；重开=找回被"移除"的内置项（04 §6.2）
                break;
            case "theme.mode":
                ApplyThemeWithFade(); // 快照交叉淡化（步骤 03 ThemeFade；06 步 ThemeEngine 接管后换装点不变）
                RefreshContainers();
                break;
            case "general.font":
                FontFamily = new FontFamily(SettingsStore.Instance.GetString("general.font", "Microsoft YaHei"));
                break;
            case "advanced.dockPriority":
                ApplyBoardPriority(); // 收纳板优先级跟随悬浮窗（2026-08-29）
                break;
            case "personal.boardBg":
                LoadCustomBg(); // 自定义背景选择/清除即时生效（步骤 05）
                break;
            case "personal.boardOpacity": // 板底不透明度（清单02任务7，实时）
                Material.SetBoardOpacity(SettingsStore.Instance.GetDouble("personal.boardOpacity", 91));
                break;
            case "personal.material": // 三档材质（步骤 06 §1.6：设置页点击即时生效，无需重开板）
                Material.Material = SettingsStore.Instance.GetString("personal.material", MaterialEngine.None);
                break;
            case "personal.boardBgOpacity": // 背景图不透明度（清单02任务9，实时）
                CustomBgHost.Opacity = Math.Clamp(SettingsStore.Instance.GetDouble("personal.boardBgOpacity", 100), 0, 100) / 100.0;
                break;
            case "display.screen":
                ClampSizeToWorkarea();
                Reposition();
                break;
        }
    }

    void RefreshContainers()
    {
        if (IconPanel == null) return;
        foreach (var child in IconPanel.Children)
            if (child is ContentPresenter cp)
                FindDescendant<IconItem>(cp)?.ApplySettings();
    }

    // ---- 布局类设置变更的 16ms 合并（07 §4 热点 4）----
    // display.iconSize/rowSpacing/colSpacing 由滑块拖动逐帧写入 settings，每个 tick 都会走到
    // OnSettingChanged。原实现逐帧全板 RefreshContainers + Relayout（含按新尺寸重新解码图标），
    // 拖动一次可打出上百次全量刷新。改为 16ms 窗口合并成一次——观感仍是逐帧跟手，
    // 但同一帧内的多次写入只做一次全量工作。

    DispatcherTimer? _layoutMergeTimer;

    void ScheduleLayoutRefresh()
    {
        if (_layoutMergeTimer == null)
        {
            _layoutMergeTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            _layoutMergeTimer.Tick += (_, _) =>
            {
                _layoutMergeTimer!.Stop();
                RefreshContainers();
                Relayout();
                HideHoverTip(); // 格位变化：提示位置失效，收起等下次悬停（错误修复④）
            };
        }
        _layoutMergeTimer.Stop(); // 重置窗口：连续写入期间只在"最后一次写入后 16ms"做一次
        _layoutMergeTimer.Start();
    }
}
