using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Cship.Core;
using Cship.Ui.Animations;
using Cship.Ui.Components;
using WinForms = System.Windows.Forms;

namespace Cship.Ui;

/// <summary>
/// 悬浮窗 z 序优先级三档（00 §6/§7·原文 L60；settings 键 advanced.dockPriority）。
/// </summary>
public enum DockZOrder
{
    /// <summary>始终置底：Topmost=False + 拦截 WM_WINDOWPOSCHANGING 强制 HWND_BOTTOM。</summary>
    Low,
    /// <summary>默认置顶，所选屏真全屏前台时隐藏/退层——真检测 07 步接通，当前行为等同 High。</summary>
    Medium,
    /// <summary>始终置顶（默认档）。</summary>
    High,
}

/// <summary>
/// 悬浮窗（步骤 4）：三层亚克力方板常驻窗口。
/// 悬停拉开/移出倒放（4.3）、单击消散+占位复现（4.4，节奏为 2026-08-28 用户修订）、
/// 手动拖动+工作区夹边（4.5）、首次运行定位与位置持久化（4.6/4.7）、
/// z 序优先级三档 ApplyPriority（4.8，01b 补丁落地）。
/// </summary>
public partial class FloatingDock : GlassWindow
{
    enum DockState { Idle, Pressing, Dragging, Dissolving, Hidden }

    // 悬浮窗动画参数唯一来源=AnimTuning（步骤 03）。消散节奏为 2026-08-28 用户两次修订
    // （STEP_LOG 决策13：取代 00 §7 时长表的 120ms）——分离与淡出同 duration，动画结束
    // 瞬间透明度恰好到 100%（两动画同时到位，无静止渐隐尾巴）
    const double ReviveDelayMs = 600;    // 消散后占位复现的延时（02 步替换为打开收纳板）
    const double FirstRunTopGap = 16.0;  // 首次运行：可见板顶距工作区顶部【L8】
    const double ClickTravelLimitPx = 4.0;   // 位移阈值（物理 px）
    const double ClickMaxMs = 300;           // 按住时长阈值

    DockState _state = DockState.Idle;
    Point _pressPhysical;                    // 按下时的物理像素光标位置
    long _pressTimestamp;                    // Stopwatch 时间戳
    readonly DispatcherTimer _posSaveTimer;  // 拖动结束 500ms 防抖（4.7）
    DispatcherTimer? _reviveTimer;           // 消散后占位复现（4.4）

    double _dockSize;
    bool _locked;
    DockZOrder _priority = DockZOrder.High;
    bool _bottomHookInstalled; // low 档 WM_WINDOWPOSCHANGING 钩子在挂状态
    bool _userHidden;          // 右键菜单"隐藏"（区别于消散后的临时 Hidden，托盘"显示"只对它生效）
    bool _loading;             // 首载加载态（清单二·任务13）：两侧圆点循环，悬停禁用，就绪后自动开板

    public FloatingDock()
    {
        InitializeComponent();
        _dockSize = Math.Clamp(SettingsStore.Instance.GetDouble("display.dockSize", 58.7), 40, 96);
        _locked = SettingsStore.Instance.GetBool("advanced.dockLocked", false);
        Plate.PlateSize = _dockSize;
        Plate.SetTheme(ThemeResolver.IsDark());
        Width = _dockSize + SquarePlate.Chrome; // 板边长 + 四周预留（悬停/消散行程）
        Height = _dockSize + SquarePlate.Chrome;
        Cursor = _locked ? Cursors.Arrow : Cursors.Hand;

        // 悬停错开动画（批次十二修订 B）：完整播放 + 播放中忽略二次触发
        _hoverCycle.Configure(
            done => Anim.Run(Plate, SquarePlate.OffsetProperty, null, SquarePlate.HoverOffset, AnimTuning.DockHover, EaseStyle.EaseInOutQuad, onDone: done),
            done => Anim.Run(Plate, SquarePlate.OffsetProperty, null, SquarePlate.RestOffset, AnimTuning.DockHover, EaseStyle.EaseInOutQuad, onDone: done),
            () => IsMouseOver);

        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonDown += OnPress;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnRelease;
        MouseRightButtonUp += OnDockRightClick;
        LostMouseCapture += (_, _) =>
        {
            if (_state is DockState.Pressing or DockState.Dragging)
                _state = DockState.Idle;
        };

        _posSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _posSaveTimer.Tick += (_, _) =>
        {
            _posSaveTimer.Stop();
            SavePositionNow();
        };

        SettingsStore.Instance.SettingsChanged += OnSettingChanged;
        ThemeAuto.DarkChanged += OnAutoDarkChanged; // auto/system 折算翻转即时跟随（06 步补齐）
        // 静态事件成对退订（批次九教训；07 步审计补齐 SettingsChanged 的对称性）：
        // 悬浮窗是进程级单例、正常不 Close，但"成对"是硬约束——窗口一旦关闭必须摘干净。
        Closed += (_, _) =>
        {
            ThemeAuto.DarkChanged -= OnAutoDarkChanged;
            SettingsStore.Instance.SettingsChanged -= OnSettingChanged;
        };
        ApplyDockCustom(); // 悬浮窗自定义三层外观（步骤 05 §3.4，含底面材质预设）
        ApplyDockTheme();  // 悬浮窗跟随主题（2026-10-03 开关控制，默认跟随）
    }

