using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Cship.Core;

namespace Cship.Ui.Components;

/// <summary>
/// 玻璃窗口材质层（步骤 06；**2026-10-06 改为"图层堆叠"口径**）。
///
/// 本层只做**静态图层**：主题底色（含个人不透明度折算）→ 噪点 → 液态玻璃的边缘渐深/顶部镜面/
/// 底部内阴影/内缘高光 → 发丝描边，全部是 WPF 画刷，**不含任何抓屏、位图、模糊着色器**。
///
/// **当前状态**：**收纳板启用系统模糊**（`BoardWindow` 里 `Material.UseNativeBlur = true`）。
/// 宿主置真后，本层在窗口可见时向 DWM 申请 `ACCENT_ENABLE_BLURBEHIND`，模糊由 DWM 实时合成——
/// 延迟为 0、本进程零开销、移动/缩放/动画期间什么都不用做。
/// 同时 **四角羽化补丁**（<see cref="RebuildCornerVeil"/>）自动接管：系统模糊形状裁不住
/// （`SetWindowRgn` 三种设法都试过、`DwmEnableBlurBehindWindow` 在 Win10 已不出模糊、
/// DWM 圆角偏好本机不支持，见 `build/_nattest2`），故在圆角外那圈**月牙**上补一层板底色，
/// 用径向渐变从"圆弧处=板内强度"渐隐到"角落=0"，把硬棱角磨成柔和过渡。
/// **关掉模糊则四角回到逐像素干净**（补丁随之静默）——用户在这两个方向之间来回过一次，只差那一行。
///
/// **能力边界**：系统模糊只适合"整个窗口矩形都该是玻璃"的窗口。悬浮窗三板（有空隙）、
/// 设置窗（板比窗口小一圈，四周留投影位）从来不开——它们的板底本身 90%+ 不透明，
/// 少那一层背后画面在观感上几乎无差别。
///
/// **既有 API 语义不变（§1.2，02/05 已在依赖）**：
/// <see cref="SetTheme"/>(bool)、<see cref="SetBoardOpacity"/>(double，只作用于板底)、
/// <see cref="SetCorners"/>(CornerRadius，方向圆角)、<see cref="PlateStroke"/>（L3 背景图内缩对齐量）、
/// <see cref="Material"/>（材质档，含"无"）。
/// </summary>
public partial class MaterialBackground : UserControl
{
    /// <summary>板底发丝描边宽度（与 Plate 的 BorderThickness 一致）。板底画刷（渐变/纯色）画在描边以内，
    /// 自定义背景图（L3）按同一内缩量对齐才不会露在描边带内（2026-10-05 清单05任务1）。</summary>
    public const double PlateStroke = 1;

    /// <summary>液态玻璃内缘高光宽度（玻璃"厚度"的主要线索）。</summary>
    const double RimThickness = 1.5;

    string _material = MaterialEngine.None;
    bool _dark;
    double _opacity = 1.0;          // personal.boardOpacity（0~100%）折算 0~1
    CornerRadius _corners = new(12);
    bool _useNativeBlur;            // 宿主是否允许用系统模糊（整个窗口矩形都该是玻璃才允许）
    bool _nativeBlurOn;             // 系统模糊**实际**是否开着（下发失败时保持 false，补丁不能画）
    Window? _owner;

