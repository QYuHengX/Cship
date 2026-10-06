using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Cship.Core;
using Cship.Models;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 图标项（步骤 3）：hover 上跳、遮罩、hoverReveal 圆形渐显、指示灯、打开三点动画、内联重命名。
/// 动画时长/缓动一律引用 AnimTuning（步骤 03 集中化）；
/// 网格参数由 BoardWindow 在设置变更时统一调 ApplySettings 刷新
/// （本控件自身不订阅 SettingsChanged，避免逐项订阅泄漏）。
/// 2026-10-02：满铺方形图标内缩磨圆；指示灯改短长条并挪出图标区（图标动画不动它）；
/// hoverReveal=指针为中心的圆形虚化裁切。
/// 2026-10-04（错误修复③）：hoverReveal 下指示灯与下方文件名一并随圆圈隐藏（此前只遮图标本体）；
/// 悬停文件名气泡整体上移到 BoardWindow.HoverTip（④：完整名字/不透明底/显示在图标上方）。
/// 2026-10-05（清单05）：④指示灯/三点落在"图标与名称之间"的固定预留条（与 showLabels 无关）；
/// ②display.iconOpacity 同时作用于指示灯与文件名；⑤恢复打开三点循环（IsRunning 转亮合并为指示灯）
/// 并与"图标短缩弹一下"并存；⑦Loaded 补挂 DataContext——容器被回收重挂时 DataContextChanged
/// 不再触发，原先只剩 _item=null。
/// </summary>
public partial class IconItem : UserControl
{
    static readonly EaseStyle HoverEase = EaseStyle.EaseInOutQuad;
    BoardItem? _item;
    bool _dark;
    bool _revealed = true;       // hoverReveal 当前目标（非 hoverReveal 模式恒 true）
    bool _renameActive;
    bool _indicatorEnabled = true; // advanced.runningIndicator 快照
    bool _revealMode;              // display.iconStyle=hoverReveal 快照
    double _iconOpacity = 1.0;     // display.iconOpacity 快照（0~1）：图标/指示灯/文件名同一口径
    bool _dotsActive;              // 打开三点循环进行中（清单05任务5）
    readonly DispatcherTimer _dotTimeout; // 三点循环兜底（防目标程序检测不到时无限闪烁）
    const double DotsTimeoutMs = 12000;
    Point _revealCenter;           // 最近一次写入画刷的圆心（SetReveal 短路用）
    double _revealRadius;
    bool _revealCenterSet;

    public IconItem()
    {
        InitializeComponent();
        _dotTimeout = new DispatcherTimer { Interval = Anim.Ms(DotsTimeoutMs) };
        _dotTimeout.Tick += (_, _) =>
        {
            _dotTimeout.Stop();
            if (!_dotsActive) return;
            // 兜底（2026-10-05 优化）：某些程序（文档类/检测不到进程）永远等不到 IsRunning 转亮，
            // 三点会 Forever 循环占用渲染动画槽位；12s 仍未合并即按"打开失败"收尾。
            Logger.Info($"打开三点：{_item?.Name} 超时未合并，自动收尾");
            AbortOpening();
        };
        _jumpCycle.Configure(
            done => Anim.Run(Jump, TranslateTransform.YProperty, null, -AnimTuning.IconLiftPx, AnimTuning.HoverJump, HoverEase, onDone: done),
            done => Anim.Run(Jump, TranslateTransform.YProperty, null, 0, AnimTuning.HoverJump, HoverEase, onDone: done),
            () => IsMouseOver);
        DataContextChanged += OnDataContextChanged;
        // 2026-10-05 错误修复⑦：容器被 ItemsControl 回收重挂（排序/对账 Move 时 Unloaded→Loaded）后
        // DataContext 未变、DataContextChanged 不再触发，_item 会一直停在 Unloaded 清出的 null，
        // 表现为"图标还在、但悬停不出文件名、右键只出空白菜单、指示灯不再刷新"。Loaded 补挂修掉。
        Loaded += (_, _) => SyncItem(DataContext as BoardItem, animate: false);
        Unloaded += (_, _) => DetachItem();
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        ApplySettings();
    }

    public BoardItem? Item => _item;

