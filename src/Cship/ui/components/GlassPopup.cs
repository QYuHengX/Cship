using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Cship.Core;
using Cship.Ui.Animations;
using Cship.Ui.Themes;
using WinForms = System.Windows.Forms;

namespace Cship.Ui.Components;

/// <summary>
/// 自绘玻璃弹出菜单（步骤 4，不用系统 ContextMenu）：圆角 12、跟随亮暗主题、
/// 锚点缩放+淡入 120ms 出现、淡出+微缩 130ms 关闭（2026-08-29 批次五）、子菜单向右展开、
/// 失焦/Esc/选择后关闭；支持勾选态、禁用态、图标。
/// 2026-08-29 用户反馈：
/// - 子菜单交互改定时制：指针在母折叠项停留 300ms 后展开；指针离开母项与子菜单 100ms 后收起；
///   同一时间至多存在一个子菜单。
/// - 菜单（含子菜单）宽度收紧：左右边界与选项文字相距约 1.5 个汉字（≈1.5×字号）。
/// - 批次五：子菜单放置先实测尺寸，右侧屏幕放不下翻转到母菜单左侧，不与母菜单重叠；
///   菜单项单击即执行动作并带关闭动画。
/// </summary>
public sealed class PopupMenuItem
{
    public string Header { get; init; } = "";
    public bool Checked { get; init; }
    public bool Enabled { get; init; } = true;
    public Action? OnClick { get; init; }
    public IReadOnlyList<PopupMenuItem>? SubItems { get; init; }
    /// <summary>可选菜单项图标（Path 几何串）。</summary>
    public string? IconData { get; init; }
    public bool IsSeparator { get; init; }

    public static PopupMenuItem Separator() => new() { IsSeparator = true };
}

public static class GlassPopup
{
    static readonly List<Popup> Chain = new(); // 打开链（父→子），统一关闭

    // 子菜单定时交互（2026-08-29）：同一时间至多一个子菜单，全链路共用一组计时器
    static Popup? _openSub;                 // 当前展开的子菜单
    static DispatcherTimer? _subOpenTimer;  // 母项悬停 300ms 展开计时
    static DispatcherTimer? _subCloseTimer; // 离开母项/子菜单 100ms 收起计时
    static readonly object TimerGate = new();

    const double SubOpenDelayMs = 300;
    const double SubCloseDelayMs = 100;
    const double ShadowPad = 16; // 弹出层四周留白：给 DropShadowEffect 出血空间，避免圆角处阴影被裁成"脏角"

    const int WM_LBUTTONDOWN = 0x0201;
    const int WM_RBUTTONDOWN = 0x0204;
    static bool _filterInstalled;

    /// <summary>外部点击统一关闭（2026-08-29 批次四）：子菜单 StaysOpen=true 后，
    /// 父菜单"点击外部自动关"失效（捕获被子菜单打开破坏）。改用线程消息过滤器：
    /// 任何鼠标按下落在全部已开弹出层矩形之外 → CloseAll。安装在首次 Show，链空时摘除。</summary>
    static void EnsureOutsideClickFilter()
    {
        if (_filterInstalled) return;
        _filterInstalled = true;
        System.Windows.Interop.ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
    }

    static void DetachOutsideClickFilter()
    {
        if (!_filterInstalled) return;
        _filterInstalled = false;
        System.Windows.Interop.ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
    }

    /// <summary>当前菜单链的锚点（Show 登记；链空即清）。</summary>
    static FrameworkElement? _chainAnchor;

    /// <summary>给定锚点是否正是当前已展开菜单链的锚点（2026-10-05 需求④修订）：
    /// 分类顶栏"更多"靠它把按钮改成**真正的开合切换**——已展开时再点=收回，而不是又弹一张。</summary>
    public static bool IsOpenFor(FrameworkElement anchor)
        => Chain.Count > 0 && ReferenceEquals(_chainAnchor, anchor);

