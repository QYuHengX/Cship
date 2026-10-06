using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cship.Core;
using Cship.Ui.Animations;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 个性化页（步骤 05 §3.4，原文 L62-71）：动画预设（枚举 BoardAnimPresets + 迷你预览）/
/// 收纳板材质四选（2026-10-06 新增"无"）/ 自定义背景图（CropPicker + 缩略图 + 背景图不透明度，
/// 清单02任务9）/ 悬浮窗自定义（预设=底面材质；自定义=三层图片；**"各层不透明度"两种模式共用**，
/// 2026-10-06 用户要求）。
/// 2026-10-06：设置项下方的说明性注释（hint）按用户要求整体去掉，界面只留设置项本身。
/// </summary>
public class PersonalizePage : SettingsPageBase
{
    FoldingList _anim = null!;
    Border _matNone = null!, _matAcrylic = null!, _matBlur = null!, _matLiquid = null!;
    Border _bgThumb = null!;
    StackPanel _presetPanel = null!, _customPanel = null!;
    Segmented _dockMode = null!;
    Segmented _dockMaterial = null!;
    DockImageRow _imgTop = null!, _imgMid = null!, _imgBottom = null!;
    SliderX _opTop = null!, _opMid = null!, _opBottom = null!;
    bool _opacitySyncing; // 从存储回填滑块时抑制回写（否则回填会被当成用户拖动再写一遍）
    SliderX _bgOpacity = null!;

