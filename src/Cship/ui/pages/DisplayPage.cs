using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Core;
using Cship.Ui.Animations;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 显示页（步骤 05 §3.2，原文 L35-43）：悬浮窗大小 / 图标样式 / 不透明度 / 文件名两开关 /
/// 图标大小 / 行列间距 / 显示屏幕。全部改动经 SettingsStore.Set 即时广播（※）。
/// </summary>
public class DisplayPage : SettingsPageBase
{
    SliderX _dockSize = null!;
    Segmented _iconStyle = null!;
    SliderX _opacity = null!;
    SliderX _boardOpacity = null!;
    ToggleSwitch _showLabels = null!;
    ToggleSwitch _hoverLabels = null!;
    SliderX _iconSize = null!;
    SliderX _rowSpacing = null!;
    SliderX _colSpacing = null!;
    FoldingList _screen = null!;

    public DisplayPage()
    {
        var body = PageBody();
        var section = Section("set.display");
        var rows = BodyOf(section);

        _dockSize = BuildSlider(rows, "set.dockSize", "px", 40, 96, "display.dockSize", step: 0.1);
        _iconStyle = BuildSegmented(rows, "set.iconStyle",
            new[] { "set.iconStyle.original", "set.iconStyle.hoverReveal" }, "display.iconStyle",
            new[] { "original", "hoverReveal" });
        _opacity = BuildSlider(rows, "set.iconOpacity", "%", 0, 100, "display.iconOpacity", step: 0.1);
        _boardOpacity = BuildSlider(rows, "set.boardOpacity", "%", 0, 100, "personal.boardOpacity", step: 0.1); // 板底不透明度（清单02任务7）
        _showLabels = BuildToggle(rows, "set.showLabels", "display.showLabels", BuildLabelLinesExpander());
        _hoverLabels = BuildToggle(rows, "set.hoverLabels", "display.hoverLabels");
        _iconSize = BuildSlider(rows, "set.iconSize", "px", 32, 96, "display.iconSize", step: 0.1);
        _rowSpacing = BuildSlider(rows, "set.rowSpacing", "px", 0, 32, "display.rowSpacing", step: 0.1);
        _colSpacing = BuildSlider(rows, "set.colSpacing", "px", 0, 32, "display.colSpacing", step: 0.1);
        _screen = BuildScreenList(rows);

        body.Children.Add(section);
        Content = ScrollWrap(body);
    }

    SliderX BuildSlider(StackPanel rows, string labelKey, string unit, double min, double max, string settingKey, double step = 0)
    {
        var slider = new SliderX(Dark)
        {
            Width = 190,
            Minimum = min,
            Maximum = max,
            Step = step,
            Unit = unit, // 单位随主题（SliderX 内部同排显示在数值后）
        };
        slider.Value = SettingsStore.Instance.GetDouble(settingKey, (min + max) / 2);
        slider.ValueChanged += v => SettingsStore.Instance.SetDouble(settingKey, v);
        RowRaw(rows, Label(labelKey), slider);
        return slider;
    }

    TextBlock Label(string key)
    {
        var label = new TextBlock { FontSize = 12.5, Text = I18n.Tr(key), Foreground = SettingsPalette.Text(Dark) };
        _texts.Add((label, key));
        return label;
    }

    Segmented BuildSegmented(StackPanel rows, string labelKey, string[] textKeys, string settingKey, string[] values)
    {
        var seg = new Segmented(Dark);
        var labels = new string[textKeys.Length];
        for (int i = 0; i < textKeys.Length; i++)
            labels[i] = I18n.Tr(textKeys[i]);
        seg.SetOptions(labels);
        string current = SettingsStore.Instance.GetString(settingKey, values[0]);
        int idx = Array.IndexOf(values, current);
        seg.SetSelected(idx < 0 ? 0 : idx, syncToUi: false);
        seg.SelectedChanged += i => SettingsStore.Instance.SetString(settingKey, values[i]);
        RowRaw(rows, Label(labelKey), seg);
        _segmentLabels[settingKey] = textKeys;
        _segments[settingKey] = (seg, values);
        return seg;
    }

