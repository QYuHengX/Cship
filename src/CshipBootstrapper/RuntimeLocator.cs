using System;
using System.IO;

namespace Cship.Bootstrapper
{
    /// <summary>.NET 8 桌面运行时的两个落点（本地 / 系统）与"是否可用"判定。</summary>
    internal static class RuntimeLocator
    {
        /// <summary>引导器下载的运行时落点：exe 旁 dependencies\runtime\{架构}\dotnet（绿色，不进系统）。</summary>
        public static string LocalRuntimeRoot(string exeDir, string arch)
            => Path.Combine(exeDir, "dependencies", "runtime", arch, "dotnet");

        /// <summary>系统安装位置；x86 程序的安装目录在 Program Files (x86) 下的 dotnet。</summary>
        public static string SystemRuntimeRoot(string arch)
        {
            var folder = arch == "x86"
                ? Environment.SpecialFolder.ProgramFilesX86
                : Environment.SpecialFolder.ProgramFiles;
            string pf = Environment.GetFolderPath(folder);
            return Path.Combine(pf, "dotnet");
        }

        /// <summary>
        /// 可用判定：有 host\fxr\（apphost 启动必需）且 shared\Microsoft.WindowsDesktop.App\ 下存在 8.x 框架。
        /// 只认 8.x：主程序 TargetFramework=net8.0，RollForward 只在 8.0 线内补丁滚动，9/10 不适用。
        /// </summary>
        public static bool HasDesktopRuntime(string dotnetRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(dotnetRoot) || !Directory.Exists(dotnetRoot)) return false;

                string fxr = Path.Combine(dotnetRoot, "host", "fxr");
                if (!Directory.Exists(fxr) || Directory.GetDirectories(fxr).Length == 0) return false;

                string desktop = Path.Combine(dotnetRoot, "shared", "Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(desktop)) return false;

                foreach (string dir in Directory.GetDirectories(desktop))
                {
                    string name = Path.GetFileName(dir);
                    Version ver;
                    if (Version.TryParse(name, out ver) && ver.Major == 8) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
