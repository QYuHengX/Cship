using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cship.Core;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 设置分页基类（步骤 05）：滚动区 + 分组卡片（圆角 10、分隔线）+ 行布局辅助；
/// 子项控件初始值一律从 SettingsStore 读取（禁止控件内另存默认值副本）。
/// 语言切换 → RefreshText()（集中登记文字块）；主题翻转 → SettingsWindow 整页重建。
/// </summary>
public abstract class SettingsPageBase : UserControl
{
    protected readonly List<(TextBlock Text, string Key)> _texts = new();
    protected readonly List<TextBlock> _plainTexts = new(); // 非固定 key 的动态文字（RefreshDynamic 里更新）
    protected bool Dark { get; private set; }

    protected SettingsPageBase()
    {
        Dark = ThemeResolver.IsDark();
    }

    /// <summary>语言切换：重刷全部登记文案（各页动态文字走 RefreshDynamic）。</summary>
    public void RefreshText()
    {
        foreach (var (text, key) in _texts)
            text.Text = I18n.Tr(key);
        foreach (var text in _plainTexts)
            text.Text = ""; // 由 RefreshDynamic 覆盖
        RefreshDynamic();
    }

    /// <summary>页面动态文字（摘要/状态类）刷新钩子。</summary>
    public virtual void RefreshDynamic() { }

    /// <summary>页面被替换/丢弃时解绑静态事件（I18n.LanguageChanged 等）。
    /// 由 <see cref="SettingsWindow"/> 在换页前显式调用——仅靠 Unloaded 有漏网窗口
    /// （页面在完成首次布局前就被换掉时 Unloaded 不触发，构造期订阅的静态事件会把旧页钉住）。
    /// 实现须幂等（重复退订无副作用）。</summary>
    public virtual void Detach() { }

    /// <summary>导入配置热应用后，从存储重载控件显示值。</summary>
    public abstract void ReloadFromSettings();

    // ---- 构建辅助 ----

    /// <summary>分组卡片（圆角 10）：标题 + 内容行。行距/内边距取舒适密度（2026-10-02 二次调整）：
    /// 不再为"一口气全部塞进窗口"压缩自身范围——放不下的子项被窗口裁剪，滚动查看（ScrollWrap）。</summary>
    protected Border Section(string titleKey)
    {
        var title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8), Text = I18n.Tr(titleKey), Foreground = SettingsPalette.Text(Dark) };
        _texts.Add((title, titleKey));
        return SectionCard(title);
    }

    /// <summary>无标题分组卡片（2026-10-06）：关于页去掉"关于 Cship"标题后仍需卡片底与内边距，
    /// 只是不再有标题行。</summary>
    protected Border Section() => SectionCard(null);

    Border SectionCard(TextBlock? title)
    {
        var body = new StackPanel();
        var wrap = new StackPanel();
        if (title != null) wrap.Children.Add(title);
        wrap.Children.Add(body);
        return new Border
        {
            Background = SettingsPalette.Card(Dark),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = wrap,
            Tag = body,
        };
    }

    protected static StackPanel BodyOf(Border section) => (StackPanel)section.Tag!;

    /// <summary>加一行（label 左、control 右）。行上下留 5px 呼吸空间（舒适密度，2026-10-02）。
    /// 返回行容器。</summary>
    protected Grid Row(StackPanel body, string labelKey, FrameworkElement control, bool indent = false)
    {
        var label = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Text = I18n.Tr(labelKey), Foreground = SettingsPalette.Text(Dark) };
        if (labelKey.Length > 0) _texts.Add((label, labelKey));
        return RowRaw(body, label, control, indent);
    }

    /// <summary>加一行（自定义 label 元素，如主题预览卡行）。</summary>
    protected Grid RowRaw(StackPanel body, FrameworkElement label, FrameworkElement control, bool indent = false)
    {
        var grid = new Grid { Margin = indent ? new Thickness(18, 5, 0, 5) : new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        grid.Children.Add(control);
        body.Children.Add(grid);
        body.Children.Add(Separator());
        return grid;
    }

    protected static Border Separator()
        => new()
        {
            Height = 1,
            Background = SettingsPalette.Divider(ThemeResolver.IsDark()),
            Margin = new Thickness(0, 3, 0, 3),
            Opacity = 0.7,
        };

    /// <summary>整页滚动容器（页面根）。子项允许被窗口裁剪后滚动查看（不改变窗口大小）：
    /// 细圆角滚动条（与收纳板同款观感，2026-10-02）+ 滚轮平滑滚动。</summary>
    protected static ScrollViewer ScrollWrap(UIElement content)
    {
        var sv = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
            Focusable = false,
            FocusVisualStyle = null, // 双保险：即便日后放开 Focusable 也不再弹虚线焦点框（2026-10-05 需求②）
            Padding = new Thickness(0, 0, 4, 2), // 右侧给滑块留缝，内容不顶滚动条
        };
        sv.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = ThinScrollBarStyle();
        new SmoothScroller(sv);
        return sv;
    }

    /// <summary>细滚动条样式（收纳板同款：8px 轨道 / 6px 圆角半透明滑块；XamlReader 装配，
    /// 与 BoardWindow.xaml 内联样式逐字一致）。</summary>
    static System.Windows.Style? _thinScrollBar;

    static System.Windows.Style ThinScrollBarStyle()
    {
        if (_thinScrollBar != null) return _thinScrollBar;
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type ScrollBar}">
                <Setter Property="Width" Value="8"/>
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type ScrollBar}">
                            <Grid Background="Transparent">
                                <Track x:Name="PART_Track" IsDirectionReversed="True">
                                    <Track.Thumb>
                                        <Thumb Focusable="False">
                                            <Thumb.Template>
                                                <ControlTemplate TargetType="{x:Type Thumb}">
                                                    <Border x:Name="ThumbBd" Background="#2E888888"
                                                            CornerRadius="4" Width="6"
                                                            HorizontalAlignment="Center" Margin="0,2,0,2"/>
                                                    <ControlTemplate.Triggers>
                                                        <Trigger Property="IsMouseOver" Value="True">
                                                            <Setter TargetName="ThumbBd" Property="Background" Value="#4A707070"/>
                                                        </Trigger>
                                                        <Trigger Property="IsDragging" Value="True">
                                                            <Setter TargetName="ThumbBd" Property="Background" Value="#66707070"/>
                                                        </Trigger>
                                                    </ControlTemplate.Triggers>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """;
        _thinScrollBar = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse(xaml);
        return _thinScrollBar;
    }

    protected static StackPanel PageBody()
        => new() { Margin = new Thickness(16, 10, 16, 12), HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>窄窗双列布局（2026-10-01 设置窗 2/3 尺寸）：左右两组分节卡片并排，
    /// 右列留 8px 列距；列内容自顶向下堆叠。</summary>
    protected static Grid TwoColumn(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        if (right is FrameworkElement fe)
            fe.Margin = new Thickness(8, 0, 0, 0);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>小标题说明文字（次要色）。</summary>
    protected TextBlock Caption(string keyOrText, bool isKey)
    {
        var text = new TextBlock { FontSize = 11, Foreground = SettingsPalette.TextSecondary(Dark), Margin = new Thickness(2, 2, 0, 6), TextWrapping = TextWrapping.Wrap, Text = isKey ? I18n.Tr(keyOrText) : keyOrText };
        if (isKey) _texts.Add((text, keyOrText));
        return text;
    }

    protected TextBlock Caption(TextBlock text) { _plainTexts.Add(text); return text; }
}
