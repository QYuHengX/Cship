using System;
using System.IO;
namespace Cship.Core;

/// <summary>
/// 全项目磁盘路径的唯一出口（00 §5.3 路径铁律）：任何地方不得手拼绝对路径。
/// exe 目录取 <see cref="AppContext.BaseDirectory"/>（单文件发布下同样指向 exe 所在目录）。
///
/// **交付布局（2026-10-05 用户规格）**：<c>cship\</c> 内放 32/64 位主程序 exe，
/// 依赖与配置放在其下的 <c>cship\dependencies\</c>（exe 旁）。因此**整个 cship 目录自包含**：
/// 复制/移动到任意路径（含中文/空格、跨分区）都能运行【L77】，不依赖任何外部位置。
/// </summary>
public static class Paths
{
    public static string AppDir() => AppContext.BaseDirectory;

    /// <summary>依赖根 / 配置根：exe目录\dependencies\（与 exe 同级，随目录整体移动）。</summary>
    public static string DepsDir() => Path.Combine(AppDir(), "dependencies");

    public static string ConfigDir() => Path.Combine(DepsDir(), "config");
    public static string ResourcesDir() => Path.Combine(DepsDir(), "resources");
    public static string I18nDir() => Path.Combine(ResourcesDir(), "i18n");

    /// <summary>内置图标资源目录（resources\icons）。</summary>
    public static string IconsDir() => Path.Combine(ResourcesDir(), "icons");

    /// <summary>文件夹统一图标（2026-08-29 用户反馈：文件夹图标改用素材 文件夹.webp 转制 PNG）。</summary>
    public static string FolderIconFile() => Path.Combine(IconsDir(), "folder.png");
    public static string AssetsDir() => Path.Combine(ConfigDir(), "assets");
    public static string IconCacheDir() => Path.Combine(ConfigDir(), "iconcache");
    public static string LogsDir() => Path.Combine(ConfigDir(), "logs");

    public static string SettingsFile() => Path.Combine(ConfigDir(), "settings.json");
    public static string StateFile() => Path.Combine(ConfigDir(), "state.json");

    /// <summary>启动时确保 config / iconcache / assets / logs 四个目录存在（步骤 1.4）。</summary>
    public static void EnsureDirs()
    {
        foreach (var dir in new[] { ConfigDir(), AssetsDir(), IconCacheDir(), LogsDir() })
            Directory.CreateDirectory(dir);
    }
}
