using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Cship.Core;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 高级页（步骤 05 §3.3，原文 L44-61）：弹出方向六选 / 三个内容过滤 Latch / 分类页母+子开关 /
/// 图标遮罩等七开关 / 悬浮窗优先级 / 配置导出导入。
/// </summary>
public class AdvancedPage : SettingsPageBase
{
    FoldingList _direction = null!;
    LatchButton _showApps = null!, _showFiles = null!, _showFolders = null!;
    ToggleSwitch _categoryDisabled = null!;
    LatchButton _catApps = null!, _catFiles = null!, _catFolders = null!;
    ToggleSwitch _iconMask = null!, _lockDrag = null!, _dblItems = null!, _dblFolders = null!,
        _bounce = null!, _indicator = null!, _dockLocked = null!;
    FoldingList _priority = null!;
    ToggleSwitch _dockFollowTheme = null!; // 悬浮窗跟随主题（2026-10-03 新增）
    HotkeyBox _toggleKey = null!, _forceKey = null!; // 全局快捷键（2026-10-05 需求③）
    TextBlock _hotkeyWarn = null!;

    public AdvancedPage()
    {
        var body = PageBody();

        // ---- 收纳板：弹出方向 + 内容过滤（每个项独占一行，2026-10-02 用户要求）----
        var board = Section("set.advanced.board");
        var rows = BodyOf(board);
        _direction = new FoldingList(Dark);
        _direction.SetOptions(new[]
        {
            new FoldingOption("auto", "Ⓐ " + I18n.Tr("dir.auto")),
            new FoldingOption("up", "↑ " + I18n.Tr("dir.up")),
            new FoldingOption("down", "↓ " + I18n.Tr("dir.down")),
            new FoldingOption("left", "← " + I18n.Tr("dir.left")),
            new FoldingOption("right", "→ " + I18n.Tr("dir.right")),
            new FoldingOption("center", "◎ " + I18n.Tr("dir.center")),
        });
        _direction.SetSelected(SettingsStore.Instance.GetString("advanced.popupDirection", "auto"), fireChanged: false);
        _direction.SelectedChanged += v =>
        {
            SettingsStore.Instance.SetString("advanced.popupDirection", v); // ※ 重锚边由 BoardWindow 联动
            _direction.Collapse();
        };
        RowRaw(rows, Label("set.popupDirection"), _direction);

        // 弹出判定范围（2026-10-02）：0~100%，两侧边缘判定带合计占屏幕高/宽比例；
        // auto 方向下，悬浮窗中心处于中央 (100−p)% 区域才判居中弹出
        var edgeSlider = new SliderX(Dark) { Width = 210, Minimum = 0, Maximum = 100, Unit = "%" };
        edgeSlider.Value = SettingsStore.Instance.GetDouble("advanced.dockEdgeRange", 40.243902439024396);
        edgeSlider.ValueChanged += v => SettingsStore.Instance.SetDouble("advanced.dockEdgeRange", v);
        RowRaw(rows, Label("set.dockEdgeRange"), edgeSlider);

        _showApps = BuildLatchRow(rows, "set.showApps", "advanced.showApps");
        _showFiles = BuildLatchRow(rows, "set.showFiles", "advanced.showFiles");
        _showFolders = BuildLatchRow(rows, "set.showFolders", "advanced.showFolders");
        body.Children.Add(board);

        // ---- 图标与交互 ----
        var misc = Section("set.advanced.icon");
        var mrows = BodyOf(misc);
        _iconMask = BuildToggle(mrows, "set.iconMask", "advanced.iconMask");
        _lockDrag = BuildToggle(mrows, "set.lockIconDrag", "advanced.lockIconDrag");
        _dblItems = BuildToggle(mrows, "set.dblOpenItems", "advanced.dblOpenItems");
        _dblFolders = BuildToggle(mrows, "set.dblOpenFolders", "advanced.dblOpenFolders");
        _bounce = BuildToggle(mrows, "set.bounceOpen", "advanced.bounceOpen");
        _indicator = BuildToggle(mrows, "set.runningIndicator", "advanced.runningIndicator");
        _dockLocked = BuildToggle(mrows, "set.dockLocked", "advanced.dockLocked");
        body.Children.Add(misc);

        // ---- 分类页：母开关 + 三子开关各自一行 ----
        var cat = Section("set.advanced.category");
        var crows = BodyOf(cat);
        _categoryDisabled = new ToggleSwitch { IsChecked = SettingsStore.Instance.GetBool("advanced.categoryDisabled", false) };
        _categoryDisabled.CheckedChanged += on =>
        {
            SettingsStore.Instance.SetBool("advanced.categoryDisabled", on);
            ApplyCategoryEnabled();
        };
        Row(crows, "set.categoryDisabled", _categoryDisabled);
        _catApps = BuildLatchRow(crows, "cat.apps", "advanced.catShowApps");
        _catFolders = BuildLatchRow(crows, "cat.folders", "advanced.catShowFolders");
        _catFiles = BuildLatchRow(crows, "cat.files", "advanced.catShowFiles");
        body.Children.Add(cat);

        // ---- 窗口优先级（2026-10-03 更名，原"悬浮窗优先级"）：收纳板与设置窗口跟随；
        //      悬浮窗固定桌面级不再受影响 ----
        var prio = Section("set.dockPriority");
        var prows = BodyOf(prio);
        _priority = new FoldingList(Dark);
        _priority.SetOptions(new[]
        {
            new FoldingOption("low", I18n.Tr("prio.low")),
            new FoldingOption("medium", I18n.Tr("prio.medium")),
            new FoldingOption("high", I18n.Tr("prio.high")),
        });
        _priority.SetSelected(SettingsStore.Instance.GetString("advanced.dockPriority", "low"), fireChanged: false);
        _priority.SelectedChanged += v =>
        {
            SettingsStore.Instance.SetString("advanced.dockPriority", v); // ※ 收纳板/设置窗置顶即时联动
            _priority.Collapse();
        };
        RowRaw(prows, Label("set.dockPriority"), _priority);
        _dockFollowTheme = new ToggleSwitch { IsChecked = SettingsStore.Instance.GetBool("advanced.dockFollowTheme", false) };
        _dockFollowTheme.CheckedChanged += on =>
        {
            SettingsStore.Instance.SetBool("advanced.dockFollowTheme", on); // ※ FloatingDock.ApplyDockTheme 联动
        };
        Row(prows, "set.dockFollowTheme", _dockFollowTheme); // 优先级子项下方（2026-10-03 新增）
        body.Children.Add(prio);

        // ---- 快捷键（2026-10-05 需求③）：两个全局快捷键各自一行，点框后按新组合即改 ----
        var keys = Section("set.advanced.hotkey");
        var krows = BodyOf(keys);
        _toggleKey = new HotkeyBox(Dark);
        _forceKey = new HotkeyBox(Dark);
        _toggleKey.SetChord(KeyChord.ParseOr(SettingsStore.Instance.GetString("advanced.hotkeyToggle", "Tab"), KeyChord.DefaultToggle));
        _forceKey.SetChord(KeyChord.ParseOr(SettingsStore.Instance.GetString("advanced.hotkeyForce", "Alt+Tab"), KeyChord.DefaultForce));
        _toggleKey.ChordSet += c => ApplyHotkey("advanced.hotkeyToggle", _toggleKey, _forceKey, c);
        _forceKey.ChordSet += c => ApplyHotkey("advanced.hotkeyForce", _forceKey, _toggleKey, c);
        RowRaw(krows, Label("set.hotkeyToggle"), _toggleKey);
        RowRaw(krows, Label("set.hotkeyForce"), _forceKey);
        // 说明性注释（"点击右侧按键框后按下新的组合键…"）按用户要求去掉；冲突提示是错误态，保留
        _hotkeyWarn = Caption("set.hotkey.dup", isKey: true);
        _hotkeyWarn.Foreground = SettingsPalette.Danger(Dark);
        _hotkeyWarn.Visibility = Visibility.Collapsed;
        krows.Children.Add(_hotkeyWarn);
        body.Children.Add(keys);

        // ---- 恢复默认（2026-10-06 用户要求）：配置区之上 ----
        var reset = Section("set.reset");
        var rrows = BodyOf(reset);
        RowRaw(rrows, new Grid(), MakeActionButton("set.reset.button", OnResetDefaults, danger: true));
        body.Children.Add(reset);

        // ---- 配置：导出/导入各自一行 ----
        var cfg = Section("set.advanced.config");
        var grows = BodyOf(cfg);
        RowRaw(grows, new Grid(), MakeActionButton("set.export", OnExport));
        RowRaw(grows, new Grid(), MakeActionButton("set.import", OnImport));
        body.Children.Add(cfg);

        Content = ScrollWrap(body);
        ApplyCategoryEnabled();
    }