    /// <summary>材质档位（personal.material：none|acrylic|blur|liquidGlass）。写入即重绘（纯画刷，无 IO）。</summary>
    public string Material
    {
        get => _material;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? MaterialEngine.None : value;
            if (string.Equals(_material, next, StringComparison.OrdinalIgnoreCase)) return;
            _material = next;
            ApplyMaterial();
        }
    }

    /// <summary>
    /// 宿主是否允许使用系统模糊（默认否）。**只有"整个窗口矩形都该是玻璃"的窗口可以置真**
    /// （收纳板）；板比窗口小一圈、或窗口里有空隙的（设置窗 / 悬浮窗）必须保持否，
    /// 否则模糊会漫到板外的透明区域上。
    /// </summary>
    public bool UseNativeBlur
    {
        get => _useNativeBlur;
        set
        {
            if (_useNativeBlur == value) return;
            _useNativeBlur = value;
            ApplyNativeBlur();
        }
    }

    public MaterialBackground()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => { ApplyClip(); RebuildCornerVeil(); };
        // 窗口 Hide→Show 后兜底重下发一次（系统若在隐藏期丢弃了强调策略，不至于此后永久失效）
        IsVisibleChanged += (_, _) => { if (IsVisible) ApplyNativeBlur(force: true); };
        ApplyMaterial();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _owner = Window.GetWindow(this);
        ApplyClip();
        ApplyNativeBlur();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var owner = _owner;
        _owner = null;
        _nativeBlurOn = false;
        if (owner != null) MaterialEngine.ClearNativeBlur(owner);
        RebuildCornerVeil();
    }

    /// <summary>亮/暗切换（ThemeResolver 折算结果）。</summary>
    public void SetTheme(bool dark)
    {
        _dark = dark;
        ApplyMaterial();
    }

    /// <summary>收纳板背景不透明度（0~100%，清单02任务7）：只作用于本纯色/渐变板底，
    /// 与自定义背景图（personal.boardBg）各自的透明度互不影响。</summary>
    public void SetBoardOpacity(double percent)
    {
        _opacity = Math.Clamp(percent, 0, 100) / 100.0;
        ApplyMaterial();
    }

    /// <summary>按弹出方向设置圆角（2026-08-29 批次四：锚边两角直角，与屏幕边缘融合）。</summary>
    public void SetCorners(CornerRadius corners)
    {
        _corners = corners;
        Plate.CornerRadius = corners;
        Rim.CornerRadius = corners;
        ApplyClip();
        RebuildCornerVeil();
    }

    /// <summary>按当前主题/材质/不透明度重建底色与装饰（纯画刷赋值，不抓屏、不建位图）。</summary>
    void ApplyMaterial()
    {
        // 板底**只画一层**：系统模糊在窗口背后，再叠一层同色底会把模糊盖到几乎不可见（见 XAML 注释）
        Plate.Background = MaterialEngine.Fill(_material, _dark, _opacity * 100);
        Plate.BorderBrush = MaterialEngine.PlateBorder(_dark);
        Plate.BorderThickness = new Thickness(PlateStroke);

        double noise = MaterialEngine.NoiseOpacity(_material);
        NoiseLayer.Background = MaterialEngine.Noise();
        NoiseLayer.Opacity = noise;
        NoiseLayer.Visibility = noise > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 液态玻璃的"玻璃厚度"四层（其余材质档全部隐去）
        bool depth = MaterialEngine.HasGlassDepth(_material);
        if (depth)
        {
            var recipe = MaterialEngine.LiquidGlassRecipe(_dark);
            Rim.BorderBrush = recipe.Rim;
            Rim.BorderThickness = new Thickness(RimThickness);
            Specular.Background = recipe.Specular;
            Vignette.Background = recipe.Vignette;
            InnerShadow.Background = recipe.InnerShadow;
        }
        double layerOpacity = depth ? 1 : 0;
        Rim.Opacity = layerOpacity;
        Specular.Opacity = layerOpacity;
        Vignette.Opacity = layerOpacity;
        InnerShadow.Opacity = layerOpacity;

        ApplyNativeBlur();
        RebuildCornerVeil();
    }

    /// <summary>
    /// 系统模糊开关：只在"宿主允许 + 材质非无 + 宿主窗口存在"时开，其余一律关
    /// （切到"无"档、或宿主不允许时立刻撤掉，不做残留）。
    /// </summary>
    void ApplyNativeBlur(bool force = false)
    {
        var owner = _owner ?? Window.GetWindow(this);
        if (owner == null) return;
        bool want = _useNativeBlur && !MaterialEngine.IsNone(_material);
        bool on = MaterialEngine.SetNativeBlur(owner, want, force && want);
        if (on == _nativeBlurOn) return;
        _nativeBlurOn = on;
        RebuildCornerVeil();
    }

    /// <summary>子元素圆角硬裁剪（底色是圆角 Border，但高光/内阴影是矩形子元素，必须裁到圆角轮廓）。</summary>
    void ApplyClip()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1) { ClipHost.Clip = null; return; }
        ClipHost.Clip = BuildRoundedGeometry(w, h, _corners);
    }

    // ---- 四角羽化补丁（磨棱角）----

    /// <summary>
    /// 在圆角外那圈"月牙"上补板底色，径向渐隐（圆弧处=板内强度 → 角落=0）。
    /// 只在系统模糊**真的开着**时画：模糊没开时圆角外本就是干净桌面，补上去反而多出一圈色。
    /// </summary>
    void RebuildCornerVeil()
    {
        CornerVeilHost.Children.Clear();
        if (!_nativeBlurOn) return;
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;
        var (top, bottom) = MaterialEngine.FillEdgeColors(_material, _dark, _opacity * 100);
        AddCornerVeil(w, h, _corners.TopLeft, top, Corner.TopLeft);
        AddCornerVeil(w, h, _corners.TopRight, top, Corner.TopRight);
        AddCornerVeil(w, h, _corners.BottomRight, bottom, Corner.BottomRight);
        AddCornerVeil(w, h, _corners.BottomLeft, bottom, Corner.BottomLeft);
    }

    enum Corner { TopLeft, TopRight, BottomRight, BottomLeft }

    void AddCornerVeil(double w, double h, double radius, Color color, Corner corner)
    {
        double r = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
        if (r < 1.5) return; // 直角或半径太小：没有可见月牙

        // 统一用"左上角月牙"作模板，靠镜像变换摆到另外三个角。
        // 径向渐变是中心对称的，镜像后观感不变，故四个角共用同一套模板坐标。
        // **画刷在 RenderTransform 之前生效**，所以圆心一律写模板坐标 (r,r)，摆位交给变换。
        Transform? transform = corner switch
        {
            Corner.TopRight => Mirror(-1, 1, w, 0),
            Corner.BottomRight => Mirror(-1, -1, w, h),
            Corner.BottomLeft => Mirror(1, -1, 0, h),
            _ => null,
        };

        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, // 圆心/半径用元素坐标（不是包围盒），才能与月牙精确对齐
            Center = new Point(r, r),
            GradientOrigin = new Point(r, r),
            RadiusX = r * Math.Sqrt(2),
            RadiusY = r * Math.Sqrt(2),
        };
        // 圆弧上（距离圆心 r = 半径的 1/√2）强度=板内板底；角落（距离 r√2）完全让位给模糊
        brush.GradientStops.Add(new GradientStop(color, 1 / Math.Sqrt(2)));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
        brush.Freeze();

        var path = new Path
        {
            Data = MoonGeometry(r),
            Fill = brush,
            IsHitTestVisible = false,
        };
        if (transform != null) path.RenderTransform = transform;
        CornerVeilHost.Children.Add(path);
    }

    /// <summary>先缩放再平移的镜像变换（点 (x,y) → (sx·x+dx, sy·y+dy)）。</summary>
    static Transform Mirror(double sx, double sy, double dx, double dy)
    {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(sx, sy));
        group.Children.Add(new TranslateTransform(dx, dy));
        group.Freeze();
        return group;
    }

    /// <summary>左上角"月牙"几何：半径 r 的角落正方形减去 1/4 圆（圆心 (r,r)）。</summary>
    static Geometry MoonGeometry(double r)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(r, 0), true, false);
            ctx.ArcTo(new Point(0, r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>支持四角不同半径的圆角矩形几何（方向圆角：锚边两角直角，直角处不加圆）。</summary>
    static Geometry BuildRoundedGeometry(double w, double h, CornerRadius c)
    {
        double tl = Math.Max(0, Math.Min(c.TopLeft, Math.Min(w, h) / 2));
        double tr = Math.Max(0, Math.Min(c.TopRight, Math.Min(w, h) / 2));
        double br = Math.Max(0, Math.Min(c.BottomRight, Math.Min(w, h) / 2));
        double bl = Math.Max(0, Math.Min(c.BottomLeft, Math.Min(w, h) / 2));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(tl, 0), true, true);
            ctx.LineTo(new Point(w - tr, 0), true, false);
            if (tr > 0) ctx.ArcTo(new Point(w, tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(w, h - br), true, false);
            if (br > 0) ctx.ArcTo(new Point(w - br, h), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(bl, h), true, false);
            if (bl > 0) ctx.ArcTo(new Point(0, h - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0, tl), true, false);
            if (tl > 0) ctx.ArcTo(new Point(tl, 0), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        return geo;
    }
}