    readonly Dictionary<string, (Segmented Seg, string[] Values)> _segments = new();
    readonly Dictionary<string, string[]> _segmentLabels = new();

    ToggleSwitch BuildToggle(StackPanel rows, string labelKey, string settingKey, FrameworkElement? extra = null)
    {
        var toggle = new ToggleSwitch { IsChecked = SettingsStore.Instance.GetBool(settingKey, false) };
        toggle.CheckedChanged += on => SettingsStore.Instance.SetBool(settingKey, on);
        FrameworkElement control = toggle;
        if (extra != null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(toggle);
            panel.Children.Add(extra);
            control = panel;
        }
        Row(rows, labelKey, control);
        return toggle;
    }

    // ---- 文件名行数展开卡（2026-10-03）：开关旁小展开按钮 → 展开卡内不连续滑块（三档）----
    // 前区=一行文字、中区=两行文字、后区=3行文字（2026-10-04 错误修复⑥：第三行完整落在格位内，不再被下方图标裁切）。
    // 2026-10-04（清单01任务12/13）：展开卡经 ExpanderOverlay 渲染进设置窗自身（不再 Popup），
    // 点击卡外任意区域=带动画关闭；卡为"开一次建一次"（重建式，见同日批次修复）。

    Border? _labelLinesCard;
    SliderX _labelLinesSlider = null!;
    TextBlock _labelLinesZone = null!;
    IDisposable? _labelLinesToken;
    bool _labelLinesSyncing;
    bool _labelLinesOpen;                         // 展开态（齿轮保持转动位）
    System.Windows.Shapes.Path? _labelLinesGear;  // 小齿轮（2026-10-04 清单03任务3，替代 chevron）
    System.Windows.Media.RotateTransform? _labelLinesGearRotate;
    Border? _labelLinesGearBtn;

    static string LabelLinesKey(int zone) => zone switch { 1 => "two", 2 => "full", _ => "one" };

    static string ZoneTextKey(string value) => value switch
    {
        "two" => "set.labelLines.two",
        "full" => "set.labelLines.full",
        _ => "set.labelLines.one",
    };

