using System;
using System.IO;
namespace Cship.Core;

/// <summary>
/// 全项目磁盘路径的唯一出口（00 §5.3 路径铁律）：任何地方不得手拼绝对路径。
/// exe 目录取 <see cref="AppContext.BaseDirectory"/>（单文件发布下同样指向 exe 所在目录）。
///
/// **交付布局**：
/// 完整版 —— exe 与 <c>dependencies\</c> 同级（exe旁命中，常规布局）；
/// 精简版 —— 引导器（用户双击的 Cship.exe/Cship-x86.exe）在程序根，主程序由引导器
/// 释放到 <c>dependencies\app\</c> 下，exe 旁没有 dependencies，此时 <see cref="DepsDir"/>
/// 向上回溯到程序根的 <c>dependencies\</c>——**整个程序仍自包含**，config 与完整版同一套结构
/// （两档间迁移设置无需改动，决策 302）。所有分支都收敛在这一个出口，调用方无感知。
/// </summary>
public static class Paths
{
    public static string AppDir() => AppContext.BaseDirectory;

    /// <summary>
    /// 精简版形态判定：主程序 exe 旁没有 dependencies 目录（它被引导器释放到了 dependencies\app\）。
    /// 完整版与开发态恒为 false。
    /// </summary>
    public static bool IsDetachedApp
    {
        get
        {
            try { return !Directory.Exists(Path.Combine(AppDir(), "dependencies")); }
            catch { return false; }
        }
    }

    /// <summary>
    /// 程序根（引导器所在目录）：完整版 = exe 所在目录；
    /// 精简版 = AppDir 的上两级（.../程序根/dependencies/app → .../程序根）。
    /// </summary>
    public static string ProgramRoot()
    {
        if (!IsDetachedApp) return AppDir();
        try
        {
            string deps = Path.GetFullPath(Path.Combine(AppDir(), ".."));
            string root = Path.GetDirectoryName(deps);
            if (!string.IsNullOrEmpty(root)) return root;
        }
        catch { /* 回退 AppDir */ }
        return AppDir();
    }

    /// <summary>
    /// 自启动 / 重启应使用的入口 exe：完整版 = 自身（self-contained，直接可跑）；
    /// 精简版 = 程序根的引导器（Cship.exe / Cship-x86.exe 按自身架构对应）——
    /// 只有引导器知道本地运行时在哪（DOTNET_ROOT 注入），直接启动主程序会因找不到运行时失败。
    /// </summary>
    public static string LauncherExe()
    {
        string self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || !IsDetachedApp)
            return self ?? Path.Combine(AppDir(), "Cship.exe");
        bool x86 = Path.GetFileNameWithoutExtension(self)
            .IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0;
        return Path.Combine(ProgramRoot(), x86 ? "Cship-x86.exe" : "Cship.exe");
    }

    /// <summary>
    /// 依赖根 / 配置根：优先 exe目录\dependencies\（完整版、开发态、引导器布局）；
    /// 精简版主程序（释放于 dependencies\app\）向上回溯到程序根的 dependencies\（须含 resources 才算命中）。
    /// </summary>
    public static string DepsDir()
    {
        var beside = Path.Combine(AppDir(), "dependencies");
        if (Directory.Exists(beside)) return beside;
        try
        {
            var dir = AppDir();
            for (int i = 0; i < 6 && dir != null; i++)
            {
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
                var cand = Path.Combine(dir, "dependencies");
                if (Directory.Exists(cand) && Directory.Exists(Path.Combine(cand, "resources")))
                    return cand;
            }
        }
        catch { /* 任何异常回退 beside（原行为） */ }
        return beside;
    }

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