    /// <summary>当前是否处于"已显形"（非 hoverReveal 恒 true）。悬停文件名提示据此判定：
    /// hoverReveal 下未显形的图标不出提示（2026-10-04 错误修复③/④）。</summary>
    public bool IsRevealed => !_revealMode || _revealed;

    /// <summary>图标可视区（含遮罩/位图）。BoardWindow 的弹跳幽灵曾用它定位（2026-10-02 弹跳已废）。</summary>
    public FrameworkElement IconVisual => IconArea;

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => SyncItem(e.NewValue as BoardItem, animate: true);

    /// <summary>绑定/解绑项（DataContextChanged 与 Loaded 两路共用）。同项重复调用无副作用
    /// （不重播入场动画、不重复订阅）。</summary>
    void SyncItem(BoardItem? item, bool animate)
    {
        if (ReferenceEquals(_item, item))
        {
            if (item != null) UpdateFromItem(); // 补刷（重挂后名称/指示灯/图标状态可能已变）
            return;
        }
        DetachItem();
        _item = item;
        if (_item == null) return;
        _item.PropertyChanged += OnItemPropertyChanged;
        UpdateFromItem();
        if (!animate)
            return;
        if (_item.JellyPending)
        {
            _item.JellyPending = false; // 一次性标志：消费即清（清单三·任务12）
            PlayJellyAppear();
        }
        else
            PlayAppear(); // 新项入场（2026-08-30 批次十）：容器新生成/换绑时淡入+微放大
    }

    void DetachItem()
    {
        if (_dotsActive)
        {
            _dotsActive = false;
            StopDotLoop(); // 容器被回收：停掉 Forever 循环动画，别留在渲染线程上
        }
        _dotTimeout.Stop();
        if (_item != null)
            _item.PropertyChanged -= OnItemPropertyChanged;
        _item = null;
    }

