using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cship.Core;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 三层方板组件（步骤 4.2 + 2026-08-28 用户修订的消散编排）：
/// - Offset 为相邻板错位（静置 6px，悬停 10px），上/中/下板分别钉在 BaseMargin+{0,o,2o}；
/// - 消散：中层停留原地，上层板向左上、下层板向右下各向外错开 DissolveSpread 并缩至一半，
///   三板同步放缓淡出（用户要求视觉上非瞬间消失）；复现为其逆过程。
/// **层名口径**：本组件内部只用**绘制序** front(左上，画在最上) / mid(中) / back(右下，画在最下)；
/// 设置页的"上/中/下"是**界面层序**，由 <see cref="Cship.Ui.FloatingDock"/> 对调后再进来
/// （2026-10-06 用户口径：视觉上"右下角那一层"才是上层，故 UI 上=back、UI 下=front）。
/// **材质来源（步骤 06 §4）**：三板底色由 <see cref="MaterialEngine.Fill"/> 提供（三档材质预设 +
/// 透明档），噪点层不透明度随材质档；三层错位/hover/消散编排与既有 API **一律不动**。
/// </summary>
public partial class SquarePlate : UserControl
{
    public static readonly DependencyProperty PlateSizeProperty = DependencyProperty.Register(
        nameof(PlateSize), typeof(double), typeof(SquarePlate),
        new PropertyMetadata(56.0, OnPlateSizeChanged));

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(SquarePlate),
        new PropertyMetadata(RestOffset, OnOffsetChanged));

    /// <summary>静置错位（00 §7：三板错位 6px）。</summary>
    public const double RestOffset = 6.0;

    /// <summary>悬停最大错位（悬停动画目标，00 §7）。</summary>
    public const double HoverOffset = 10.0;

    /// <summary>画布基础边距：给上层板向左上错开预留的行程。</summary>
    public const double BaseMargin = 24.0;

    /// <summary>消散时上/下板向外错开的距离。</summary>
    public const double DissolveSpread = 24.0;

    /// <summary>窗口/画布在板边长之外的预留总量 = 2×BaseMargin + 2×HoverOffset + DissolveSpread。</summary>
    public const double Chrome = 72.0;

    /// <summary>消散时上/下板的缩放终值。</summary>
    internal const double DissolveScale = 0.5;

    string _material = MaterialEngine.Blur; // personal.dockCustom.material：acrylic|blur|liquidGlass（默认"柔化"）
    bool _dark;

    /// <summary>底面材质预设（悬浮窗自定义·预设模式）：三档材质由 MaterialEngine 提供配方（步骤 06 §4）。</summary>
    public string Material
    {
        get => _material;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? MaterialEngine.Blur : value;
            if (string.Equals(_material, next, StringComparison.OrdinalIgnoreCase)) return;
            _material = next;
            ApplyPlateFill();
        }
    }

    /// <summary>单板边长（display.dockSize，默认 56，范围 40~96）。</summary>
    public double PlateSize
    {
        get => (double)GetValue(PlateSizeProperty);
        set => SetValue(PlateSizeProperty, value);
    }

    /// <summary>相邻两板错位间距（悬停 10，动画目标）。</summary>
    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public SquarePlate()
    {
        InitializeComponent();
        foreach (var noise in new[] { NoiseBack, NoiseMid, NoiseFront })
            noise.Background = MaterialEngine.Noise();
        ApplySize();
    }

    /// <summary>亮/暗主题切换板底色（theme.mode 经 ThemeResolver 折算）。
    /// personal.dockCustom 设置了层图片时以其覆盖（步骤 05 悬浮窗自定义）。</summary>
    public void SetTheme(bool dark)
    {
        _dark = dark;
        ApplyPlateFill();
    }

    /// <summary>三板底色 = MaterialEngine 材质配方（无自定义层图时）；各层按
    /// personal.dockCustom.opacity 折算 α（预设模式的层不透明度，见 <see cref="SetLayerOpacities"/>）。</summary>
    void ApplyPlateFill()
    {
        double noise = MaterialEngine.NoiseOpacity(_material);
        ApplyLayer(PlateFront, NoiseFront, 0, noise);
        ApplyLayer(PlateMid, NoiseMid, 1, noise);
        ApplyLayer(PlateBack, NoiseBack, 2, noise);
    }

    /// <summary>单层外观：层图优先，其次材质配方（按该层不透明度折算 α）；噪点与内高光随该层同透明度
    /// ——否则把某层调到 0% 时画刷透了、装饰还会留下一道亮边。</summary>
    void ApplyLayer(Border plate, Border noiseLayer, int index, double noise)
    {
        double op = _layerOpacity[index];
        plate.Background = CustomFill(index) ?? MaterialEngine.Fill(_material, _dark, op);
        noiseLayer.Opacity = noise;
        noiseLayer.Visibility = noise > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (plate.Child is Grid content) content.Opacity = op / 100.0;
    }

    Brush?[] _customFills = { null, null, null }; // [front, mid, back]

    /// <summary>各层不透明度（personal.dockCustom.opacity，[上,中,下]，0~100%）。
    /// **2026-10-06 用户要求**：这一组调整对**预设模式同样生效**——层底取
    /// <see cref="MaterialEngine.Fill"/> 时按本值折算 α，配方本身（原始预设）不被改写。
    /// 之所以折进画刷而不动 Border.Opacity：三板的 Opacity 归消散/复现动画所有，改基值会被动画顶掉。</summary>
    double[] _layerOpacity = { 100, 100, 100 };

    Brush? CustomFill(int layer) => _customFills[layer];

    /// <summary>
    /// 悬浮窗自定义三层外观（步骤 05 §3.4）：brush 非空=覆盖对应层底色
    /// （纯色或 cover 图片画刷，圆角由 Border 自带裁剪）；null=回主题色。
    /// layers = [上(front), 中(mid), 下(back)]。
    /// </summary>
    public void ApplyCustomLayers(Brush?[] layers)
    {
        _customFills = layers.Length == 3 ? layers : new Brush?[] { null, null, null };
        ApplyPlateFill();
    }

    /// <summary>各层不透明度（[上,中,下]，0~100%）。预设模式的层底与噪点/内高光都按本值折算；
    /// 自定义模式的层图不透明度由 FloatingDock 设在 ImageBrush 上（同一组值，不重复折算）。</summary>
    public void SetLayerOpacities(double[] percents)
    {
        if (percents.Length != 3) return;
        _layerOpacity = new[]
        {
            Math.Clamp(percents[0], 0, 100),
            Math.Clamp(percents[1], 0, 100),
            Math.Clamp(percents[2], 0, 100),
        };
        ApplyPlateFill();
    }

    static void OnPlateSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SquarePlate)d).ApplySize();

    void ApplySize()
    {
        var side = PlateSize;
        foreach (var plate in new[] { PlateBack, PlateMid, PlateFront })
        {
            plate.Width = side;
            plate.Height = side;
        }
        Root.Width = side + Chrome;
        Root.Height = side + Chrome;
        LayoutOffset();
    }

    static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SquarePlate)d).LayoutOffset();

    void LayoutOffset()
    {
        var o = Offset;
        Canvas.SetLeft(PlateFront, BaseMargin);
        Canvas.SetTop(PlateFront, BaseMargin);
        Canvas.SetLeft(PlateMid, BaseMargin + o);
        Canvas.SetTop(PlateMid, BaseMargin + o);
        Canvas.SetLeft(PlateBack, BaseMargin + 2 * o);
        Canvas.SetTop(PlateBack, BaseMargin + 2 * o);
    }

    /// <summary>定格当前错位（悬停/呼吸动画若在播，取当前值后停掉），供消散计算起点。</summary>
    public void FreezeOffset()
    {
        var current = Offset;
        BeginAnimation(OffsetProperty, null);
        Offset = current;
    }

    // ---- 消散 / 复现（2026-08-28 用户修订）----

    /// <summary>
    /// 消散：中层停留原地；上层板向左上、下层板向右下各向外错开 DissolveSpread 并缩至 DissolveScale；
    /// 三板同步淡出至完全透明（fadeMs 放缓，保证视觉上非瞬间消失）。淡出完成后回调（调用方 Hide 窗口）。
    /// 内部经 Anim.Run（步骤 03）：fps 自动生效，完成时动画移除+基值落定，无属性残留。
    /// </summary>
    public void PlayDissolve(double moveMs, double fadeMs, Action? completed)
    {
        FreezeOffset();
        ResetPlateVisuals();
        double o = Offset;

        Anim.Run(PlateFront, Canvas.LeftProperty, BaseMargin, BaseMargin - DissolveSpread, moveMs);
        Anim.Run(PlateFront, Canvas.TopProperty, BaseMargin, BaseMargin - DissolveSpread, moveMs);
        Anim.Run(PlateBack, Canvas.LeftProperty, BaseMargin + 2 * o, BaseMargin + 2 * o + DissolveSpread, moveMs);
        Anim.Run(PlateBack, Canvas.TopProperty, BaseMargin + 2 * o, BaseMargin + 2 * o + DissolveSpread, moveMs);
        Anim.Run(FrontScale, ScaleTransform.ScaleXProperty, 1, DissolveScale, moveMs);
        Anim.Run(FrontScale, ScaleTransform.ScaleYProperty, 1, DissolveScale, moveMs);
        Anim.Run(BackScale, ScaleTransform.ScaleXProperty, 1, DissolveScale, moveMs);
        Anim.Run(BackScale, ScaleTransform.ScaleYProperty, 1, DissolveScale, moveMs);
        // 中层：位置与缩放均保持原地不动；淡出以正层收尾（三板同 duration 同起点）
        Anim.Run(PlateFront, OpacityProperty, 1, 0, fadeMs, EaseStyle.EaseInOutQuad, completed);
        Anim.Run(PlateMid, OpacityProperty, 1, 0, fadeMs, EaseStyle.EaseInOutQuad);
        Anim.Run(PlateBack, OpacityProperty, 1, 0, fadeMs, EaseStyle.EaseInOutQuad);
    }

    /// <summary>复现：消散的逆过程，回到静置错位；指针仍悬在窗口上时 MouseEnter 会随后拉到 10。</summary>
    public void PlayRevive(double moveMs, double fadeMs, Action? completed)
    {
        double o = Offset;      // 消散时定格的错位
        double rest = RestOffset;

        Anim.Run(PlateFront, Canvas.LeftProperty, BaseMargin - DissolveSpread, BaseMargin, moveMs);
        Anim.Run(PlateFront, Canvas.TopProperty, BaseMargin - DissolveSpread, BaseMargin, moveMs);
        Anim.Run(PlateMid, Canvas.LeftProperty, BaseMargin + o, BaseMargin + rest, moveMs);
        Anim.Run(PlateMid, Canvas.TopProperty, BaseMargin + o, BaseMargin + rest, moveMs);
        Anim.Run(PlateBack, Canvas.LeftProperty, BaseMargin + 2 * o + DissolveSpread, BaseMargin + 2 * rest, moveMs);
        Anim.Run(PlateBack, Canvas.TopProperty, BaseMargin + 2 * o + DissolveSpread, BaseMargin + 2 * rest, moveMs);
        Anim.Run(FrontScale, ScaleTransform.ScaleXProperty, DissolveScale, 1, moveMs);
        Anim.Run(FrontScale, ScaleTransform.ScaleYProperty, DissolveScale, 1, moveMs);
        Anim.Run(BackScale, ScaleTransform.ScaleXProperty, DissolveScale, 1, moveMs);
        Anim.Run(BackScale, ScaleTransform.ScaleYProperty, DissolveScale, 1, moveMs);

        Anim.Run(PlateFront, OpacityProperty, 0, 1, fadeMs, EaseStyle.EaseInOutQuad, () =>
        {
            FinalizeRest();
            completed?.Invoke();
        });
        Anim.Run(PlateMid, OpacityProperty, 0, 1, fadeMs, EaseStyle.EaseInOutQuad);
        Anim.Run(PlateBack, OpacityProperty, 0, 1, fadeMs, EaseStyle.EaseInOutQuad);
    }

    /// <summary>消散开始前：清掉三板残留动画并把基值按当前 Offset 重排（起点=当前观感）。</summary>
    void ResetPlateVisuals()
    {
        foreach (var plate in new[] { PlateFront, PlateMid, PlateBack })
        {
            plate.BeginAnimation(Canvas.LeftProperty, null);
            plate.BeginAnimation(Canvas.TopProperty, null);
            plate.BeginAnimation(OpacityProperty, null);
            plate.Opacity = 1;
            var scale = plate.RenderTransform as ScaleTransform;
            if (scale != null)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }
        LayoutOffset();
    }

    /// <summary>复现完成后：移除全部板级动画，基值落回静置布局（错位 6）。</summary>
    void FinalizeRest()
    {
        BeginAnimation(OffsetProperty, null);
        Offset = RestOffset;
        foreach (var plate in new[] { PlateFront, PlateMid, PlateBack })
        {
            plate.BeginAnimation(Canvas.LeftProperty, null);
            plate.BeginAnimation(Canvas.TopProperty, null);
            plate.BeginAnimation(OpacityProperty, null);
            plate.Opacity = 1;
            var scale = plate.RenderTransform as ScaleTransform;
            if (scale != null)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }
        LayoutOffset();
    }

}
