using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Cship.Core;
using Cship.Ui.Components;

namespace Cship.Ui.Pages;

/// <summary>
/// 关于页（步骤 05 §3.5，原文 L72）：Logo + 产品名 + 版本号
/// （csproj 单一来源，Assembly.GetName().Version）+ 一句话简介。
/// 2026-10-06：卡片标题"关于 Cship"与"预留扩展区"占位卡按用户要求去掉（页面已在导航里叫"关于"，
/// 标题重复；占位卡只是注释性内容）。
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
        body.Children.Add(card);

        Content = ScrollWrap(body);
    }

    TextBlock _versionText = null!;

    public override void RefreshDynamic()
    {
        string ver = (Assembly.GetEntryAssembly() ?? typeof(AboutPage).Assembly).GetName().Version?.ToString(3) ?? "0.0.0";
        _versionText.Text = string.Format(I18n.Tr("set.about.version"), ver);
    }

    public override void ReloadFromSettings() { }
}