    /// <summary>auto 时段 / system 偏好翻转：悬浮窗跟随主题时同步三板配色（不动消散/悬停动画）。</summary>
    void OnAutoDarkChanged(bool dark)
    {
        if (Dispatcher.CheckAccess()) ApplyDockTheme();
        else Dispatcher.BeginInvoke(new Action(ApplyDockTheme));
    }

    /// <summary>读取 personal.dockCustom.images 的字符串数组（缺项补空）。</summary>
    static string[] ReadArray(string key)
    {
        var result = new[] { "", "", "" };
        var el = SettingsStore.Instance.GetRawValue(key);
        if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            int i = 0;
            foreach (var v in el.EnumerateArray())
                if (i < 3 && v.ValueKind == System.Text.Json.JsonValueKind.String)
                    result[i++] = v.GetString() ?? "";
        }
        return result;
    }

    /// <summary>三层图片各自不透明度（personal.dockCustom.opacity 数组，0~100，缺项/非法补 100，清单02任务8）。</summary>
    static double[] ReadLayerOpacities()
    {
        var result = new[] { 100.0, 100.0, 100.0 };
        var el = SettingsStore.Instance.GetRawValue("personal.dockCustom.opacity");
        if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            int i = 0;
            foreach (var v in el.EnumerateArray())
                if (i < 3 && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetDouble(out var d))
                    result[i++] = Math.Clamp(d, 0, 100);
        }
        return result;
    }

    /// <summary>
    /// 悬浮窗自定义（步骤 05 §3.4；清单02任务8 重做）：preset=底面材质观感；custom=三层图片
    /// （相对 config\assets 的副本路径，cover 填充）。
    /// **2026-10-06 用户要求**：personal.dockCustom.opacity 这组"各层不透明度"对**两种模式都生效**
    /// ——自定义模式乘在层图 ImageBrush 上，预设模式由 SquarePlate 折进材质配方（原始预设不被改写）。
    ///
    /// **层序对调（2026-10-06 用户口径）**：存储与设置页里的三个值按**界面层序** [上,中,下] 排，
    /// 而板子的绘制序是 [front(左上), mid(中), back(右下)]。用户实测口径：**视觉上"右下角那一层"才是上层**
    /// ——故交给板子前把上/下对调（`2 - uiSlot`）。层图与层不透明度**共用同一份对调**，
    /// 否则会出现"图落在某一层、不透明度作用在另一层"的错配。
    /// </summary>
    void ApplyDockCustom()
    {
        var s = SettingsStore.Instance;
        string mode = s.GetString("personal.dockCustom.mode", "preset");
        Plate.Material = s.GetString("personal.dockCustom.material", MaterialEngine.Blur); // 三种底面材质预设（2026-10-03）
        var opacities = ReadLayerOpacities();                     // 界面层序 [上,中,下]
        var plateOpacity = new[]                                // 板序 [front,mid,back]
        {
            opacities[2], opacities[1], opacities[0],
        };
        var layers = new Brush?[3];                              // 板序 [front,mid,back]
        if (string.Equals(mode, "custom", StringComparison.OrdinalIgnoreCase))
        {
            var images = ReadArray("personal.dockCustom.images"); // 界面层序 [上,中,下]
            for (int i = 0; i < 3; i++)
            {
                if (images[i].Length == 0) continue;
                // 层图经 IconBitmapCache 共享 + 解码宽封顶 256（层显示尺寸 ≤96px 板边长，
                // 原图动辄数兆像素全解码纯属浪费；内存优化 2026-09-27）
                var bmp = IconBitmapCache.GetOrLoad(System.IO.Path.Combine(Paths.AssetsDir(), images[i]), 256);
                if (bmp != null)
                {
                    layers[2 - i] = new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center,
                        Opacity = opacities[i] / 100.0, // 每层独立不透明度（清单02任务8）
                    };
                }
            }
        }
        Plate.ApplyCustomLayers(layers);
        Plate.SetLayerOpacities(plateOpacity); // 预设模式的层不透明度（无层图的层同样按此折算）
    }

    // ---- 定位 ----

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var saved = StateStore.Instance.GetPoint2("dock.pos");
        if (saved is { } pos)
        {
            MoveToPhysical(pos.X, pos.Y);
            ClampIntoWorkingArea(); // 分辨率/屏幕变化后夹回合法区（4.7）
        }
        else
        {
            PlaceFirstRun();
        }
        StateStore.Instance.SetBool("firstRunDone", true);
    }

    /// <summary>复位到默认位置（托盘"复位"，3.3）。</summary>
    public void ResetToDefaultPosition() => PlaceFirstRun();

    /// <summary>
    /// 显示设置变化（07 §3 热插拔/分辨率）：所选屏被拔掉 → 回主屏顶部居中；否则把可见板
    /// 夹回所选屏工作区（分辨率变小后原位置可能出屏）。由 App 的 DisplaySettingsChanged 调用。
    /// </summary>
    public void OnDisplayChanged()
    {
        var id = SettingsStore.Instance.GetString("display.screen", "auto");
        bool specifiedGone = !string.Equals(id, "auto", StringComparison.OrdinalIgnoreCase)
                             && !Screens.Exists(id);
        if (specifiedGone)
        {
            PlaceFirstRun(); // 指定屏已不在：Screens.SelectedScreen 会回退光标屏，居中复位
            Logger.Info("所选屏已移除：悬浮窗复位到当前屏顶部居中");
            return;
        }
        ClampIntoWorkingArea();
    }

    /// <summary>所选屏工作区顶部水平居中、可见板顶上留 16px【4.6、L8】。</summary>
    void PlaceFirstRun()
    {
        var area = Screens.SelectedScreen().WorkingArea;
        double scale = DpiScaleX;
        double w = Width * scale;
        double x = area.Left + (area.Width - w) / 2.0; // 窗口居中 → 三板组视觉居中
        double y = area.Top + (FirstRunTopGap - SquarePlate.BaseMargin) * scale;
        MoveToPhysical(x, y);
        SavePositionNow();
    }

    /// <summary>
    /// 松手校验：可见三板组必须完整落在所选屏工作区内（不出屏、不压任务栏，4.5）。
    /// 窗口比可见板大一圈（四周动画行程），按内缩后的可见包络夹算。
    /// 所选屏统一走 <see cref="Screens.SelectedScreen"/>（07 §3：带缓存 + 指定屏拔掉时回退）。
    /// </summary>
    void ClampIntoWorkingArea()
    {
        var area = Screens.SelectedScreen().WorkingArea;
        double scale = DpiScaleX;
        double ox, oy;
        try
        {
            var origin = PointToScreen(new Point(0, 0));
            ox = origin.X;
            oy = origin.Y;
        }
        catch
        {
            ox = Left * scale;
            oy = Top * scale;
        }
        double winW = ActualWidth * scale;
        double winH = ActualHeight * scale;
        // 可见包络内缩量：左上=BaseMargin；右下=Chrome−BaseMargin−2×HoverOffset（悬停态保守值）
        double insetTl = SquarePlate.BaseMargin * scale;
        double insetBr = (SquarePlate.Chrome - SquarePlate.BaseMargin - 2 * SquarePlate.HoverOffset) * scale;
        double vx = ox + insetTl;
        double vy = oy + insetTl;
        double vw = winW - insetTl - insetBr;
        double vh = winH - insetTl - insetBr;
        double nvx = Math.Min(Math.Max(vx, area.Left), Math.Max(area.Left, area.Right - vw));
        double nvy = Math.Min(Math.Max(vy, area.Top), Math.Max(area.Top, area.Bottom - vh));
        double nx = nvx - insetTl;
        double ny = nvy - insetTl;
        if (Math.Abs(nx - ox) > 0.5 || Math.Abs(ny - oy) > 0.5)
            MoveToPhysical(nx, ny);
    }

    void SavePositionNow()
    {
        try
        {
            var origin = PointToScreen(new Point(0, 0));
            StateStore.Instance.SetPoint2("dock.pos", (int)Math.Round(origin.X), (int)Math.Round(origin.Y));
        }
        catch (Exception ex)
        {
            Logger.Warn($"保存悬浮窗位置失败：{ex.Message}");
        }
    }

    // ---- 点击 vs 拖动（4.4/4.5，手动实现以支持阈值判定，不用 DragMove）----

    void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (_state != DockState.Idle) return;
        _state = DockState.Pressing;
        _pressPhysical = CursorPhysical();
        _pressTimestamp = Stopwatch.GetTimestamp();
        CaptureMouse();
        e.Handled = true;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_state == DockState.Pressing && !_locked) // 锁定悬浮窗：禁拖不禁点（4.5）
        {
            var cur = CursorPhysical();
            if (Math.Abs(cur.X - _pressPhysical.X) > ClickTravelLimitPx ||
                Math.Abs(cur.Y - _pressPhysical.Y) > ClickTravelLimitPx)
            {
                _state = DockState.Dragging;
                Plate.BeginAnimation(SquarePlate.OffsetProperty, null); // 停悬停动画，回静置错位
                Plate.Offset = SquarePlate.RestOffset;
            }
        }
        if (_state == DockState.Dragging)
        {
            var cur = CursorPhysical();
            double dx = cur.X - _pressPhysical.X;
            double dy = cur.Y - _pressPhysical.Y;
            _pressPhysical = cur;
            Left += dx / DpiScaleX;
            Top += dy / DpiScaleY;
        }
    }

    void OnRelease(object sender, MouseButtonEventArgs e)
    {
        // 注意：ReleaseMouseCapture 会同步触发 LostMouseCapture（把 _state 归位 Idle），
        // 必须先取状态再释放，否则点击/拖动的收尾分支永远走不到
        var stateAtRelease = _state;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        switch (stateAtRelease)
        {
            case DockState.Pressing:
                _state = DockState.Idle;
                if (ElapsedSincePress().TotalMilliseconds < ClickMaxMs)
                    Dissolve(); // 位移<4px 且 <300ms 判定为点击【4.4】
                break;
            case DockState.Dragging:
                _state = DockState.Idle;
                ClampIntoWorkingArea();
                _posSaveTimer.Stop();
                _posSaveTimer.Start(); // 500ms 防抖持久化【4.7】
                break;
        }
        e.Handled = true;
    }

    TimeSpan ElapsedSincePress()
    {
        var ticks = (long)((Stopwatch.GetTimestamp() - _pressTimestamp)
                           * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
        return new TimeSpan(ticks);
    }

    static Point CursorPhysical()
    {
        var p = WinForms.Cursor.Position;
        return new Point(p.X, p.Y);
    }

    // ---- 悬停（4.3；批次十二修订 B，用户裁决：触发后完整播放，播放中忽略二次触发
    //      （HoverCycle），播完按指针实际位置补账）----

    readonly HoverCycle _hoverCycle = new();

    void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_state != DockState.Idle || _loading) return; // 加载态：悬停错开禁用（任务13），点击仍可用
        _hoverCycle.OnEnter();
    }

    void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_state != DockState.Idle || _loading) return;
        _hoverCycle.OnLeave();
    }

    // ---- 右键快捷菜单（清单二·修订 B）：悬浮窗上方横向小按钮条（替代纵向 GlassPopup 菜单）----

    /// <summary>右键"隐藏"后触发（App 同步托盘"显示"项的启用态）；复现/启动入场时也触发一次。</summary>
    public event Action? HiddenStateChanged;

    /// <summary>右键"结束程序"（App 接线走 ExitWithFade 离场）。</summary>
    public event Action? ExitRequested;

    /// <summary>悬浮窗是否处于"右键隐藏"态（App 用它启用/禁用托盘"显示"）。</summary>
    public bool IsUserHidden => _userHidden;

    /// <summary>
    /// 开板闸门（清单二·任务13）：App 注入"首载是否就绪"。Dissolve 前询问——未就绪不消散、
    /// 进加载态；null=不设闸（行为同旧版）。
    /// </summary>
    public Func<bool>? OpenGate { get; set; }

    Popup? _quickMenu; // 右键快捷按钮条（StaysOpen=false：单击其余区域即关闭，用户修订 B）

    void OnDockRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_state != DockState.Idle || !IsVisible || _loading) return;
        ShowQuickMenu();
        e.Handled = true;
    }

    /// <summary>
    /// 悬浮窗上方横向小按钮条（隐藏/结束程序）：出现=上浮+淡入（AnimTuning.MenuPop），
    /// StaysOpen=false——菜单显示时单击其余区域即关闭（2026-09-26 修订 B），Esc 亦可关。
    /// </summary>
    void ShowQuickMenu()
    {
        CloseQuickMenu();
        // 06 §2.4：快捷按钮条同样跟随主题 + 材质（小窗不抓屏，用静态材质底）
        bool dark = ThemeResolver.IsDark();
        string material = SettingsStore.Instance.GetString("personal.dockCustom.material", MaterialEngine.Blur);
        var bg = MaterialEngine.Fill(material, dark, 92);
        var line = Ui.Themes.ThemeEngine.Brush("Brush.MenuBorder");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(QuickButton(I18n.Tr("dock.hide"), HideByUser));
        panel.Children.Add(QuickButton(I18n.Tr("app.exit"), () => ExitRequested?.Invoke()));

        var border = new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3),
            BorderThickness = new Thickness(1),
            BorderBrush = line,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3, Direction = 270 },
            Child = panel,
            Opacity = 0,
            Focusable = true,
        };
        var rise = new TranslateTransform(0, 4); // 出现时自下方 4px 上浮
        border.RenderTransform = rise;

        _quickMenu = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Top, // 悬浮窗上方水平居中
            PlacementTarget = Plate,
            // Placement=Top 对齐的是窗口顶缘，三板可见顶缘在窗口顶 + BaseMargin（24px 动画行程区）——
            // 正向偏移把菜单底边拉到贴板顶上方 4px（修订 C：原 -6 离板太远）
            VerticalOffset = SquarePlate.BaseMargin - 4,
            StaysOpen = false, // 单击其余区域自动关闭（修订 B）
            PopupAnimation = PopupAnimation.None,
            Child = border,
        };
        _quickMenu.Closed += (_, _) => _quickMenu = null;
        _quickMenu.Opened += (_, _) =>
        {
            Anim.Run(border, OpacityProperty, 0, 1, AnimTuning.MenuPop, EaseStyle.Linear);
            Anim.Run(rise, TranslateTransform.YProperty, 4, 0, AnimTuning.MenuPop, EaseStyle.EaseOutCubic);
            border.Focus(); // 接住 Esc
        };
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseQuickMenu();
                e.Handled = true;
            }
        };
        _quickMenu.IsOpen = true;
    }

    void CloseQuickMenu()
    {
        if (_quickMenu == null) return;
        var menu = _quickMenu;
        _quickMenu = null;
        menu.IsOpen = false; // Closed 事件随后把字段再清一次，无副作用
    }

    /// <summary>快捷菜单小按钮：胶囊底、hover 提亮一档（00 §9 hoverOverlay，token 取色）；修订 C 缩小字号与内距。</summary>
    static Border QuickButton(string text, Action onClick)
    {
        var hover = Ui.Themes.ThemeEngine.Brush("Brush.MenuHover");
        var fg = Ui.Themes.ThemeEngine.Brush("Brush.TextPrimary");
        var button = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(1),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
        };
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonDown += (_, e) => e.Handled = true;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return button;
    }

    /// <summary>
    /// 右键"隐藏"：播消散过渡后收起（修订 B：不再瞬间消失），功能失效，
    /// 托盘"显示"复现。复用三板消散编排（PlayDissolve）——与单击消散同观感。
    /// </summary>
    void HideByUser()
    {
        if (_state != DockState.Idle || !IsVisible || _loading) return;
        CloseQuickMenu();
        _hoverCycle.Reset();
        _state = DockState.Dissolving; // 过渡期间悬停/点击守卫自然拦截
        Plate.PlayDissolve(AnimTuning.DockDissolve, AnimTuning.DockDissolve, () =>
        {
            Hide();
            _userHidden = true;
            _state = DockState.Hidden;
            HiddenStateChanged?.Invoke();
            Logger.Info("悬浮窗已由右键菜单隐藏（带消散过渡；托盘\"显示\"可复现）");
        });
    }

    /// <summary>退出"右键隐藏"态（Revive/PlayIntro 调用），并通知 App 禁用托盘"显示"。</summary>
    void ClearUserHidden()
    {
        if (!_userHidden) return;
        _userHidden = false;
        HiddenStateChanged?.Invoke();
    }

    /// <summary>呼吸动画：间距 6→10→6（各 AnimTuning.DockHover；托盘左键 3.2 / 二实例唤起 2.2）。</summary>
    public void PlayBreathing()
    {
        if (_state != DockState.Idle || !IsVisible || _loading) return;
        _hoverCycle.Reset(); // 呼吸接管 Offset 属性，悬停状态闩锁复位
        Anim.Run(Plate, SquarePlate.OffsetProperty, null, SquarePlate.HoverOffset, AnimTuning.DockHover, EaseStyle.EaseInOutQuad,
            onDone: () =>
            {
                if (_state != DockState.Idle) return;
                Anim.Run(Plate, SquarePlate.OffsetProperty, null, SquarePlate.RestOffset, AnimTuning.DockHover, EaseStyle.EaseInOutQuad);
            });
    }

    // ---- 单击消散（4.4；编排为 2026-08-28 用户修订版）----

    /// <summary>消散完成后触发（02 步：App 订阅打开收纳板）；无订阅方时回退占位复现。</summary>
    public event Action? DissolveCompleted;

    /// <summary>消散开始即触发（2026-08-29 批次八）：App 用这 ~500ms 并行预构造收纳板
    /// （扫描/容器/解码），开板动画不再被首次加载卡住。</summary>
    public event Action? DissolveStarted;

    public void Dissolve()
    {
        if (_state != DockState.Idle) return;
        if (_loading) return; // 加载中：等 FirstLoadReady 自动开板（点击不误触未就绪链路，拖动不受影响）
        // 开板闸门（任务13）：首载未就绪 → 不消散，进加载态；就绪后 FirstLoadReady 自动续链
        if (OpenGate != null && !OpenGate())
        {
            EnterLoading();
            return;
        }
        _state = DockState.Dissolving;
        _hoverCycle.Reset(); // 消散接管三板属性，悬停状态闩锁复位
        DissolveStarted?.Invoke();
        // 中层停留原地；上/下板向错开方向外移并缩小；三板同步放缓淡出（非瞬间消失）
        Plate.PlayDissolve(AnimTuning.DockDissolve, AnimTuning.DockDissolve, () =>
        {
                Hide();
                _state = DockState.Hidden;
                if (DissolveCompleted != null)
                {
                    DissolveCompleted.Invoke(); // 02 步：消散→打开收纳板，收纳板关闭后 Revive
                }
                else
                {
                    // 占位演示（无订阅方时的兜底）：延时 600ms 原地反向复现
                    _reviveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReviveDelayMs) };
                    _reviveTimer.Tick += ReviveTick;
                    _reviveTimer.Start();
                }
            });
    }

    void ReviveTick(object? sender, EventArgs e)
    {
        if (_reviveTimer != null)
        {
            _reviveTimer.Stop();
            _reviveTimer.Tick -= ReviveTick;
            _reviveTimer = null;
        }
        Revive();
    }

    /// <summary>消散倒放复现（02 步公开化：收纳板关闭后由其调用）。</summary>
    public void Revive()
    {
        if (_state != DockState.Hidden || IsVisible) return;
        ClearUserHidden(); // 托盘"显示"/正常复现：退出右键隐藏态
        Show();
        Plate.PlayRevive(AnimTuning.DockDissolve, AnimTuning.DockDissolve, () => _state = DockState.Idle);
    }

    // ---- 首载加载态（清单二·任务13）----

    /// <summary>进入加载态：两侧圆点淡入并循环闪烁；悬停错开禁用，拖动/点击链路保持响应。</summary>
    void EnterLoading()
    {
        if (_loading) return;
        _loading = true;
        Logger.Info("首载未就绪：悬浮窗进入加载态（两侧圆点循环）");
        StartLoadingPulse();
        Anim.Run(LoadingLeft, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.Linear);
        Anim.Run(LoadingRight, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.Linear);
    }

    void StartLoadingPulse()
    {
        var dots = new[] { LeftDot1, LeftDot2, LeftDot3, RightDot1, RightDot2, RightDot3 };
        for (int i = 0; i < dots.Length; i++)
        {
            int phase = i % 3; // 列内相位错开（BeginTime）
            var pulse = new DoubleAnimation(0.12, 1.0, Anim.Ms(AnimTuning.LoadingPulse))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = Anim.Ms(phase * AnimTuning.LoadingPulsePhase),
            };
            dots[i].BeginAnimation(OpacityProperty, pulse);
        }
    }

    void StopLoadingPulse()
    {
        foreach (var dot in new[] { LeftDot1, LeftDot2, LeftDot3, RightDot1, RightDot2, RightDot3 })
            dot.BeginAnimation(OpacityProperty, null);
    }

    /// <summary>
    /// 首载就绪（App 经 BoardWindow.FirstLoadReady 调入）：圆点"散开+拉高消失"
    /// （左右外移 ~10px + fade，AnimTuning.LoadingScatter），完成后走正常 Dissolve→
    /// DissolveCompleted→OpenBoard（此时闸门即刻通过，无卡顿）。非加载态调用无副作用。
    /// </summary>
    public void OnFirstLoadReady()
    {
        if (!_loading) return;
        _loading = false;
        StopLoadingPulse();
        Logger.Info("首载就绪：加载点散开消失 → 正常消散开板");
        Anim.Run(LoadingLeft, OpacityProperty, null, 0, AnimTuning.LoadingScatter, EaseStyle.Linear);
        Anim.Run(LoadingRight, OpacityProperty, null, 0, AnimTuning.LoadingScatter, EaseStyle.Linear);
        Anim.Run(LoadingLeftShift, TranslateTransform.XProperty, null, -10, AnimTuning.LoadingScatter, EaseStyle.EaseInQuad);
        Anim.Run(LoadingRightShift, TranslateTransform.XProperty, null, 10, AnimTuning.LoadingScatter, EaseStyle.EaseInQuad,
            onDone: () =>
            {
                LoadingLeftShift.X = 0;
                LoadingRightShift.X = 0;
                Dissolve(); // 散开完成后走正常开板链路（闸门已就绪即刻通过）
            });
    }

    // ---- 程序启动/退出出入场（2026-08-30 批次十）----

    /// <summary>
    /// 启动入场：窗口 Show 后播三板自消散态聚合复现（AnimTuning.DockDissolve），替代冷启动瞬间直弹。
    /// 动画期间 _state=Hidden（非 Idle），悬停/点击守卫自然拦截误触发。
    /// </summary>
    public void PlayIntro()
    {
        if (!IsVisible) Show();
        ClearUserHidden(); // 启动入场：退出右键隐藏态（进程刚起不该有，防御性同步）
        _state = DockState.Hidden;
        Plate.PlayRevive(AnimTuning.DockDissolve, AnimTuning.DockDissolve, () => _state = DockState.Idle);
    }

    /// <summary>
    /// 退出离场：复用消散编排（三板分离+淡出，AnimTuning.DockDissolve）后回调，再由调用方真正 Shutdown。
    /// 不走 Dissolve()——那会触发 DissolveStarted/DissolveCompleted 事件拉起收纳板。
    /// </summary>
    public void PlayExit(Action onDone)
    {
        if (!IsVisible || _state is DockState.Dissolving or DockState.Hidden)
        {
            onDone(); // 已隐藏/正在消散：无动画可播，直接放行
            return;
        }
        _state = DockState.Dissolving;
        _hoverCycle.Reset();
        Plate.PlayDissolve(AnimTuning.DockDissolve, AnimTuning.DockDissolve, () =>
        {
            Hide();
            _state = DockState.Hidden;
            onDone();
        });
    }

    /// <summary>
    /// 收纳板会话（02 步，步骤01 记录挂账的落点）：收纳板打开期间临时取消 Topmost
    /// （悬浮窗此时隐藏，防止恢复时的 z 序竞争），关闭后恢复。low 档无需处理。
    /// </summary>
    public void SetBoardSession(bool boardOpen)
    {
        if (_priority == DockZOrder.Low) return;
        Topmost = !boardOpen;
        Logger.Info($"悬浮窗 Topmost 临时降级 = {boardOpen}（收纳板会话）");
    }

    /// <summary>悬浮窗三板组中心（物理像素），收纳板对齐与开合动画锚点用。</summary>
    public Point DockCenterPhysical()
    {
        var origin = PhysicalOrigin();
        return new Point(origin.X + ActualWidth * DpiScaleX / 2, origin.Y + ActualHeight * DpiScaleY / 2);
    }

    // ---- z 序优先级三档（4.8，01b 补丁落地）----

    /// <summary>
    /// 置顶策略（2026-10-03 用户要求）：悬浮窗的窗口优先级**固定在桌面级**（HWND_BOTTOM，
    /// 位于普通窗口之下、桌面之上），不再受 advanced.dockPriority 影响——
    /// 该设置（已更名"窗口优先级"）改管收纳板与设置窗口。
    /// </summary>
    protected override void ApplyTopmostStrategy() => ApplyPriority(DockZOrder.Low);

    /// <summary>
    /// 优先级落地（00 §7/步骤 4.8）：high=恒置顶；medium=暂同 high；low=Topmost=False +
    /// 拦截 WM_WINDOWPOSCHANGING 强制 HWND_BOTTOM。悬浮窗自 2026-10-03 起恒走 Low（桌面级）；
    /// 三档设置改由收纳板/设置窗消费。顺序约定（01b 3.3）：先解除旧模式钩子 → Topmost 落定 → 再挂新钩子；
    /// 反过来先挂低档钩子，紧随的 Topmost 变更窗口位置调用会被钩子改写，DP 覆盖后置底失效。
    /// </summary>
    public void ApplyPriority(DockZOrder mode)
    {
        var source = HwndSourceFromWindow();
        if (_bottomHookInstalled)
        {
            source?.RemoveHook(LowZOrderHook); // 模式切换必须摘旧钩子，防 low 残留强制置底
            _bottomHookInstalled = false;
        }

        switch (mode)
        {
            case DockZOrder.Low:
                Topmost = false; // 先落定 Topmost 再挂钩子
                if (source != null)
                    _bottomHookInstalled = true;
                else
                    Logger.Warn("low 档挂钩时窗口句柄未就绪；SourceInitialized 时会随 ApplyTopmostStrategy 重挂");
                break;
            case DockZOrder.Medium:
                // 2026-10-05：原"medium 档检测到全屏则隐藏悬浮窗"已作废（悬浮窗恒桌面级、
                // 全屏应用天然盖住它）。全屏退让改由 App 层统一实现，作用于**置顶的收纳板/设置窗**
                // （07 §8 方案 B，App.OnFullScreenTick）——此处不再各自探测。
                Topmost = true;
                break;
            default: // high
                Topmost = true;
                break;
        }

        if (_bottomHookInstalled)
            source!.AddHook(LowZOrderHook);

        _priority = mode;
        Logger.Info($"悬浮窗 z 序优先级 = {mode}（Topmost={Topmost}，置底钩子={_bottomHookInstalled}）");
    }

    HwndSource? HwndSourceFromWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        return hwnd == IntPtr.Zero ? null : HwndSource.FromHwnd(hwnd);
    }

    /// <summary>
    /// low 档置底钩子：本次位置变更要动 z 序（flags 无 SWP_NOZORDER）就把插入位改写为 HWND_BOTTOM，
    /// 使悬浮窗始终位于普通窗口之下。只改写 lParam 上的 WINDOWPOS，钩子内绝不调 SetWindowPos（重入）。
    /// 拖动/移动每次都进此钩子，故不写日志（防刷爆日志）。
    /// </summary>
    IntPtr LowZOrderHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != SystemBridge.WM_WINDOWPOSCHANGING || _priority != DockZOrder.Low)
            return IntPtr.Zero;
        var wp = Marshal.PtrToStructure<SystemBridge.WINDOWPOS>(lParam);
        if ((wp.flags & SystemBridge.SWP_NOZORDER) == 0)
        {
            wp.hwndInsertAfter = SystemBridge.HWND_BOTTOM;
            Marshal.StructureToPtr(wp, lParam, fDeleteOld: true);
        }
        return IntPtr.Zero;
    }

    // 2026-10-05：medium 档占位 API（OnForegroundFullScreenChanged / ProbeForegroundFullScreen）
    // 随"悬浮窗恒定桌面级 + 全屏退让改管收纳板/设置窗"一并删除（07 §8 方案 B 落地，见 App.OnFullScreenTick）。

    // ---- 设置联动 ----

    void OnSettingChanged(SettingChangedArgs args)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSettingChanged(args));
            return;
        }
        switch (args.Key)
        {
            case "display.dockSize":
                _dockSize = Math.Clamp(SettingsStore.Instance.GetDouble("display.dockSize", 58.7), 40, 96);
                Plate.PlateSize = _dockSize;
                Width = _dockSize + SquarePlate.Chrome;
                Height = _dockSize + SquarePlate.Chrome;
                ClampIntoWorkingArea();
                break;
            case "advanced.dockLocked":
                _locked = SettingsStore.Instance.GetBool("advanced.dockLocked", false);
                Cursor = _locked ? Cursors.Arrow : Cursors.Hand;
                break;
            case "display.screen":
                ClampIntoWorkingArea();
                break;
            case "theme.mode":
            case "advanced.dockFollowTheme":
                ApplyDockTheme(); // 悬浮窗跟随主题（2026-10-03 开关控制）
                break;
            case "personal.dockCustom.mode":
            case "personal.dockCustom.material":
            case "personal.dockCustom.images":
            case "personal.dockCustom.opacity":
                ApplyDockCustom(); // 悬浮窗自定义即时生效（※）；colors 键已随颜色盘功能删除（清单02任务8）
                break;
        }
    }

    /// <summary>悬浮窗主题（2026-10-03）：advanced.dockFollowTheme 开（默认）随主题亮暗；
    /// 关=固定亮色观感（不跟随暗夜主题）。</summary>
    void ApplyDockTheme()
    {
        bool follow = SettingsStore.Instance.GetBool("advanced.dockFollowTheme", false);
        // 三板材质来源=MaterialEngine（06 §4）；层图片覆盖逻辑不变
        Plate.SetTheme(follow && ThemeResolver.IsDark());
    }
}