    /// <summary>本次鼠标按下把**哪个锚点**的菜单"点外收回"了（null=最近一次按下没收回任何菜单）。
    /// 2026-10-05 需求④修订：点外收起发生在**按下**、按钮动作发生在**抬起**，同一次点击会被判成
    /// "先收后弹"（用户报的"展开卡收回后会二次展开"根因）。锚点在抬起时问一次"刚才被收回的是不是
    /// 我这张"，是则把这次点击整体当作"收回"。**按锚点区分**是必要的：别的菜单（如板内右键菜单）
    /// 被这次点击收走时，本锚点这一次仍然应该照常展开。</summary>
    static FrameworkElement? _outsideClosedAnchor;

    /// <summary>刚才那次"点外收回"收的是不是 anchor 自己的菜单（见 <see cref="_outsideClosedAnchor"/>）。</summary>
    public static bool JustClosedByOutsideClick(FrameworkElement anchor)
        => _outsideClosedAnchor != null && ReferenceEquals(_outsideClosedAnchor, anchor);

    static void OnThreadFilterMessage(ref System.Windows.Interop.MSG msg, ref bool handled)
    {
        if (msg.message != WM_LBUTTONDOWN && msg.message != WM_RBUTTONDOWN) return;
        if (Chain.Count == 0)
        {
            // 无弹出层：清掉上一次的"点外收回"标记。**这条分支必须还能被调到**——点外收起后
            // 链就空了，若此时径直摘掉过滤器，下一次点击的按下就没人来清标记，抬起时锚点会拿着
            // 过期标记把"打开"误判成"收回"。故标记未消费前保留过滤器，消费掉这次才摘。
            bool pending = _outsideClosedAnchor != null;
            _outsideClosedAnchor = null;
            if (pending) DetachOutsideClickFilter();
            return;
        }
        // lParam 是"接收消息窗口"的客户区坐标（点在弹出层上时=弹出层 HWND 的客户区），
        // 必须经 ClientToScreen 转屏幕坐标后再比较——批次四漏了这一步，点击永远被判为
        // "外部"而 CloseAll，菜单项单击只关菜单不执行动作（2026-08-29 批次五根因之二）
        int x = (short)((long)msg.lParam & 0xFFFF);
        int y = (short)(((long)msg.lParam >> 16) & 0xFFFF);
        var pt = new SystemBridge.POINT { X = x, Y = y };
        if (msg.hwnd != IntPtr.Zero && SystemBridge.ClientToScreen(msg.hwnd, ref pt))
        {
            x = pt.X;
            y = pt.Y;
        }
        foreach (var popup in Chain)
        {
            // 经 popup.Tag（Build 时登记 Border）取菜单矩形：批次四给 Child 包了阴影留白
            // Grid 后，检查 Child is Border 永不匹配，任何按下都被判为外部点击
            if (popup.Tag is Border border && border.RenderSize.Width > 0)
            {
                try
                {
                    var tl = border.PointToScreen(new Point(0, 0));
                    var dpi = VisualTreeHelper.GetDpi(border);
                    double w = border.RenderSize.Width * dpi.DpiScaleX;
                    double h = border.RenderSize.Height * dpi.DpiScaleY;
                    if (x >= tl.X - ShadowPad * dpi.DpiScaleX && x <= tl.X + w + ShadowPad * dpi.DpiScaleX
                        && y >= tl.Y - ShadowPad * dpi.DpiScaleY && y <= tl.Y + h + ShadowPad * dpi.DpiScaleY)
                    {
                        _outsideClosedAnchor = null; // 点在弹出层内：这是一次"点菜单"而非"点外部"
                        return;
                    }
                }
                catch { /* 元素未加载等，忽略该层 */ }
            }
        }
        // 先记"被收回的是哪个锚点的菜单"再收口：CloseAll 会把 _chainAnchor 清掉，
        // 而同一次点击的"抬起"还要靠这个登记来判"该不该二次展开"
        _outsideClosedAnchor = _chainAnchor;
        CloseAll();
    }