    TextBlock Label(string key)
    {
        var label = new TextBlock { FontSize = 12.5, Text = I18n.Tr(key), Foreground = SettingsPalette.Text(Dark) };
        _texts.Add((label, key));
        return label;
    }

    /// <summary>构建闩锁并独占一行（label 左、闩锁右，2026-10-02 用户要求"每个项占一行"）。</summary>
    LatchButton BuildLatchRow(StackPanel body, string labelKey, string settingKey)
    {
        var latch = new LatchButton(Dark) { IsOn = SettingsStore.Instance.GetBool(settingKey, true) };
        latch.SetText(I18n.Tr(labelKey)); // 初始文字（RefreshText 只做语言热切换刷新）
        latch.Toggled += on => SettingsStore.Instance.SetBool(settingKey, on);
        RowRaw(body, Label(labelKey), latch);
        _latchTexts.Add((latch, labelKey));
        return latch;
    }

    readonly System.Collections.Generic.List<(LatchButton, string)> _latchTexts = new();

    ToggleSwitch BuildToggle(StackPanel rows, string labelKey, string settingKey)
    {
        var toggle = new ToggleSwitch { IsChecked = SettingsStore.Instance.GetBool(settingKey, true) };
        toggle.CheckedChanged += on => SettingsStore.Instance.SetBool(settingKey, on);
        Row(rows, labelKey, toggle);
        return toggle;
    }