    /// <summary>行数展开按钮：齿轮图标（几何/大小/hover 慢转口径照搬 BoardWindow 板头齿轮：
    /// hover 转 15°/GearSpin/微亮），展开态与 hover 同样保持转动位。图标经显式 Stretch=Uniform +
    /// 双向 Center 在 22×20 按钮内居中（旧 chevron 默认布局漂移出按钮的根因）。</summary>
    FrameworkElement BuildLabelLinesExpander()
    {
        _labelLinesGearRotate = new System.Windows.Media.RotateTransform();
        _labelLinesGear = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(
                "M19.14,12.94c0.04-0.3,0.06-0.61,0.06-0.94c0-0.32-0.02-0.64-0.07-0.94l2.03-1.58c0.18-0.14,0.23-0.41,0.12-0.61 l-1.92-3.32c-0.12-0.22-0.37-0.29-0.59-0.22l-2.39,0.96c-0.5-0.38-1.03-0.7-1.62-0.94L14.4,2.81c-0.04-0.24-0.24-0.41-0.48-0.41 h-3.84c-0.24,0-0.43,0.17-0.47,0.41L9.25,5.35C8.66,5.59,8.12,5.92,7.63,6.29L5.24,5.33c-0.22-0.08-0.47,0-0.59,0.22L2.74,8.87 C2.62,9.08,2.66,9.34,2.86,9.48l2.03,1.58C4.84,11.36,4.8,11.69,4.8,12s0.02,0.64,0.07,0.94l-2.03,1.58 c-0.18,0.14-0.23,0.41-0.12,0.61l1.92,3.32c0.12,0.22,0.37,0.29,0.59,0.22l2.39-0.96c0.5,0.38,1.03,0.7,1.62,0.94l0.36,2.54 c0.05,0.24,0.24,0.41,0.48,0.41h3.84c0.24,0,0.44-0.17,0.47-0.41l0.36-2.54c0.59-0.24,1.13-0.56,1.62-0.94l2.39,0.96 c0.22,0.08,0.47,0,0.59-0.22l1.92-3.32c0.12-0.22,0.07-0.47-0.12-0.61L19.14,12.94z M12,15.6c-1.98,0-3.6-1.62-3.6-3.6 s1.62-3.6,3.6-3.6s3.6,1.62,3.6,3.6S13.98,15.6,12,15.6z"),
            Stretch = Stretch.Uniform,
            Width = 13,
            Height = 13,
            Fill = SettingsPalette.TextSecondary(Dark),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _labelLinesGearRotate,
        };
        _labelLinesGearBtn = new Border
        {
            Width = 22,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            Background = SettingsPalette.Track(Dark),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = _labelLinesGear,
        };
        _labelLinesGearBtn.MouseEnter += (_, _) => AnimateLabelLinesGear();
        _labelLinesGearBtn.MouseLeave += (_, _) => AnimateLabelLinesGear();
        _labelLinesGearBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleLabelLinesPopup(_labelLinesGearBtn);
        };
        return _labelLinesGearBtn;
    }

    /// <summary>齿轮转动位 = hover 或展开态；透明度随之微亮（与板头齿轮同口径）。</summary>
    void AnimateLabelLinesGear()
    {
        if (_labelLinesGear == null || _labelLinesGearRotate == null) return;
        bool active = _labelLinesGearBtn?.IsMouseOver == true || _labelLinesOpen;
        Anim.Run(_labelLinesGear, OpacityProperty, null, active ? 1 : 0.85, AnimTuning.Micro, EaseStyle.Linear);
        Anim.Run(_labelLinesGearRotate, System.Windows.Media.RotateTransform.AngleProperty,
            null, active ? 15 : 0, AnimTuning.GearSpin, EaseStyle.EaseInOutQuad);
    }

    void ToggleLabelLinesPopup(FrameworkElement anchor)
    {
        if (_labelLinesCard != null)
        {
            CloseLabelLinesCard();
            return;
        }
        if (_labelLinesSlider == null)
        {
            _labelLinesSlider = new SliderX(Dark) { Width = 180, Minimum = 0, Maximum = 2, Step = 1, ShowValue = false };
            _labelLinesSlider.ValueChanged += v =>
            {
                if (_labelLinesSyncing) return;
                string value = LabelLinesKey((int)Math.Round(v));
                SettingsStore.Instance.SetString("display.labelLines", value);
                _labelLinesZone.Text = I18n.Tr(ZoneTextKey(value));
            };
            _labelLinesZone = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = SettingsPalette.TextSecondary(Dark),
            };
            _labelLinesCard = new Border
            {
                Background = SettingsPalette.Bg(Dark),
                BorderBrush = SettingsPalette.Divider(Dark),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.28, Direction = 270 },
                Child = new StackPanel { Children = { _labelLinesSlider, _labelLinesZone } },
            };
        }
        // 打开前从存储同步（含滑块与档位文案）
        string current = SettingsStore.Instance.GetString("display.labelLines", "two");
        int zone = current switch { "two" => 1, "full" => 2, _ => 0 };
        _labelLinesSyncing = true;
        _labelLinesSlider.Value = zone;
        _labelLinesSyncing = false;
        _labelLinesZone.Text = I18n.Tr(ZoneTextKey(current));
        var card = _labelLinesCard; // 上面已惰性构造（或提前关闭返回），流程上必非空
        if (card!.Parent is Panel old) old.Children.Remove(card);
        if (!ExpanderOverlay.TryPlace(anchor, card, null, 0, 16)) return; // 渲染进设置窗自身（同窗不跃出）
        _labelLinesToken = ExpanderOverlay.RegisterOpen(CloseLabelLinesCard); // 点外关闭由承载层统一收口
        _labelLinesOpen = true;
        AnimateLabelLinesGear(); // 展开态齿轮保持转动位
        card.Opacity = 0;
        Anim.Run(card, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseOutQuad);
    }

    /// <summary>关闭行数展开卡（带动画）：淡出后从承载层摘除；卡/滑块/档位文案一并弃用（重建式）。</summary>
    void CloseLabelLinesCard()
    {
        var card = _labelLinesCard;
        if (card == null) return;
        _labelLinesCard = null;
        _labelLinesSlider = null!;
        _labelLinesZone = null!;
        _labelLinesToken?.Dispose();
        _labelLinesToken = null;
        _labelLinesOpen = false;
        AnimateLabelLinesGear(); // 收起后齿轮复位（hover 中则保持 hover 位）
        Anim.Run(card, OpacityProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseInQuad,
            onDone: () => { if (card.Parent is Panel host) host.Children.Remove(card); });
    }

    FoldingList BuildScreenList(StackPanel rows)
    {
        var list = new FoldingList(Dark);
        RebuildScreenOptions(list);
        RowRaw(rows, Label("set.screen"), list);
        return list;
    }

    void RebuildScreenOptions(FoldingList list)
    {
        var options = new System.Collections.Generic.List<FoldingOption>
        {
            new("auto", I18n.Tr("set.screen.auto")),
        };
        // 07 §3：枚举信息增强——"显示器N · 2560×1440 · 主屏 · DELL U2723QE"。
        // 型号名走 EnumDisplayDevices（零依赖）；取不到时**只回退基础信息**（不显示空占位）。
        var screens = Screens.Enumerate();
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            string label = string.Format(I18n.Tr("set.screen.item"), i + 1,
                $"{s.Width}×{s.Height}", s.Primary ? I18n.Tr("set.screen.primary") : I18n.Tr("set.screen.secondary"));
            if (!string.IsNullOrEmpty(s.Model))
                label += " · " + s.Model;
            options.Add(new FoldingOption(s.DeviceName, label));
        }
        list.SetOptions(options);
        list.SetSelected(SettingsStore.Instance.GetString("display.screen", "auto"), fireChanged: false);
        if (list.SelectedValue.Length == 0)
            list.SetSelected("auto", fireChanged: false);
        list.SelectedChanged -= OnScreenChanged;
        list.SelectedChanged += OnScreenChanged;
    }

    void OnScreenChanged(string v)
    {
        SettingsStore.Instance.SetString("display.screen", v);
        _screen.Collapse();
    }

    public override void RefreshDynamic()
    {
        // 屏幕列表文字随语言重建
        RebuildScreenOptions(_screen);
        // 分段控件文字
        foreach (var (key, textKeys) in _segmentLabels)
        {
            var labels = new string[textKeys.Length];
            for (int i = 0; i < textKeys.Length; i++)
                labels[i] = I18n.Tr(textKeys[i]);
            _segments[key].Seg.SetOptions(labels);
        }
    }

    public override void ReloadFromSettings()
    {
        _dockSize.Value = SettingsStore.Instance.GetDouble("display.dockSize", 58.7);
        _opacity.Value = SettingsStore.Instance.GetDouble("display.iconOpacity", 100);
        _boardOpacity.Value = SettingsStore.Instance.GetDouble("personal.boardOpacity", 91);
        _iconSize.Value = SettingsStore.Instance.GetDouble("display.iconSize", 67.1);
        _rowSpacing.Value = SettingsStore.Instance.GetDouble("display.rowSpacing", 0);
        _colSpacing.Value = SettingsStore.Instance.GetDouble("display.colSpacing", 10.9);
        SetSegment("display.iconStyle", new[] { "original", "hoverReveal" });
        _showLabels.IsChecked = SettingsStore.Instance.GetBool("display.showLabels", false);
        _hoverLabels.IsChecked = SettingsStore.Instance.GetBool("display.hoverLabels", true);
        // 屏幕列表随热插拔/分辨率变化重建（07 §3；App 在 DisplaySettingsChanged 里调 ReloadCurrentPage）
        RebuildScreenOptions(_screen);
        _screen.SetSelected(SettingsStore.Instance.GetString("display.screen", "auto"), fireChanged: false);
    }

    void SetSegment(string key, string[] values)
    {
        if (!_segments.TryGetValue(key, out var entry)) return;
        string current = SettingsStore.Instance.GetString(key, values[0]);
        int idx = Array.IndexOf(entry.Values, current);
        entry.Seg.SetSelected(idx < 0 ? 0 : idx, syncToUi: false);
    }
}