    /// <summary>在 anchor 元素坐标系的 offset 处弹出菜单。
    /// onClosed（2026-10-05 需求④，可空）：本层关闭后回调——分类顶栏用它把"更多"三角形转回朝下。</summary>
    public static void Show(FrameworkElement anchor, Point offset, IReadOnlyList<PopupMenuItem> items,
        Action? onClosed = null)
    {
        CloseAll(animate: false); // 开新菜单立即关旧：避免旧菜单关闭动画与新菜单短暂叠影
        _chainAnchor = anchor;    // 登记锚点（必须在 Build 入链之前，链里不允许出现没有锚点的层）
        EnsureOutsideClickFilter();
        // 本轮菜单链的宿主窗口（子菜单的锚点在弹出层的可视树里，Window.GetWindow 取不到——
        // 记下首个锚点所属窗口，供整条链同步置顶态用，清单05任务8）
        _ownerWindow = Window.GetWindow(anchor);
        // 不做 -ShadowPad 补偿：WPF Popup 定位时自动排除子元素 Margin（Border 内容对齐到
        // target+offset），再减一次会把整块菜单平移 16px（2026-08-29 批次五实测定位偏差的根因）
        var popup = Build(anchor, items, PlacementMode.RelativePoint, offset, isSubmenu: false);
        if (onClosed != null)
            popup.Closed += (_, _) => onClosed();
        popup.IsOpen = true;
    }

    /// <summary>
    /// 托盘右键菜单（步骤 06 §3）：无 WPF 锚点，按<b>物理光标坐标</b>绝对定位弹出——
    /// 先按该点所在屏的 DPI 折算 DIP，再实测菜单尺寸把菜单摆到光标左上（托盘在屏幕底部，
    /// 向下弹会出屏），并夹回该屏可视区。托盘菜单需要 Esc 可关，故弹出后取前台焦点。
    /// </summary>
    public static void ShowAtScreen(System.Windows.Point screenPhysical, IReadOnlyList<PopupMenuItem> items)
    {
        CloseAll(animate: false);
        _chainAnchor = null; // 无锚点：点外收回标记对任何锚点都不生效（不影响其它菜单）
        EnsureOutsideClickFilter();
        _ownerWindow = null;
        double scale = SystemBridge.ScaleAt(screenPhysical);
        if (scale <= 0) scale = 1.0;
        // Absolute 定位的偏移以"虚拟屏左上角"为原点（WPF Popup 语义），按屏 DPI 折 DIP
        var origin = new Point(
            (screenPhysical.X - SystemParameters.VirtualScreenLeft) / scale,
            (screenPhysical.Y - SystemParameters.VirtualScreenTop) / scale);
        var popup = Build(null, items, PlacementMode.Absolute, origin, isSubmenu: false, forceTopmost: true);

        double wDip = 0, hDip = 0;
        if (popup.Child is Grid wrapGrid)
            wrapGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (popup.Tag is Border border)
        {
            var size = border.RenderSize.Width > 0 ? border.RenderSize : MeasureBorder(border);
            wDip = size.Width + 2 * ShadowPad;
            hDip = size.Height + 2 * ShadowPad;
        }
        var screen = WinForms.Screen.FromPoint(new System.Drawing.Point((int)screenPhysical.X, (int)screenPhysical.Y));
        double screenLeft = (screen.Bounds.Left - SystemParameters.VirtualScreenLeft) / scale;
        double screenTop = (screen.Bounds.Top - SystemParameters.VirtualScreenTop) / scale;
        double screenW = screen.Bounds.Width / scale;
        double screenH = screen.Bounds.Height / scale;
        double x = origin.X - wDip;           // 菜单右缘贴光标（向左展开）
        double y = origin.Y - hDip - 6;       // 菜单底边在光标上方 6px
        x = Math.Min(Math.Max(x, screenLeft + 4), Math.Max(screenLeft + 4, screenLeft + screenW - wDip - 4));
        y = Math.Min(Math.Max(y, screenTop + 4), Math.Max(screenTop + 4, screenTop + screenH - hDip - 4));
        popup.HorizontalOffset = x;
        popup.VerticalOffset = y;
        popup.IsOpen = true;
        Logger.Info($"托盘菜单弹出：光标物理({screenPhysical.X:0},{screenPhysical.Y:0}) 尺寸 {wDip:0}×{hDip:0} → DIP({x:0},{y:0})");
    }