    FrameworkElement MakeActionButton(string labelKey, Action onClick, bool danger = false)
    {
        var btn = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Background = danger ? SettingsPalette.Danger(Dark) : SettingsPalette.Accent(Dark),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock
            {
                Text = I18n.Tr(labelKey),
                FontSize = 12,
                Foreground = danger ? SettingsPalette.OnDanger(Dark) : SettingsPalette.OnAccent(Dark),
            },
        };
        btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        _texts.Add(((TextBlock)btn.Child!, labelKey));
        return btn;
    }

    /// <summary>
    /// 恢复默认（2026-10-06 用户要求）：把**全部设置项**写回默认值，**不动分类项**
    /// （分类/图标格序/移除名单/板内快捷方式都在 state.json，本操作只碰 settings.json；
    /// 窗口位置尺寸同属"这台机器的当前状态"亦不动——那是托盘"复位"的职责）。
    /// 二次确认后逐键 Set → SettingsChanged 广播，各窗口与系统副作用（主题/材质/字体/快捷键/
    /// 自启动/隐藏桌面图标/语言）即时跟随；最后广播 <see cref="DefaultsRestored"/> 让设置页回读控件值。
    /// </summary>
    void OnResetDefaults()
    {
        var choice = MessageBox.Show(I18n.Tr("set.reset.confirm"), I18n.Tr("set.reset"),
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK) return;
        try
        {
            int changed = SettingsStore.Instance.ResetToDefaults();
            Logger.Info($"恢复默认设置：{changed} 项写回默认值（分类项与图标布局未动）");
        }
        catch (Exception ex)
        {
            Logger.Warn($"恢复默认设置失败：{ex.Message}");
            MessageBox.Show(I18n.Tr("set.reset") + "\n" + ex.Message, "Cship", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DefaultsRestored?.Invoke();
    }

    /// <summary>恢复默认后由 SettingsWindow 触发（当前页控件值整体回读；页面被主题重建时同样安全）。</summary>
    public static event Action? DefaultsRestored;

    /// <summary>落盘一个快捷键（2026-10-05 需求③）：两个键位不允许相同——相同则整条修改作废
    /// （框内显示回退原值），并亮出冲突提示；相同键位无法判定语义，静默取一反而更难排查。</summary>
    void ApplyHotkey(string key, HotkeyBox box, HotkeyBox other, KeyChord chord)
    {
        if (chord == other.Value)
        {
            box.SetChord(box.Value); // 回退显示：不改存储
            _hotkeyWarn.Visibility = Visibility.Visible;
            return;
        }
        _hotkeyWarn.Visibility = Visibility.Collapsed;
        SettingsStore.Instance.SetString(key, chord.ToStorage()); // ※ App.OnHotkeyPressed 每键实时读设置
        box.SetChord(chord);
    }

    void ApplyCategoryEnabled()
    {
        bool disabled = SettingsStore.Instance.GetBool("advanced.categoryDisabled", false);
        foreach (var latch in new[] { _catApps, _catFiles, _catFolders })
        {
            latch.Opacity = disabled ? 0.4 : 1;
            latch.IsEnabled = !disabled; // 母开关关闭→子开关置灰不可点
        }
    }

    // ---- 配置导出/导入（§4）----

    void OnExport()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Cship 配置|*.txt",
            FileName = $"Cship配置_{DateTime.Now:yyyyMMdd}.txt",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            ConfigPort.Export(dialog.FileName);
        }
        catch (Exception ex)
        {
            Logger.Warn($"配置导出失败：{ex.Message}");
            MessageBox.Show($"导出失败：{ex.Message}", "Cship", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OnImport()
    {
        var dialog = new OpenFileDialog { Filter = "Cship 配置|*.txt|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var parsed = ConfigPort.Parse(System.IO.File.ReadAllText(dialog.FileName));
            string summary = ConfigPort.Summary(parsed,
                I18n.Tr("cfg.applyCount"), I18n.Tr("cfg.ignoreCount"), I18n.Tr("cfg.ignoredKeys"));
            if (parsed.Ignored > 0)
                Logger.Info($"配置导入忽略 {parsed.Ignored} 项：{string.Join("、", parsed.IgnoredKeys)}");
            var choice = MessageBox.Show(summary, I18n.Tr("cfg.summaryTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (choice != MessageBoxResult.OK) return;
            ConfigPort.Apply(parsed.Apply);
            ImportedConfigHotReload?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Warn($"配置导入失败：{ex.Message}");
            MessageBox.Show(I18n.Tr("cfg.importFailed") + "\n" + ex.Message, "Cship", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>导入应用后由 SettingsWindow 触发（窗口重载页面 + 各窗口重载持久化状态）。</summary>
    public static event Action? ImportedConfigHotReload;

    public override void RefreshDynamic()
    {
        foreach (var (latch, key) in _latchTexts)
            latch.SetText(I18n.Tr(key));
        _direction.SetOptions(new[]
        {
            new FoldingOption("auto", "Ⓐ " + I18n.Tr("dir.auto")),
            new FoldingOption("up", "↑ " + I18n.Tr("dir.up")),
            new FoldingOption("down", "↓ " + I18n.Tr("dir.down")),
            new FoldingOption("left", "← " + I18n.Tr("dir.left")),
            new FoldingOption("right", "→ " + I18n.Tr("dir.right")),
            new FoldingOption("center", "◎ " + I18n.Tr("dir.center")),
        });
        _direction.SetSelected(SettingsStore.Instance.GetString("advanced.popupDirection", "auto"), fireChanged: false);
        _priority.SetOptions(new[]
        {
            new FoldingOption("low", I18n.Tr("prio.low")),
            new FoldingOption("medium", I18n.Tr("prio.medium")),
            new FoldingOption("high", I18n.Tr("prio.high")),
        });
        _priority.SetSelected(SettingsStore.Instance.GetString("advanced.dockPriority", "low"), fireChanged: false);

        // 快捷键（2026-10-05 需求③）：组合名与语言无关，只需重刷录入提示语
        _toggleKey.RefreshText();
        _forceKey.RefreshText();
    }

    public override void ReloadFromSettings()
    {
        _direction.SetSelected(SettingsStore.Instance.GetString("advanced.popupDirection", "auto"), fireChanged: false);
        _showApps.IsOn = SettingsStore.Instance.GetBool("advanced.showApps", true);
        _showFiles.IsOn = SettingsStore.Instance.GetBool("advanced.showFiles", true);
        _showFolders.IsOn = SettingsStore.Instance.GetBool("advanced.showFolders", true);
        _categoryDisabled.IsChecked = SettingsStore.Instance.GetBool("advanced.categoryDisabled", false);
        _catApps.IsOn = SettingsStore.Instance.GetBool("advanced.catShowApps", true);
        _catFiles.IsOn = SettingsStore.Instance.GetBool("advanced.catShowFiles", true);
        _catFolders.IsOn = SettingsStore.Instance.GetBool("advanced.catShowFolders", true);
        _iconMask.IsChecked = SettingsStore.Instance.GetBool("advanced.iconMask", true);
        _lockDrag.IsChecked = SettingsStore.Instance.GetBool("advanced.lockIconDrag", false);
        _dblItems.IsChecked = SettingsStore.Instance.GetBool("advanced.dblOpenItems", true);
        _dblFolders.IsChecked = SettingsStore.Instance.GetBool("advanced.dblOpenFolders", true);
        _bounce.IsChecked = SettingsStore.Instance.GetBool("advanced.bounceOpen", true);
        _indicator.IsChecked = SettingsStore.Instance.GetBool("advanced.runningIndicator", true);
        _dockLocked.IsChecked = SettingsStore.Instance.GetBool("advanced.dockLocked", false);
        _priority.SetSelected(SettingsStore.Instance.GetString("advanced.dockPriority", "low"), fireChanged: false);
        _dockFollowTheme.IsChecked = SettingsStore.Instance.GetBool("advanced.dockFollowTheme", false);
        _toggleKey.SetChord(KeyChord.ParseOr(SettingsStore.Instance.GetString("advanced.hotkeyToggle", "Tab"), KeyChord.DefaultToggle));
        _forceKey.SetChord(KeyChord.ParseOr(SettingsStore.Instance.GetString("advanced.hotkeyForce", "Alt+Tab"), KeyChord.DefaultForce));
        _hotkeyWarn.Visibility = _toggleKey.Value == _forceKey.Value ? Visibility.Visible : Visibility.Collapsed;
        ApplyCategoryEnabled();
    }
}
