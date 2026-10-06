using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cship.Core;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 关于页（步骤 05 §3.5，原文 L72）：Logo + 产品名 + 版本号
/// （csproj 单一来源，Assembly.GetName().Version）+ 一句话简介。
/// 2026-10-06：卡片标题"关于 Cship"与"预留扩展区"占位卡按用户要求去掉（页面已在导航里叫"关于"，
/// 标题重复；占位卡只是注释性内容）。
/// 2026-10-06 扩展内容：简介下方追加四个"大按钮折叠项"（<see cref="InfoExpander"/>，展开卡经
/// ExpanderOverlay 同窗渲染）——项目仓库、开源许可证（MIT 原文+原文链接）、开发者、Bilibili 主页；
/// 页面末尾为无标题的致谢段（用户原文，随语言切换走 i18n）。链接点击走系统默认浏览器打开。
/// 折叠项标题/内容不走 _texts 集中登记（内容含非文本元素），语言切换由 RefreshDynamic 整体重建。
/// </summary>
public class AboutPage : SettingsPageBase
{
    public AboutPage()
    {
        var body = PageBody();

        var card = Section();
        var rows = BodyOf(card);

        // Logo：产品图标（步骤 06 附加需求）——素材 CShip.png 转制的 app.png，与 exe/托盘同源；
        // 经 IconBitmapCache 共享解码（不新增解码路径），圆角由 Border 画刷绘制裁切
        var logo = new Border
        {
            Width = 84,
            Height = 84,
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 12),
            Background = SettingsPalette.Track(Dark),
        };
        var logoBmp = IconBitmapCache.GetOrLoad(System.IO.Path.Combine(Paths.IconsDir(), "app.png"), 168);
        if (logoBmp != null)
            logo.Background = new ImageBrush(logoBmp) { Stretch = Stretch.UniformToFill };
        rows.Children.Add(logo);

        var name = new TextBlock
        {
            Text = I18n.Tr("app.name"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = SettingsPalette.Text(Dark),
        };
        rows.Children.Add(name);

        var version = new TextBlock
        {
            FontSize = 12,
            Foreground = SettingsPalette.TextSecondary(Dark),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 10),
        };
        string ver = (Assembly.GetEntryAssembly() ?? typeof(AboutPage).Assembly).GetName().Version?.ToString(3) ?? "0.0.0";
        _versionText = version;
        _versionText.Text = string.Format(I18n.Tr("set.about.version"), ver);
        _plainTexts.Add(version);
        rows.Children.Add(version);