    /// <summary>关闭全部弹出层。默认带淡出+微缩关闭动画（2026-08-29 批次五）；
    /// animate=false 用于开新菜单前的立即清理。Chain 立即清空（外部点击过滤器随即摘除），
    /// 动画仅作用于视觉，关闭中的层已不可命中。</summary>
    public static void CloseAll(bool animate = true)
    {
        lock (TimerGate)
        {
            StopTimers();
            _openSub = null;
        }
        var snapshot = Chain.ToList();
        Chain.Clear();
        if (Chain.Count == 0)
        {
            _chainAnchor = null; // 链空：锚点登记一并作废（IsOpenFor 随之恒 false）
            // 过滤器不在这里摘：若本次是"点外收回"，标记还要留给同一次点击的"抬起"读，
            // 由 OnThreadFilterMessage 消费掉标记后再摘（否则下一次点击会读到过期标记）
            if (_outsideClosedAnchor == null) DetachOutsideClickFilter();
        }
        foreach (var popup in snapshot)
            ClosePopup(popup, animate);
    }

    /// <summary>关闭单个弹出层（2026-08-29 批次五）：带关闭动画——淡出+锚点微缩
    /// （AnimTuning.MenuClose，与 MenuPop 出现动画对称），完成后才置 IsOpen=false；
    /// 期间禁用命中避免"关了还能点"。无 Border（未打开/异常）时立即关闭。</summary>
    static void ClosePopup(Popup popup, bool animate = true)
    {
        if (!animate || popup.Tag is not Border border || !popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }
        if (popup.Child is System.Windows.Controls.Grid wrapper)
            wrapper.IsHitTestVisible = false;
        Anim.Run(border, UIElement.OpacityProperty, null, 0, AnimTuning.MenuClose, EaseStyle.EaseInQuad,
            onDone: () => popup.IsOpen = false);
        if (border.RenderTransform is ScaleTransform scale)
        {
            Anim.Run(scale, ScaleTransform.ScaleXProperty, null, 0.96, AnimTuning.MenuClose, EaseStyle.EaseInQuad);
            Anim.Run(scale, ScaleTransform.ScaleYProperty, null, 0.96, AnimTuning.MenuClose, EaseStyle.EaseInQuad);
        }
    }

    static void StopTimers()
    {
        if (_subOpenTimer != null) { _subOpenTimer.Stop(); _subOpenTimer = null; }
        if (_subCloseTimer != null) { _subCloseTimer.Stop(); _subCloseTimer = null; }
    }