    public PersonalizePage()
    {
        var body = PageBody();

        // ---- 收纳板材质：4 选 1 预览图卡（"无"=不启用任何材质，2026-10-06 新增）----
        var matSection = Section("set.material");
        var matRow = new StackPanel { Orientation = Orientation.Horizontal };
        var matCol = new StackPanel();
        matCol.Children.Add(matRow);
        var matCaps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 0) };
        matCol.Children.Add(matCaps);
        _matNone = MaterialCard(matRow, matCaps, "material.none");
        _matAcrylic = MaterialCard(matRow, matCaps, "material.acrylic");
        _matBlur = MaterialCard(matRow, matCaps, "material.blur");
        _matLiquid = MaterialCard(matRow, matCaps, "material.liquidGlass");
        BodyOf(matSection).Children.Add(matCol);
        ApplyMaterialSelection();
        body.Children.Add(matSection);

        // ---- 收纳板弹出方式（BoardAnimPresets 枚举 + 预览）----
        var animSection = Section("set.boardAnim");
        _anim = new FoldingList(Dark);
        RebuildAnimOptions();
        _anim.SelectedChanged += v =>
        {
            SettingsStore.Instance.SetString("personal.boardAnim", v); // ※ 开板即用（BoardAnimPresets.Current）
            _anim.Collapse();
        };
        RowRaw(BodyOf(animSection), Label("set.boardAnim"), _anim);
        body.Children.Add(animSection);

        // ---- 自定义背景图片（清单02任务9 重排：缩略图最左 → 选择/清除按钮；背景图不透明度滑块）----
        var bgSection = Section("set.boardBg");
        var brows = BodyOf(bgSection);
        _bgThumb = new Border
        {
            Width = 46,
            Height = 30,
            CornerRadius = new CornerRadius(4),
            Background = SettingsPalette.Track(Dark),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var bgButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bgButtons.Children.Add(_bgThumb);
        bgButtons.Children.Add(MakeButton("set.pickImage", PickBackground));
        bgButtons.Children.Add(MakeButton("set.clearImage", ClearBackground));
        RowRaw(brows, new Grid(), bgButtons);
        // 背景图不透明度（0~100%，只作用于背景图 Image，与板底 personal.boardOpacity 互不影响）
        _bgOpacity = new SliderX(Dark) { Width = 190, Minimum = 0, Maximum = 100, Step = 0.1, Unit = "%" };
        _bgOpacity.Value = SettingsStore.Instance.GetDouble("personal.boardBgOpacity", 100);
        _bgOpacity.ValueChanged += v => SettingsStore.Instance.SetDouble("personal.boardBgOpacity", v);
        RowRaw(brows, Label("set.bgOpacity"), _bgOpacity);
        body.Children.Add(bgSection);

        // ---- 悬浮窗自定义 ----
        var dockSection = Section("set.dockCustom");
        var drows = BodyOf(dockSection);
        _dockMode = new Segmented(Dark);
        _dockMode.SetOptions(new[] { I18n.Tr("set.dockCustom.preset"), I18n.Tr("set.dockCustom.custom") });
        _dockMode.SelectedChanged += i =>
        {
            SettingsStore.Instance.SetString("personal.dockCustom.mode", i == 0 ? "preset" : "custom");
            ApplyDockModePanels(animate: true); // 用户点击：淡出→换面板→淡入（清单04任务10）
        };
        RowRaw(drows, Label("set.dockCustom"), _dockMode);

        // 方式1：底面材质三选一（颜色盘已随清单02任务8删除：三层统一走材质观感）
        _presetPanel = new StackPanel();
        var prows = _presetPanel;
        _dockMaterial = new Segmented(Dark);
        _dockMaterial.SetOptions(new[] { I18n.Tr("material.acrylic"), I18n.Tr("material.blur"), I18n.Tr("material.liquidGlass") });
        _dockMaterial.SelectedChanged += i =>
        {
            SettingsStore.Instance.SetString("personal.dockCustom.material", i switch { 1 => "blur", 2 => "liquidGlass", _ => "acrylic" });
        };
        var matLabel = Label("set.dockCustom.material");
        matLabel.Margin = new Thickness(2, 2, 0, 4);
        prows.Children.Add(matLabel);
        prows.Children.Add(_dockMaterial);

        // 方式2：上/中/下三层各行 = 选择图片 → 缩略图 → 层标注（清单02任务8）
        _customPanel = new StackPanel();
        var crows = _customPanel;
        _imgTop = BuildImageRow(crows, "set.layer.top", 0);
        _imgMid = BuildImageRow(crows, "set.layer.mid", 1);
        _imgBottom = BuildImageRow(crows, "set.layer.bottom", 2);

        drows.Children.Add(_presetPanel);
        drows.Children.Add(_customPanel);

        // ---- 各层不透明度（2026-10-06 用户要求：这组调整**两种模式都生效**）----
        // 自定义模式=乘在层图上；预设模式=由 SquarePlate 折进材质配方（原始预设配方不被改写）。
        // 因此它不属于任何一个模式面板，而是常驻在两者之下——切模式也随时可调。
        var opTitle = Label("set.dockCustom.layerOpacity");
        opTitle.FontWeight = FontWeights.SemiBold;
        opTitle.Margin = new Thickness(2, 6, 0, 0);
        drows.Children.Add(opTitle);
        _opTop = BuildLayerOpacityRow(drows, "set.layer.top", 0);
        _opMid = BuildLayerOpacityRow(drows, "set.layer.mid", 1);
        _opBottom = BuildLayerOpacityRow(drows, "set.layer.bottom", 2);

        // 重置
        RowRaw(drows, new Grid(), MakeButton("set.dockCustom.reset", ResetDockCustom));
        body.Children.Add(dockSection);

        Content = ScrollWrap(body);
        LoadDockCustom();
        ReloadBgThumb();
    }

    /// <summary>悬浮窗自定义重置（2026-10-01；清单02任务8 去颜色项）：模式/底面材质/三层图片/
    /// 三层不透明度全部回**默认值**（材质=柔化、三层 100%），写键即广播 →
    /// FloatingDock.ApplyDockCustom 即时恢复默认三板外观。</summary>
    void ResetDockCustom()
    {
        SettingsStore.Instance.SetString("personal.dockCustom.mode", "preset");
        SettingsStore.Instance.SetString("personal.dockCustom.material", MaterialEngine.Blur);
        SettingsStore.Instance.Set("personal.dockCustom.images", JsonSerializer.SerializeToElement(new[] { "", "", "" }));
        SettingsStore.Instance.Set("personal.dockCustom.opacity", JsonSerializer.SerializeToElement(new[] { 100.0, 100.0, 100.0 }));
        LoadDockCustom();
    }

    TextBlock Label(string key)
    {
        var label = new TextBlock { FontSize = 12.5, Text = I18n.Tr(key), Foreground = SettingsPalette.Text(Dark) };
        _texts.Add((label, key));
        return label;
    }

    void RebuildAnimOptions()
    {
        var options = new System.Collections.Generic.List<FoldingOption>();
        foreach (var preset in BoardAnimPresets.All)
        {
            var captured = preset;
            options.Add(new FoldingOption(captured.Id, I18n.Tr(captured.I18nName), () => OpenAnimPreview(captured.Id)));
        }
        _anim.SetOptions(options);
        _anim.SetSelected(SettingsStore.Instance.GetString("personal.boardAnim", "slideEdge"), fireChanged: false);
    }

    // ---- 材质预览卡 ----

    Border MaterialCard(StackPanel host, StackPanel caps, string labelKey)
    {
        string mode = labelKey switch
        {
            "material.none" => MaterialEngine.None,
            "material.blur" => "blur",
            "material.liquidGlass" => "liquidGlass",
            _ => "acrylic",
        };
        var card = new Border
        {
            Width = CardWidth, // 2026-10-06：62 → 76px（材质档显示名变长："瞎眼防弹玻璃"）
            Height = 48,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 8, 0),
            // 步骤 06 §1.6：真实材质预览图（由 MaterialEngine 离屏渲染 120×76 后进 Textures 缓存，
            // 不再是静态占位色块）；圆角由 Border 背景绘制天然裁切
            Background = new ImageBrush(MaterialEngine.Thumbnail(mode, Dark))
            {
                Stretch = Stretch.UniformToFill,
            },
            Cursor = Cursors.Hand,
        };
        var cap = new TextBlock
        {
            FontSize = 10,
            Width = CardWidth,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, // 名称再长就折行，不溢出卡片（英文档名更长）
            Margin = new Thickness(0, 0, 8, 0),
            Text = I18n.Tr(labelKey),
            Foreground = SettingsPalette.TextSecondary(Dark),
        };
        _texts.Add((cap, labelKey));
        caps.Children.Add(cap);
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            SettingsStore.Instance.SetString("personal.material", mode); // ※ 06 步渲染内核接管
            ApplyMaterialSelection();
        };
        host.Children.Add(card);
        _materialCards.Add((card, mode, labelKey));
        return card;
    }

    readonly System.Collections.Generic.List<(Border Card, string Mode, string LabelKey)> _materialCards = new();

    /// <summary>材质卡（含名称）宽度：4 张合计 336px，留在设置页右列（约 386px）内。</summary>
    const double CardWidth = 76;

    void ApplyMaterialSelection()
    {
        string current = SettingsStore.Instance.GetString("personal.material", MaterialEngine.None);
        foreach (var (card, mode, _) in _materialCards)
        {
            bool on = string.Equals(mode, current, StringComparison.OrdinalIgnoreCase);
            card.BorderThickness = new Thickness(on ? 2 : 1);
            card.BorderBrush = on ? SettingsPalette.Accent(Dark) : SettingsPalette.Divider(Dark);
        }
    }

    // ---- 自定义背景图 ----

    /// <summary>背景图缩略图刷新（2026-10-06：原"未设置（使用默认材质背景）"文字注释按用户要求去掉，
    /// 状态改由缩略图本身表达——空名即回占位底色）。</summary>
    void ReloadBgThumb() => LoadThumb(_bgThumb, ReadBgImage());

    /// <summary>往小缩略图 Border 里装载 assets 相对名图片；空名=回占位底色。</summary>
    static void LoadThumb(Border thumb, string relativeName)
    {
        if (string.IsNullOrEmpty(relativeName))
        {
            thumb.Background = SettingsPalette.Track(ThemeResolver.IsDark());
            thumb.Child = null;
            return;
        }
        try
        {
            string path = Path.Combine(Paths.AssetsDir(), relativeName);
            if (!File.Exists(path)) return;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 92;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            thumb.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            thumb.Background = null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"缩略图加载失败：{ex.Message}");
        }
    }

    string ReadBgImage()
    {
        var el = SettingsStore.Instance.GetRawValue("personal.boardBg");
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String)
            return img.GetString() ?? "";
        return "";
    }

    void PickBackground()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            // 选图→复制副本到 config\assets（内容 hash 命名，删源图不影响显示【L65】）→CropPicker 框选
            string hashName = CopyToAssets(dialog.FileName);
            var picker = new CropPickerWindow(Path.Combine(Paths.AssetsDir(), hashName));
            picker.Finished += crop =>
            {
                if (crop is { } rect)
                {
                    var payload = new Dictionary<string, object?>
                    {
                        ["image"] = hashName,
                        ["crop"] = new Dictionary<string, double> { ["x"] = rect.X, ["y"] = rect.Y, ["w"] = rect.Width, ["h"] = rect.Height },
                    };
                    SettingsStore.Instance.Set("personal.boardBg", JsonSerializer.SerializeToElement(payload));
                }
                // 取消框选：保留旧背景不动
                ReloadBgThumb();
            };
            picker.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Warn($"选择背景图失败：{ex.Message}");
            MessageBox.Show(ex.Message, "Cship", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void ClearBackground()
    {
        SettingsStore.Instance.Set("personal.boardBg", JsonDocument.Parse("null").RootElement.Clone());
        ReloadBgThumb();
    }

    /// <summary>内容 SHA256 命名复制到 config\assets\（同内容不重复复制）。</summary>
    static string CopyToAssets(string source)
    {
        byte[] bytes = File.ReadAllBytes(source);
        string hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        string name = hash + Path.GetExtension(source).ToLowerInvariant();
        string target = Path.Combine(Paths.AssetsDir(), name);
        if (!File.Exists(target))
            File.Copy(source, target);
        return name;
    }

    // ---- 悬浮窗自定义 ----

    DockImageRow BuildImageRow(StackPanel rows, string labelKey, int index)
    {
        var row = new DockImageRow(Dark);
        row.PickRequested += () => PickLayerImage(index, row);
        row.LabelText.Text = I18n.Tr(labelKey); // 创建即赋初值（登记 _texts 只管语言热刷，2026-10-03 补）
        rows.Children.Add(row);
        _texts.Add((row.LabelText, labelKey));
        return row;
    }

    /// <summary>各层不透明度一行（0~100%）。常驻行，预设/自定义两种模式共用同一组值：
    /// 自定义模式乘在层图上，预设模式由 SquarePlate 折进材质配方（原始预设不被改写）。</summary>
    SliderX BuildLayerOpacityRow(StackPanel rows, string labelKey, int index)
    {
        var slider = new SliderX(Dark) { Width = 190, Minimum = 0, Maximum = 100, Step = 0.1, Unit = "%" };
        slider.Value = ReadDockOpacities()[index];
        slider.ValueChanged += v => { if (!_opacitySyncing) SaveDockOpacity(index, v); };
        RowRaw(rows, Label(labelKey), slider);
        return slider;
    }

    void PickLayerImage(int index, DockImageRow row)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            string name = CopyToAssets(dialog.FileName);
            var images = ReadDockImages();
            images[index] = name;
            SaveDockImages(images);
            row.SetImage(name);
        }
        catch (Exception ex)
        {
            Logger.Warn($"选择悬浮窗层图失败：{ex.Message}");
        }
    }

    /// <summary>三层图片各自不透明度（personal.dockCustom.opacity 数组，缺项/非法补 100，清单02任务8）。</summary>
    double[] ReadDockOpacities()
    {
        var result = new[] { 100.0, 100.0, 100.0 };
        var el = SettingsStore.Instance.GetRawValue("personal.dockCustom.opacity");
        if (el.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var v in el.EnumerateArray())
                if (i < 3 && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
                    result[i++] = Math.Clamp(d, 0, 100);
        }
        return result;
    }

    void SaveDockOpacity(int index, double value)
    {
        var values = ReadDockOpacities();
        values[index] = Math.Clamp(value, 0, 100);
        SettingsStore.Instance.Set("personal.dockCustom.opacity", JsonSerializer.SerializeToElement(values)); // ※ 三层透明度即时生效
    }

    string[] ReadDockImages()
    {
        var el = SettingsStore.Instance.GetRawValue("personal.dockCustom.images");
        var result = new[] { "", "", "" };
        if (el.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var v in el.EnumerateArray())
                if (i < 3 && v.ValueKind == JsonValueKind.String)
                    result[i++] = v.GetString() ?? "";
        }
        return result;
    }

    void SaveDockImages(string[] images)
    {
        SettingsStore.Instance.Set("personal.dockCustom.images", JsonSerializer.SerializeToElement(images)); // ※ 悬浮窗三层图片即时生效
    }

    /// <summary>
    /// 预设/自定义联动面板切换（清单04任务10）：animate=true（用户点击分段触发）走
    /// "旧面板淡出 → 收起换面板 → 新面板淡入"（单段 SettingsPageSwitch，与设置窗右列分页同节奏）；
    /// animate=false（初始载入/重置/设置回读）瞬时落位。
    /// </summary>
    void ApplyDockModePanels(bool animate = false)
    {
        bool custom = string.Equals(SettingsStore.Instance.GetString("personal.dockCustom.mode", "preset"), "custom", StringComparison.OrdinalIgnoreCase);
        var outgoing = custom ? _presetPanel : _customPanel;
        var incoming = custom ? _customPanel : _presetPanel;
        if (!animate)
        {
            ClearPanelAnim(incoming, visible: true);
            ClearPanelAnim(outgoing, visible: false);
            return;
        }
        if (outgoing.Visibility == Visibility.Visible)
            Anim.Run(outgoing, OpacityProperty, null, 0, AnimTuning.SettingsPageSwitch, EaseStyle.EaseInCubic,
                onDone: () => ClearPanelAnim(outgoing, visible: false));
        else
            ClearPanelAnim(outgoing, visible: false);
        ClearPanelAnim(incoming, visible: true);
        incoming.Opacity = 0;
        Anim.Run(incoming, OpacityProperty, null, 1, AnimTuning.SettingsPageSwitch, EaseStyle.EaseOutCubic);
    }

    /// <summary>面板终态落位：清动画、复位透明度、按需收起（收起时也清动画，防残留挂住 Visibility）。</summary>
    static void ClearPanelAnim(StackPanel panel, bool visible)
    {
        panel.BeginAnimation(OpacityProperty, null);
        panel.Opacity = 1;
        panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    void LoadDockCustom()
    {
        _dockMode.SetSelected(string.Equals(SettingsStore.Instance.GetString("personal.dockCustom.mode", "preset"), "custom", StringComparison.OrdinalIgnoreCase) ? 1 : 0, syncToUi: false);
        string material = SettingsStore.Instance.GetString("personal.dockCustom.material", MaterialEngine.Blur);
        _dockMaterial.SetSelected(material switch { "blur" => 1, "liquidGlass" => 2, _ => 0 }, syncToUi: false);
        // 各层不透明度（两种模式共用的常驻行）：回填时抑制回写，避免"回填被当成拖动"再写一遍
        var opacities = ReadDockOpacities();
        _opacitySyncing = true;
        _opTop.Value = opacities[0];
        _opMid.Value = opacities[1];
        _opBottom.Value = opacities[2];
        _opacitySyncing = false;
        var images = ReadDockImages();
        _imgTop.SetImage(images[0]);
        _imgMid.SetImage(images[1]);
        _imgBottom.SetImage(images[2]);
        ApplyDockModePanels();
    }

    public override void RefreshDynamic()
    {
        RebuildAnimOptions();
        ReloadBgThumb();
        _dockMode.SetOptions(new[] { I18n.Tr("set.dockCustom.preset"), I18n.Tr("set.dockCustom.custom") });
        _dockMaterial.SetOptions(new[] { I18n.Tr("material.acrylic"), I18n.Tr("material.blur"), I18n.Tr("material.liquidGlass") });
    }

    public override void ReloadFromSettings()
    {
        _anim.SetSelected(SettingsStore.Instance.GetString("personal.boardAnim", "slideEdge"), fireChanged: false);
        ApplyMaterialSelection();
        ReloadBgThumb();
        LoadDockCustom();
    }

    FrameworkElement MakeButton(string labelKey, Action onClick)
    {
        var btn = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = SettingsPalette.Track(Dark),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = I18n.Tr(labelKey), FontSize = 12, Foreground = SettingsPalette.Text(Dark) },
        };
        btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        btn.MouseEnter += (_, _) => btn.Background = SettingsPalette.HoverOverlay(Dark);
        btn.MouseLeave += (_, _) => btn.Background = SettingsPalette.Track(Dark);
        _texts.Add(((TextBlock)btn.Child!, labelKey));
        return btn;
    }

    // ---- 动画预览迷你窗（320×200 画布演示缩略开合）----

    void OpenAnimPreview(string presetId)
    {
        var win = new AnimPreviewWindow(presetId);
        win.Owner = Window.GetWindow(this);
        win.Show();
    }

    // ---- 三层图片行（清单02任务8 重排：选择图片按钮最左 → 缩略图 → 层标注紧随其右；
    //      "选择图片"按钮垂直居中修正（原 Border 被行高拉伸、文字偏上）；清除按钮去除——重置按钮统一负责清空。
    //      2026-10-06：行尾不透明度滑块**上移为两种模式共用的常驻行**（见 BuildLayerOpacityRow）----

    sealed class DockImageRow : Grid
    {
        public event Action? PickRequested;
        public TextBlock LabelText { get; } = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = SettingsPalette.Text(ThemeResolver.IsDark()) };

        readonly Border _thumb = new()
        {
            Width = 46,
            Height = 30,
            CornerRadius = new CornerRadius(4),
            Background = SettingsPalette.Track(ThemeResolver.IsDark()),
            VerticalAlignment = VerticalAlignment.Center,
        };

        public DockImageRow(bool dark)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var pick = MakeAction("set.pickImage", () => PickRequested?.Invoke(), dark);
            Grid.SetColumn(pick, 0);
            Children.Add(pick);
            _thumb.Margin = new Thickness(8, 4, 0, 4);
            Grid.SetColumn(_thumb, 1);
            Children.Add(_thumb);
            Grid.SetColumn(LabelText, 2);
            Children.Add(LabelText);
        }

        FrameworkElement MakeAction(string key, Action onClick, bool dark)
        {
            var btn = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 3, 10, 3),
                Background = SettingsPalette.Track(dark),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, // 修正：随行高拉伸导致按钮偏高、文字不居中
                Child = new TextBlock { Text = I18n.Tr(key), FontSize = 11, Foreground = SettingsPalette.Text(dark) },
            };
            btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
            return btn;
        }

        public void SetImage(string relativeName) => PersonalizePage.LoadThumb(_thumb, relativeName);
    }
}