        var intro = new TextBlock
        {
            Text = I18n.Tr("set.about.intro"),
            FontSize = 12,
            Foreground = SettingsPalette.TextSecondary(Dark),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 0, 8, 4),
        };
        _texts.Add((intro, "set.about.intro"));
        rows.Children.Add(intro);

        // ---- 扩展内容：四个大按钮折叠项（2026-10-06）----
        _repo = MakeExpander();
        _license = MakeExpander();
        _developer = MakeExpander();
        _bilibili = MakeExpander();
        foreach (var exp in new[] { _repo, _license, _developer, _bilibili })
            rows.Children.Add(exp);
        RefreshExpanders();

        // ---- 结尾致谢段（无标题，用户原文）----
        rows.Children.Add(Separator());
        var outro = new TextBlock
        {
            Text = I18n.Tr("set.about.outro"),
            FontSize = 11,
            Foreground = SettingsPalette.TextSecondary(Dark),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 8, 2, 4),
        };
        _texts.Add((outro, "set.about.outro"));
        rows.Children.Add(outro);

        body.Children.Add(card);

        Content = ScrollWrap(body);
    }

    TextBlock _versionText = null!;
    readonly InfoExpander _repo, _license, _developer, _bilibili;

    InfoExpander MakeExpander() => new(Dark) { Margin = new Thickness(0, 6, 0, 2) };

    // ---- 各折叠项标题与内容（语言切换时整体重建：内容含链接芯片等非文本元素，不进 _texts 集中登记）----

    void RefreshExpanders()
    {
        _repo.SetTitle(I18n.Tr("set.about.repo"));
        _repo.SetContent(RepoContent());

        _license.SetTitle(I18n.Tr("set.about.license"));
        _license.SetContent(LicenseContent());

        _developer.SetTitle(I18n.Tr("set.about.developer"));
        _developer.SetContent(DeveloperContent());

        _bilibili.SetTitle(I18n.Tr("set.about.bilibili"));
        _bilibili.SetContent(BilibiliContent());
    }

    /// <summary>项目仓库：简介说明 + 仓库链接按钮。</summary>
    StackPanel RepoContent()
    {
        var p = ContentPanel();
        p.Children.Add(CaptionText("set.about.repo.desc"));
        p.Children.Add(LinkChip(I18n.Tr("set.about.repo.open"), RepoUrl));
        return p;
    }

    /// <summary>开源许可证：说明 + MIT 原文（浅底板内）+ 原文链接。</summary>
    StackPanel LicenseContent()
    {
        var p = ContentPanel();
        p.Children.Add(CaptionText("set.about.license.summary"));
        var plate = new Border
        {
            Background = SettingsPalette.Track(Dark),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            MaxWidth = 440,
            Child = new TextBlock
            {
                Text = I18n.Tr("set.about.license.text"),
                FontSize = 10.5,
                LineHeight = 15,
                Foreground = SettingsPalette.TextSecondary(Dark),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            },
        };
        p.Children.Add(plate);
        p.Children.Add(LinkChip(I18n.Tr("set.about.license.open"), LicenseUrl));
        return p;
    }

    /// <summary>开发者：署名 + GitHub 链接按钮。</summary>
    StackPanel DeveloperContent()
    {
        var p = ContentPanel();
        p.Children.Add(new TextBlock
        {
            Text = I18n.Tr("set.about.developer.name"),
            FontSize = 12,
            Foreground = SettingsPalette.Text(Dark),
            Margin = new Thickness(0, 0, 0, 6),
        });
        p.Children.Add(LinkChip(I18n.Tr("set.about.developer.github"), GitHubUrl));
        return p;
    }

    /// <summary>Bilibili 主页：说明 + 链接按钮（展示地址文案）。</summary>
    StackPanel BilibiliContent()
    {
        var p = ContentPanel();
        p.Children.Add(CaptionText("set.about.bilibili.desc"));
        p.Children.Add(LinkChip(I18n.Tr("set.about.bilibili.open"), BilibiliUrl));
        return p;
    }

    /// <summary>折叠卡内容容器：统一内边距。</summary>
    StackPanel ContentPanel() => new() { Margin = new Thickness(6, 2, 6, 6) };

    /// <summary>说明文字（次要色、可换行，窄窗下不顶卡边）。</summary>
    TextBlock CaptionText(string key) => new()
    {
        Text = I18n.Tr(key),
        FontSize = 12,
        Foreground = SettingsPalette.TextSecondary(Dark),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
        MaxWidth = 440,
    };

    /// <summary>链接按钮（浅底小圆角芯片，悬停加深+下划线）：点击用系统默认浏览器打开。</summary>
    static Button LinkChip(string text, string url)
    {
        var btn = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 12 },
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Tag = url,
        };
        ApplyChipVisual(btn, hover: false);
        btn.MouseEnter += (_, _) => ApplyChipVisual(btn, hover: true);
        btn.MouseLeave += (_, _) => ApplyChipVisual(btn, hover: false);
        btn.Click += (_, _) => OpenUrl((string)btn.Tag);
        return btn;
    }

    static void ApplyChipVisual(Button btn, bool hover)
    {
        bool dark = ThemeResolver.IsDark();
        btn.Background = hover ? SettingsPalette.HoverOverlay(dark) : SettingsPalette.Track(dark);
        btn.BorderBrush = SettingsPalette.Divider(dark);
        btn.BorderThickness = new Thickness(1);
        if (btn.Content is TextBlock tb)
        {
            tb.Foreground = SettingsPalette.Text(dark);
            if (hover)
            {
                var deco = new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Pen = new Pen(SettingsPalette.TextSecondary(dark), 1),
                };
                tb.TextDecorations = new TextDecorationCollection { deco };
            }
            else
            {
                tb.TextDecorations = null;
            }
        }
    }

    static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn($"打开链接失败：{url}（{ex.Message}）");
        }
    }

    const string RepoUrl = "https://github.com/QYuHengX/Cship";
    const string LicenseUrl = "https://github.com/QYuHengX/Cship/blob/main/LICENSE";
    const string GitHubUrl = "https://github.com/QYuHengX";
    const string BilibiliUrl = "https://space.bilibili.com/3546630974343310";

    public override void RefreshDynamic()
    {
        string ver = (Assembly.GetEntryAssembly() ?? typeof(AboutPage).Assembly).GetName().Version?.ToString(3) ?? "0.0.0";
        _versionText.Text = string.Format(I18n.Tr("set.about.version"), ver);
        RefreshExpanders(); // 折叠项标题与内容随语言重建
    }

    public override void ReloadFromSettings() { }
}