    static Popup Build(FrameworkElement? anchor, IReadOnlyList<PopupMenuItem> items,
        PlacementMode placement, Point offset, bool isSubmenu, bool forceTopmost = false)
    {
        // 06 §2.4：各级菜单跟随主题 + 材质（token 换肤 + MaterialEngine 底色；小窗不抓屏，用静态材质底）
        bool dark = ThemeResolver.IsDark();
        string material = SettingsStore.Instance.GetString("personal.material", MaterialEngine.None);
        var bg = MaterialEngine.Fill(material, dark, 92);
        var fg = ThemeEngine.Brush("Brush.TextPrimary");
        var hover = ThemeEngine.Brush("Brush.MenuHover"); // 00 §9 hoverOverlay
        var divider = ThemeEngine.Brush("Brush.Divider");

        // 菜单宽度收紧（批次十二，原文任务2）：宽度随最宽选项文字自适应，
        // 行左右内距 3 + 菜单内边距 4 ≈ 7px ≈ 半个汉字（13px 字号）——文字距菜单边界约半个汉字
        var panel = new StackPanel();

        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                panel.Children.Add(new Border { Height = 1, Background = divider, Margin = new Thickness(8, 4, 8, 4) });
                continue;
            }
            panel.Children.Add(BuildRow(item, fg, hover, isSubmenu));
        }

        var border = new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeEngine.Brush("Brush.MenuBorder"),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.35 },
            Child = panel,
            Focusable = true,
        };
        // 四周留白包裹：DropShadowEffect 的模糊超出 Border 边界会被弹出层 HWND 裁掉，
        // 圆角处残留"擦不净的深色角"；留白让阴影完整渲染（2026-08-29 批次四）
        var wrapper = new Grid { Margin = new Thickness(ShadowPad) };
        wrapper.Children.Add(border);

        // 出现动画：锚点 ScaleTransform 0.95→1 + 淡入 120ms【4.1】
        var scale = new ScaleTransform(0.95, 0.95);
        border.RenderTransform = scale;
        border.RenderTransformOrigin = new Point(0, 0);
        border.Opacity = 0;

        var popup = new Popup
        {
            AllowsTransparency = true,
            // 子菜单必须 StaysOpen=true：否则展开瞬间抢鼠标捕获，父行被触发 Leave/Enter 风暴
            // （实测：展开后 115ms 内反复排程、子菜单闪烁）。关闭由父菜单 Closed→PruneFrom
            // 与 100ms 离开定时器负责（2026-08-29）。
            StaysOpen = placement != PlacementMode.Right,
            Placement = placement,
            PlacementTarget = anchor, // 托盘菜单=null（Placement=Absolute 只认偏移）
            HorizontalOffset = offset.X,
            VerticalOffset = offset.Y,
            PopupAnimation = PopupAnimation.None,
            Child = wrapper,
        };
        popup.Tag = border; // 登记菜单 Border：外部点击过滤与关闭动画都要越过阴影留白 wrapper 拿到它

        popup.Opened += (_, _) =>
        {
            // 出现动画：锚点 ScaleTransform 0.95→1 + 淡入（AnimTuning.MenuPop）【4.1/03 §3.4】
            Anim.Run(scale, ScaleTransform.ScaleXProperty, 0.95, 1, AnimTuning.MenuPop, EaseStyle.EaseOutCubic);
            Anim.Run(scale, ScaleTransform.ScaleYProperty, 0.95, 1, AnimTuning.MenuPop, EaseStyle.EaseOutCubic);
            Anim.Run(border, UIElement.OpacityProperty, 0, 1, AnimTuning.MenuPop, EaseStyle.Linear);
            border.Focus(); // 接住 Esc
            if (forceTopmost)
            {
                // 托盘菜单无 WPF 宿主窗口：置顶 + 取前台焦点，才能压在其它程序之上并接住 Esc
                if (popup.Child is Visual vis
                    && System.Windows.PresentationSource.FromVisual(vis) is System.Windows.Interop.HwndSource src)
                {
                    SystemBridge.SetTopmost(src.Handle, true);
                    SystemBridge.SetForegroundWindow(src.Handle);
                }
            }
            else if (anchor != null)
            {
                SyncOwnerPriority(popup, anchor); // 菜单优先级跟随所属窗口（清单05任务8）
            }
        };
        popup.Closed += (_, _) => PruneFrom(popup);
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseAll();
                e.Handled = true;
            }
        };

        Chain.Add(popup);
        return popup;
    }

    /// <summary>关闭 popup 及其之后打开的子菜单（父关子随）。</summary>
    static void PruneFrom(Popup popup)
    {
        int index = Chain.IndexOf(popup);
        if (index < 0) return;
        for (int i = Chain.Count - 1; i >= index; i--)
        {
            var p = Chain[i];
            Chain.RemoveAt(i);
            if (!ReferenceEquals(p, popup))
                ClosePopup(p); // 带关闭动画（2026-08-29 批次五）
        }
        if (Chain.Count == 0)
        {
            _chainAnchor = null; // 链空：锚点登记一并作废（这条路径=菜单被自身关闭，如选中菜单项）
            if (_outsideClosedAnchor == null) DetachOutsideClickFilter();
        }
    }

    // ---- 子菜单定时展开/收起（2026-08-29）----

    /// <summary>母折叠项悬停 300ms 后展开子菜单（同一时间至多一个）。</summary>
    static void ScheduleSubmenuOpen(FrameworkElement row, PopupMenuItem item)
    {
        lock (TimerGate)
        {
            if (_subCloseTimer != null) { _subCloseTimer.Stop(); _subCloseTimer = null; } // 已在母项/子菜单间移动：取消收起
            if (_subOpenTimer != null) _subOpenTimer.Stop();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SubOpenDelayMs) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                lock (TimerGate)
                {
                    if (!ReferenceEquals(_subOpenTimer, timer)) return;
                    _subOpenTimer = null;
                }
                Logger.Info($"子菜单定时到达：hover={row.IsMouseOver} items={item.SubItems!.Count}");
                if (!row.IsMouseOver) return; // 等待期间指针已离开
                if (_openSub != null)
                {
                    var old = _openSub;
                    _openSub = null;
                    ClosePopup(old); // 只保留一个子菜单
                }
                // 放置位置显式计算（2026-08-29）：行右侧贴齐；放不下时翻转。不用 Placement=Right
                // 默认逻辑——它把放不下的子菜单对齐到指针附近，实测会整块叠在父菜单之下不可见。
                // 批次五（2026-08-29）：先构建并实测子菜单尺寸再放置——
                // ① 右侧屏幕放不下时 WPF 会把弹出层整体夹回屏内，压在母菜单上 → 改翻转到母菜单左侧；
                // ② 垂直收改用实测高度，替换估算值。
                var tl = row.PointToScreen(new Point(0, 0)); // 物理像素
                var dpi = VisualTreeHelper.GetDpi(row);
                var sub = Build(row, item.SubItems!, PlacementMode.RelativePoint, new Point(0, 0), isSubmenu: true);
                if (sub.Child is System.Windows.Controls.Grid wrapGrid)
                    wrapGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double subWPhys = 0, subHPhys = 0;
                if (sub.Tag is Border subBorder)
                {
                    var sz = subBorder.RenderSize.Width > 0 ? subBorder.RenderSize : MeasureBorder(subBorder);
                    subWPhys = sz.Width * dpi.DpiScaleX;
                    subHPhys = sz.Height * dpi.DpiScaleY;
                }
                var screen = WinForms.Screen.FromPoint(new System.Drawing.Point((int)tl.X, (int)tl.Y));
                double rightPhys = screen.Bounds.Right;
                // 母菜单 Border 右缘 = 行右缘 + 内边距4 + 边框1；子菜单贴其外 1~2px
                double rowRightPhys = tl.X + row.ActualWidth * dpi.DpiScaleX;
                double parentBorderLeftPhys = tl.X - 5 * dpi.DpiScaleX;
                double xPhys = rowRightPhys + 8 * dpi.DpiScaleX;
                if (xPhys + subWPhys > rightPhys - 4)
                {
                    double flipped = parentBorderLeftPhys - subWPhys - 2 * dpi.DpiScaleX;
                    xPhys = flipped >= 4 ? flipped : Math.Max(rightPhys - 4 - subWPhys, 4); // 无翻转空间则贴右缘兜底
                }
                double yPhys = tl.Y;
                if (yPhys + subHPhys > screen.Bounds.Bottom - 4)
                    yPhys = Math.Max(screen.Bounds.Bottom - 4 - subHPhys, 4);
                // 不做 -ShadowPad 补偿（同 Show：Popup 定位已排除 Margin，Border 落在 target+offset）
                sub.HorizontalOffset = (xPhys - tl.X) / dpi.DpiScaleX;
                sub.VerticalOffset = (yPhys - tl.Y) / dpi.DpiScaleY;
                HookSubmenuHover(sub);
                _openSub = sub;
                sub.IsOpen = true;
                Logger.Info($"子菜单已展开 w={subWPhys:0} h={subHPhys:0} x={xPhys:0} 翻转={xPhys < tl.X}");
            };
            _subOpenTimer = timer;
            timer.Start();
            Logger.Info("子菜单展开定时已排（300ms）");
        }
    }

    /// <summary>弹出前测量 Border 实际尺寸（尚未加载进可视树时用）。</summary>
    static Size MeasureBorder(Border border)
    {
        border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return border.DesiredSize;
    }

    /// <summary>
    /// 菜单窗口优先级跟随所属窗口（2026-10-05 清单05任务8）：Popup 是独立顶层 HWND，WPF 只在
    /// 创建时按宿主窗口的置顶状态落定一次——收纳板切到"低"优先级（不置顶）后菜单仍浮在最上层，
    /// 与板本身"被普通窗口盖住"的语义不一致。打开时按 anchor 所在窗口的 Topmost 同步一次，
    /// 得到"板不置顶 → 菜单也不置顶"的一致行为（子菜单同样经 Build 走到这里）。
    /// </summary>
    static void SyncOwnerPriority(Popup popup, FrameworkElement? anchor)
    {
        try
        {
            var owner = (anchor != null ? Window.GetWindow(anchor) : null) ?? _ownerWindow; // 子菜单的锚点在弹出层里，回落到本轮宿主
            if (owner == null || popup.Child is not Visual child) return;
            if (System.Windows.PresentationSource.FromVisual(child) is not System.Windows.Interop.HwndSource src)
                return;
            SystemBridge.SetTopmost(src.Handle, owner.Topmost);
        }
        catch { /* 句柄未就绪等异常不挡菜单显示 */ }
    }

    static Window? _ownerWindow; // 本轮菜单链的宿主窗口（Show 时记录）

    /// <summary>指针离开母项/子菜单 100ms 后收起当前子菜单。</summary>
    static void ScheduleSubmenuClose()
    {
        lock (TimerGate)
        {
            if (_subOpenTimer != null) { _subOpenTimer.Stop(); _subOpenTimer = null; } // 等待展开期间离开：不再展开
            if (_subCloseTimer != null) _subCloseTimer.Stop();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SubCloseDelayMs) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                lock (TimerGate)
                {
                    if (!ReferenceEquals(_subCloseTimer, timer)) return;
                    _subCloseTimer = null;
                    var sub = _openSub;
                    _openSub = null;
                    if (sub != null) ClosePopup(sub); // 带关闭动画（批次五）
                }
            };
            _subCloseTimer = timer;
            timer.Start();
        }
    }

    /// <summary>子菜单进入：取消收起计时；离开：排 100ms 收起。</summary>
    static void HookSubmenuHover(Popup sub)
    {
        if (sub.Child is Border border)
        {
            border.MouseEnter += (_, _) =>
            {
                lock (TimerGate)
                {
                    if (_subCloseTimer != null) { _subCloseTimer.Stop(); _subCloseTimer = null; }
                }
            };
            border.MouseLeave += (_, _) => ScheduleSubmenuClose();
        }
    }

    static FrameworkElement BuildRow(PopupMenuItem item, Brush fg, Brush hover, bool inSubmenu)
    {
        var row = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(3, 0, 3, 0),
            Background = Brushes.Transparent,
            Cursor = item.Enabled ? Cursors.Hand : Cursors.Arrow,
            Opacity = item.Enabled ? 1 : 0.4,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });

        if (!string.IsNullOrEmpty(item.IconData))
        {
            var icon = new Path
            {
                Data = Geometry.Parse(item.IconData),
                Fill = fg,
                Stretch = Stretch.Uniform,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);
        }
        else if (item.Checked)
        {
            var check = new Path
            {
                Data = Geometry.Parse("M4,10 L9,15 L18,5"),
                Stroke = fg,
                StrokeThickness = 2,
                Stretch = Stretch.None,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);
        }

        var header = new TextBlock
        {
            Text = item.Header,
            Foreground = fg,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(header, 1);
        grid.Children.Add(header);

        if (item.SubItems != null)
        {
            var arrow = new Path
            {
                Data = Geometry.Parse("M0,0 L5,5 L0,10"),
                Stroke = fg,
                StrokeThickness = 1.6,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);
        }

        row.Child = grid;
        if (!item.Enabled) return row;

        row.MouseEnter += (_, _) =>
        {
            row.Background = hover;
            if (item.SubItems != null)
                ScheduleSubmenuOpen(row, item);
            else if (inSubmenu)
            {
                // 子菜单内的行：保持子菜单开启，仅取消收起计时（2026-08-29 批次四修
                // "悬浮在子菜单上子菜单仍会关闭"——此前子菜单自身的普通行也走了收起逻辑）
                lock (TimerGate)
                {
                    if (_subCloseTimer != null) { _subCloseTimer.Stop(); _subCloseTimer = null; }
                }
            }
            else
                ScheduleSubmenuClose(); // 父菜单普通行：收起现存子菜单（同口径 100ms 缓冲）
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            if (item.SubItems != null)
                ScheduleSubmenuClose(); // 离开母折叠项：100ms 后收起（若进入子菜单则取消）
        };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (item.SubItems != null) return; // 有子菜单的父项点击不关闭（由子项执行）
            Logger.Info($"菜单行点击：{item.Header}");
            CloseAll();
            item.OnClick?.Invoke();
        };
        return row;
    }
}