    void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // IconVersion（07 §4 渐次回填）：缓存路径不变但文件刚就绪 → 也要重载
        if (e.PropertyName is nameof(BoardItem.Name) or nameof(BoardItem.IconPath)
            or nameof(BoardItem.IconVersion) or nameof(BoardItem.IsRunning))
            Dispatcher.BeginInvoke(UpdateFromItem);
    }

    // ---- 外观装配 ----

    /// <summary>网格参数/主题实时刷新（BoardWindow 在设置键变更时逐容器调用，步骤 3.1）。</summary>
    public void ApplySettings()
    {
        double iconSize = Math.Clamp(SettingsStore.Instance.GetDouble("display.iconSize", 67.1), 32, 96);
        bool labels = SettingsStore.Instance.GetBool("display.showLabels", false); // 默认不显示文件名（2026-08-29）
        bool mask = SettingsStore.Instance.GetBool("advanced.iconMask", true);
        _dark = ThemeResolver.IsDark();

        Root.Width = iconSize;
        // 高度=顶距 6 + 图标 + 指示灯预留条 8 + 文件名预留（2026-10-05 清单05任务4：指示灯常驻
        // 图标与名称之间的固定条，条高与 showLabels 无关——文件名显示/不显示都不改变它的位置）
        Root.Height = TopPad + iconSize + IndicatorStrip + LabelSpace(labels);
        IconArea.Width = iconSize;
        IconArea.Height = iconSize;

        // 遮罩（2026-10-01 重做）：主题色圆角底（亮=透明深灰 / 暗=透白）；图标自身剪影为规则的
        // 满铺方形/四边磨圆形时不加遮罩（满铺方形改为内缩+磨圆呈现，见 UpdateMaskPlate）
        _maskEnabled = mask;
        UpdateMaskPlate(iconSize);

        // 运行指示灯（短长条）：配色随主题（亮=暗灰 / 暗=透白），开关走 advanced.runningIndicator
        _indicatorEnabled = SettingsStore.Instance.GetBool("advanced.runningIndicator", true);
        var indicatorBrush = Freeze(_dark ? "#B8FFFFFF" : "#FF6E6E6E");
        Indicator.Background = indicatorBrush;
        Dot0.Fill = indicatorBrush; // 打开三点色=指示灯色（清单05任务5）
        Dot1.Fill = indicatorBrush;
        Dot2.Fill = indicatorBrush;
        UpdateIndicatorVisibility();

        NameText.Visibility = labels ? Visibility.Visible : Visibility.Collapsed;
        NameText.Foreground = new SolidColorBrush(_dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1B));
        NameText.Effect = null; // 透明主题已删除（2026-10-06）：不再需要"板底近乎全透时给文件名加重投影"
        ConfigureNameText(SettingsStore.Instance.GetString("display.labelLines", "two"));

        // display.iconOpacity：图标/指示灯/文件名同一不透明度口径（清单05任务2）
        _iconOpacity = Math.Clamp(SettingsStore.Instance.GetDouble("display.iconOpacity", 100), 0, 100) / 100.0;
        // hoverReveal（2026-10-02 重做）：可见性由指针圆形 OpacityMask 接管，Opacity 恒=配置值；
        // 非 hoverReveal：清除遮罩直接落定
        _revealMode = string.Equals(
            SettingsStore.Instance.GetString("display.iconStyle", "original"), "hoverReveal", StringComparison.OrdinalIgnoreCase);
        if (_revealMode)
        {
            IconArea.BeginAnimation(OpacityProperty, null);
            IconArea.Opacity = _iconOpacity;
            IconArea.OpacityMask = HiddenMask; // 初始全隐，指针进入半径圈才渐显
            _revealed = false;
        }
        else
        {
            _revealed = true;
            IconArea.BeginAnimation(OpacityProperty, null);
            IconArea.OpacityMask = null;
            IconArea.Opacity = _iconOpacity;
        }
        ApplyRevealVisibility(); // 指示灯/文件名一并随圆圈显隐（2026-10-04 错误修复③）

        UpdateFromItem();
    }

    public const double IndicatorStrip = 8; // 指示灯/打开三点预留条高度（与 Root.Height/BoardWindow.Relayout 口径一致）

    /// <summary>图标上顶距（与 XAML Root 第一行 6 同口径）：拖入文件夹的中心圈定位共用
    /// （2026-10-05 清单06任务3），别处手写 6 一律改用本常量。</summary>
    public const double TopPad = 6;

    /// <summary>文件名行高（与 XAML NameText.LineHeight 一致）。</summary>
    const double LabelLineHeight = 16;
    /// <summary>文件名与图标间距（与 XAML NameText.Margin.Top 一致）。</summary>
    const double LabelMarginTop = 3;

    /// <summary>文件名预留高度（Root.Height 与 BoardWindow.Relayout 共用口径，2026-10-03）：
    /// 显示时一行=19、两行=35、三行=51（行高 16 + 上间距 3），不显示=0。
    /// 2026-10-04 错误修复⑥：full 档由"不限高"改为真正的 3 行——预留高度必须等于实际可渲染行数×行高，
    /// 否则第三行溢出格位被下一行图标盖住（旧 35 只够两行）。</summary>
    public static double LabelSpace(bool labels)
    {
        if (!labels) return 0;
        return SettingsStore.Instance.GetString("display.labelLines", "two").Trim().ToLowerInvariant() switch
        {
            "two" => LabelMarginTop + 2 * LabelLineHeight,   // 35
            "full" => LabelMarginTop + 3 * LabelLineHeight,  // 51
            _ => LabelMarginTop + LabelLineHeight,           // 19
        };
    }

    /// <summary>文件名行数模式（display.labelLines，2026-10-03）：
    /// one=一行省略；two=两行省略；full=3 行文字（自动换行，超三行末行省略；
    /// 快捷方式/文件夹不显示后缀，与 DisplayName 口径一致）。</summary>
    void ConfigureNameText(string mode)
    {
        switch (mode)
        {
            case "full":
                // 3 行文字（2026-10-04 错误修复⑥）：MaxHeight=3×行高，配合 LabelSpace(full)=51 的格位预留，
                // 第三行完整落在格内、不被下方图标裁切；超出三行在末行省略号收尾
                NameText.TextWrapping = TextWrapping.Wrap;
                NameText.TextTrimming = TextTrimming.CharacterEllipsis;
                NameText.MaxHeight = 3 * LabelLineHeight;
                break;
            case "two":
                NameText.TextWrapping = TextWrapping.Wrap;
                NameText.TextTrimming = TextTrimming.CharacterEllipsis;
                NameText.MaxHeight = 2 * LabelLineHeight;
                break;
            default: // one
                NameText.TextWrapping = TextWrapping.NoWrap;
                NameText.TextTrimming = TextTrimming.CharacterEllipsis;
                NameText.MaxHeight = LabelLineHeight;
                break;
        }
    }

    string? _lastIconPath; // 路径未变不重新解码（内存优化：ApplySettings/轮询重刷零重复解码）
    int _lastIconVersion = -1; // 渐次回填版本（07 §4）：路径不变、图标刚提完时靠它触发重载
    bool _maskEnabled;     // 遮罩开关（advanced.iconMask 快照；实际显示还取决于图标形状，见 UpdateMaskPlate）

    /// <summary>图标剪影分级 + 遮罩/磨圆装配：方形=内缩磨圆；磨圆/圆形与异形按遮罩开关走。
    /// （2026-10-04 任务4 的"满铺重组/瓷砖缩小"方案当日经用户裁决撤销，恢复 2026-10-02 口径。）</summary>
    void UpdateMaskPlate(double iconSize)
    {
        var kind = GetShapeKind(_lastIconPath);
        bool show = _maskEnabled && kind is IconShapeKind.Irregular or IconShapeKind.Unknown;
        MaskPlate.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MaskPlate.CornerRadius = new CornerRadius(iconSize * 0.25);
        MaskPlate.Background = Freeze(_dark ? "#40FFFFFF" : "#334A4A4A"); // 暗=透白 / 亮=透明深灰

        double margin = kind switch
        {
            IconShapeKind.Square => iconSize * 0.05,               // 满铺方形：内缩 5%（"调整合适大小"）
            _ => show ? iconSize * 0.12 : 0,                       // 遮罩生效时图标内缩 12% 居中【3.4】
        };
        IconImage.Margin = new Thickness(margin);

        // 满铺方形磨圆（2026-10-02，"有棱有角"反馈）：圆角≈内缩后边长 24%
        double inner = iconSize - 2 * margin;
        IconImage.Clip = kind == IconShapeKind.Square && inner > 10
            ? new RectangleGeometry(new Rect(0, 0, inner, inner), inner * 0.24, inner * 0.24)
            : null;
    }

    enum IconShapeKind { Unknown, Square, Regular, Irregular }

    static readonly Dictionary<string, IconShapeKind> _shapeCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object _shapeLock = new();
    const int ShapeCacheCapacity = 256; // 上限：形状判定是纯路径派生、可随时重算（无界增长防不住）

    /// <summary>图标剪影分级（64px 缩样）：Square=满铺直角方形；Regular=磨圆方形/圆形；
    /// Irregular=异形剪影。结果随路径缓存（2026-10-02 由二值判定升级为分级）。</summary>
    static IconShapeKind GetShapeKind(string? iconPath)
    {
        if (string.IsNullOrEmpty(iconPath)) return IconShapeKind.Unknown;
        lock (_shapeLock)
        {
            if (_shapeCache.TryGetValue(iconPath, out var cached)) return cached;
        }
        var bitmap = IconBitmapCache.GetOrLoad(iconPath, 64);
        var kind = AnalyzeShape(bitmap);
        // 位图缺失（07 §4：图标仍在后台提取队列里）时不缓存——否则"Unknown"会被钉住，
        // 图标就绪后遮罩/磨圆不会按真实剪影重算。就绪后（IconVersion 触发重载）再算一次即可。
        if (bitmap != null)
        {
            lock (_shapeLock)
            {
                if (_shapeCache.Count >= ShapeCacheCapacity)
                    _shapeCache.Clear(); // 满员整清：重算只是 64px 像素扫描，代价远小于位图缓存
                _shapeCache[iconPath] = kind;
            }
        }
        return kind;
    }

    static IconShapeKind AnalyzeShape(BitmapSource? src)
    {
        if (src == null) return IconShapeKind.Unknown;
        int w = src.PixelWidth, h = src.PixelHeight;
        if (w < 8 || h < 8) return IconShapeKind.Unknown;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);
        bool Opaque(int x, int y) => px[(y * w + x) * 4 + 3] >= 160;

        int inset = Math.Max(1, Math.Min(w, h) / 16);
        int corners = (Opaque(inset, inset) ? 1 : 0) + (Opaque(w - 1 - inset, inset) ? 1 : 0)
                    + (Opaque(inset, h - 1 - inset) ? 1 : 0) + (Opaque(w - 1 - inset, h - 1 - inset) ? 1 : 0);
        if (corners == 4) return IconShapeKind.Square;  // 满铺直角方形：内缩+磨圆呈现
        if (corners != 0) return IconShapeKind.Irregular; // 角半实半空 → 不规则剪影（企鹅、异形等）

        // 四边各采 9 点，≥7 点不透明视为该边"实"——磨圆方形/圆形角空而边实
        bool EdgeSolid(Func<int, (int X, int Y)> at)
        {
            int solid = 0;
            for (int i = 0; i < 9; i++)
                if (Opaque(at(i).X, at(i).Y)) solid++;
            return solid >= 7;
        }
        return EdgeSolid(i => (inset + (w - 1 - 2 * inset) * i / 8, inset))
            && EdgeSolid(i => (inset + (w - 1 - 2 * inset) * i / 8, h - 1 - inset))
            && EdgeSolid(i => (inset, inset + (h - 1 - 2 * inset) * i / 8))
            && EdgeSolid(i => (w - 1 - inset, inset + (h - 1 - 2 * inset) * i / 8))
            ? IconShapeKind.Regular
            : IconShapeKind.Irregular;
    }

    void UpdateFromItem()
    {
        var item = _item;
        if (item == null) return;
        NameText.Text = item.DisplayName;   // 快捷方式不显示扩展名（2026-08-29 批次六）
        UpdateIndicatorVisibility();
        if (!string.Equals(item.IconPath, _lastIconPath, StringComparison.OrdinalIgnoreCase)
            || item.IconVersion != _lastIconVersion)
            LoadIcon(item.IconPath, item.IconVersion);
        // 打开完成（IsRunning 转亮）→ 三点汇聚成指示灯（清单05任务5）
        if (_dotsActive && item.IsRunning)
            MergeDots();
    }

    /// <summary>指示灯显隐：开关注册 × 该项运行中 × 打开三点未在循环（三点期间让位给三点）。
    /// 2026-10-05 清单05任务4：灯落在图标与名称之间的固定预留条，与文件名显隐无关。</summary>
    void UpdateIndicatorVisibility()
    {
        bool show = _indicatorEnabled && _item is { IsRunning: true } && !_dotsActive;
        Indicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void LoadIcon(string iconPath, int iconVersion)
    {
        _lastIconPath = iconPath;
        _lastIconVersion = iconVersion;
        double iconSize = Math.Clamp(SettingsStore.Instance.GetDouble("display.iconSize", 67.1), 32, 96);
        if (string.IsNullOrEmpty(iconPath))
        {
            IconImage.Source = null;
            UpdateMaskPlate(iconSize);
            return;
        }
        // 解码尺寸跟随显示尺寸 2 倍（首开卡顿治理，批次七/八）：默认 56px 图标解码 112px，
        // 相比 256px 原图省 ~5 倍解码开销；位图经 IconBitmapCache 全局共享（同路径单实例）
        IconImage.Source = IconBitmapCache.GetOrLoad(iconPath, (int)Math.Round(iconSize * 2));
        UpdateMaskPlate(iconSize); // 图标换了 → 形状判定跟着刷新（遮罩/磨圆是否需要）
    }

    // ---- hover（3.2 / 3.3；批次十二修订 B，用户裁决：触发后完整播放，播放中忽略二次触发）----

    readonly HoverCycle _jumpCycle = new();

    void OnMouseEnter(object sender, MouseEventArgs e) => _jumpCycle.OnEnter();

    void OnMouseLeave(object sender, MouseEventArgs e) => _jumpCycle.OnLeave();

    // ---- hoverReveal（3.5；2026-10-02 重做）----
    // 不再整枚图标渐显：以指针为中心、revealRadius 为半径的圆内才显示内容，圆缘渐隐=虚化裁切。
    // 实现=IconArea 的径向 OpacityMask（Absolute 映射，中心=指针在图标区内坐标）。
    // 2026-10-04 错误修复③：指示灯与下方文件名同样属于"该图标的内容"，一并随圆圈显隐
    // （此前只遮 IconArea，非悬停态下指示灯/文件名仍然可见）。

    static readonly Brush HiddenMask = Freeze(Colors.Transparent); // 未命中圆圈=全隐
    readonly RadialGradientBrush _revealBrush = new()
    {
        MappingMode = BrushMappingMode.Absolute,
        GradientStops =
        {
            new GradientStop(Colors.White, 0),
            new GradientStop(Colors.White, 0.6),
            new GradientStop(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0.85), // 圆缘渐隐（虚化）
            new GradientStop(Colors.Transparent, 1),
        },
    };

    /// <summary>更新圆形渐显状态（BoardWindow 按指针位置逐项调用；revealed=false 或指针移动都要刷新）。
    /// 短路（2026-10-05 性能）：BoardWindow 在每次 MouseMove 对全部图标调用本方法，
    /// 圆心/半径完全未变（或已是全隐）时跳过画刷突变与遮罩重设——省掉逐帧无谓重渲染。</summary>
    public void SetReveal(bool revealed, Point pointerInIcon, double radius)
    {
        bool wasRevealed = _revealed;
        _revealed = revealed;
        if (!_revealMode) return;
        if (revealed)
        {
            if (wasRevealed && _revealCenterSet
                && _revealCenter == pointerInIcon && _revealRadius == radius
                && ReferenceEquals(IconArea.OpacityMask, _revealBrush))
                return; // 完全没变：无需重设
            _revealCenter = pointerInIcon;
            _revealCenterSet = true;
            _revealRadius = radius;
            _revealBrush.Center = pointerInIcon;
            _revealBrush.GradientOrigin = pointerInIcon;
            _revealBrush.RadiusX = radius;
            _revealBrush.RadiusY = radius;
            IconArea.OpacityMask = _revealBrush;
        }
        else
        {
            _revealCenterSet = false;
            if (!wasRevealed && ReferenceEquals(IconArea.OpacityMask, HiddenMask))
                return; // 已经全隐：不重复赋值
            IconArea.OpacityMask = HiddenMask;
        }
        ApplyRevealVisibility(); // 指示灯/下方文件名与图标同显隐（错误修复③）
    }

    /// <summary>hoverReveal 下把"指示灯 + 下方文件名"并入圆圈显隐（2026-10-04 错误修复③）：
    /// 非悬停（未命中圆圈）时 Opacity=0，悬停渐显后恢复 1。非 hoverReveal 模式恒 1。
    /// 用 Opacity 而非 Visibility：Visibility 由各自的设置开关（runningIndicator / showLabels）掌管，
    /// 两层条件叠加互不打架。2026-10-05 清单05任务2：再乘 display.iconOpacity——
    /// "图标不透明度"对图标/指示灯/文件名是同一个口径。</summary>
    void ApplyRevealVisibility()
    {
        double o = (!_revealMode || _revealed ? 1 : 0) * _iconOpacity;
        Indicator.Opacity = o;
        DotsHost.Opacity = _dotsActive ? 1 : o; // 三点循环期间不受 reveal 压制（打开中必然可见）
        NameText.Opacity = o;
    }

    // ---- 打开动画（2026-10-05 清单05任务5：两点并存）----
    // ① PlayOpenPop：图标短缩弹一下（点击即时反馈，1→0.8→1）；
    // ② PlayOpening：指示灯位出现 3 个 4px 点，相位错开往复闪烁，直到该项 IsRunning 转亮
    //    （PollRunning 驱动）=程序/文件/文件夹打开完成 → 三点向中心汇聚淡出、合并为常亮指示灯；
    //    打开失败（FileOps.Open 返回 false）→ AbortOpening。
    // 两者动画对象互不重叠（Squash 在图标宿主、三点在指示灯条），由 BoardWindow.OpenItem 一并触发。

    /// <summary>打开时的短缩弹一下（重复调用幂等接续；Squash 挂在 JumpHost，指示灯/三点不随动）。
    /// 方向=先向中心快速缩小（谷值 OpenPopScaleMin）再复原。</summary>
    public void PlayOpenPop()
    {
        double t = AnimTuning.OpenPopMs;
        var keys = new (double, double, EaseStyle)[]
        {
            (1, 0, EaseStyle.EaseOutQuad),
            (AnimTuning.OpenPopScaleMin, t * 0.4, EaseStyle.EaseOutQuad),
            (1, t, EaseStyle.EaseInOutQuad),
        };
        Anim.RunKeys(Squash, ScaleTransform.ScaleXProperty, keys);
        Anim.RunKeys(Squash, ScaleTransform.ScaleYProperty, keys);
    }

    /// <summary>开始三点循环（重复调用幂等：已在循环则不动）。已在运行中的项立即合并
    /// （无需等待，避免"点已运行的软件"永远停在三点）。</summary>
    public void PlayOpening()
    {
        var item = _item;
        if (item == null || _dotsActive) return;
        _dotsActive = true;
        Dot0Shift.X = 0;
        Dot2Shift.X = 0;
        DotsHost.BeginAnimation(OpacityProperty, null);
        DotsHost.Opacity = 1;
        DotsHost.Visibility = Visibility.Visible;
        UpdateIndicatorVisibility(); // 三点期间让位给三点
        StartDotLoop();
        _dotTimeout.Stop();
        _dotTimeout.Start(); // 超时兜底（见构造器 Tick）
        ApplyRevealVisibility();
        Logger.Info($"打开三点：{item.Name} 开始循环（等 IsRunning 转亮合并）");
        if (item.IsRunning)
            MergeDots(); // 已在运行：直接合并
    }

    /// <summary>三点循环：三枚点各自透明度 0.25↔1 往复闪烁，相邻相位错开 OpenDotsPhase。</summary>
    void StartDotLoop()
    {
        var dots = new[] { Dot0, Dot1, Dot2 };
        for (int i = 0; i < dots.Length; i++)
        {
            var a = new DoubleAnimation
            {
                From = 0.25,
                To = 1,
                Duration = Anim.Ms(AnimTuning.OpenDotsPulse),
                BeginTime = Anim.Ms(AnimTuning.OpenDotsPhase * i),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Cship.Ui.Animations.AnimationClock.Apply(a);
            dots[i].BeginAnimation(OpacityProperty, a);
        }
    }

    /// <summary>打开完成：三点向中心汇聚 + 淡出，同时指示灯点亮（DotsMerge）。</summary>
    public void MergeDots()
    {
        if (!_dotsActive) return;
        _dotsActive = false;
        _dotTimeout.Stop();
        StopDotLoop();
        UpdateIndicatorVisibility();
        Logger.Info($"打开三点：{_item?.Name} 合并为指示灯");
        double t = AnimTuning.DotsMerge;
        Anim.Run(DotsHost, OpacityProperty, null, 0, t, EaseStyle.EaseInQuad, onDone: () =>
        {
            DotsHost.Visibility = Visibility.Collapsed;
            DotsHost.BeginAnimation(OpacityProperty, null);
            DotsHost.Opacity = 1;
            Dot0Shift.X = 0;
            Dot2Shift.X = 0;
            ApplyRevealVisibility();
        });
        Anim.Run(Dot0Shift, TranslateTransform.XProperty, null, 8, t, EaseStyle.EaseInQuad);
        Anim.Run(Dot2Shift, TranslateTransform.XProperty, null, -8, t, EaseStyle.EaseInQuad);
        ApplyRevealVisibility();
    }

    /// <summary>打开失败/被取消：停循环，三点淡出，恢复指示灯状态。</summary>
    public void AbortOpening()
    {
        if (!_dotsActive) return;
        _dotsActive = false;
        _dotTimeout.Stop();
        StopDotLoop();
        Anim.Run(DotsHost, OpacityProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInQuad, onDone: () =>
        {
            DotsHost.Visibility = Visibility.Collapsed;
            DotsHost.BeginAnimation(OpacityProperty, null);
            DotsHost.Opacity = 1;
            ApplyRevealVisibility();
        });
        UpdateIndicatorVisibility();
        ApplyRevealVisibility();
    }

    void StopDotLoop()
    {
        foreach (var dot in new[] { Dot0, Dot1, Dot2 })
        {
            dot.BeginAnimation(OpacityProperty, null);
            dot.Opacity = 1;
        }
    }

    /// <summary>拖入垃圾桶后的缩小淡出（AnimTuning.DeleteFade，步骤 5）。onDone 由 X 轴动画收尾时触发。</summary>
    public void PlayDeleteFade(Action? onDone)
    {
        Anim.Run(Squash, ScaleTransform.ScaleXProperty, null, 0, AnimTuning.DeleteFade, HoverEase, onDone);
        Anim.Run(Squash, ScaleTransform.ScaleYProperty, null, 0, AnimTuning.DeleteFade, HoverEase);
        Anim.Run(this, OpacityProperty, null, 0, AnimTuning.DeleteFade, HoverEase);
    }

    /// <summary>入场动画（2026-08-30 批次十）：新文件出现在收纳板时淡入+自 0.85 微放大（AnimTuning.Micro）。
    /// 仅在容器新生成/换绑（DataContext 变化）时播；Reconcile 复用实例不换绑，不会误触。</summary>
    public void PlayAppear()
    {
        Anim.Run(this, OpacityProperty, 0, 1, AnimTuning.Micro, HoverEase);
        Anim.Run(Squash, ScaleTransform.ScaleXProperty, 0.85, 1, AnimTuning.Micro, HoverEase);
        Anim.Run(Squash, ScaleTransform.ScaleYProperty, 0.85, 1, AnimTuning.Micro, HoverEase);
    }

    /// <summary>渐变果冻入场（清单三·任务12，刷新重建专用）：Squash 缩放 0.7→过冲/下压→1 关键帧
    /// + 淡入（AnimTuning.JellyAppear）。X 轴过冲 1.08、Y 轴反向压到 0.92，果冻挤压感。</summary>
    public void PlayJellyAppear()
    {
        double t = AnimTuning.JellyAppear;
        Anim.RunKeys(Squash, ScaleTransform.ScaleXProperty, new[]
        {
            (0.7, 0.0, EaseStyle.EaseOutQuad),
            (1.08, t * 0.65, EaseStyle.EaseOutQuad),
            (1.0, t, EaseStyle.EaseInOutQuad),
        });
        Anim.RunKeys(Squash, ScaleTransform.ScaleYProperty, new[]
        {
            (0.7, 0.0, EaseStyle.EaseOutQuad),
            (0.92, t * 0.65, EaseStyle.EaseOutQuad),
            (1.0, t, EaseStyle.EaseInOutQuad),
        });
        Anim.Run(this, OpacityProperty, 0, 1, t, EaseStyle.EaseOutQuad);
    }

    // ---- 内联重命名（4.3）：图标处 TextBox，回车确认、Esc 取消、失焦确认 ----

    public void StartRename(Action<bool, string> finished)
    {
        var item = _item;
        if (item == null || _renameActive) return;
        _renameActive = true;
        NameText.Visibility = Visibility.Collapsed;
        RenameBox.Text = item.Name;
        RenameBox.Visibility = Visibility.Visible;
        RenameBox.Focus();
        RenameBox.SelectAll();

        void End(bool success)
        {
            if (!_renameActive) return;
            _renameActive = false;
            RenameBox.Visibility = Visibility.Collapsed;
            NameText.Visibility = SettingsStore.Instance.GetBool("display.showLabels", false) // 默认不显示文件名（2026-08-29）
                ? Visibility.Visible : Visibility.Collapsed;
            RenameBox.KeyDown -= OnKey;
            RenameBox.LostFocus -= OnLostFocus;
            finished(success, RenameBox.Text);
        }

        void OnKey(object s, KeyEventArgs e)
        {
            e.Handled = true;
            if (e.Key == Key.Enter) End(true);
            else if (e.Key == Key.Escape) End(false);
        }
        void OnLostFocus(object s, RoutedEventArgs e) => End(true);

        RenameBox.KeyDown += OnKey;
        RenameBox.LostFocus += OnLostFocus;
    }

    public bool RenameActive => _renameActive;

    static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
