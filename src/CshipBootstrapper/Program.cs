using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Cship.Bootstrapper
{
    /// <summary>
    /// 精简版引导器（精简版交付里用户双击的 Cship.exe / Cship-x86.exe 就是它）。
    ///
    /// 目标机器可能没有 .NET 8，所以本程序面向 net48（Win10/11 系统自带 .NET Framework 4.8），
    /// 职责只有三步：找到可用的 .NET 8 桌面运行时 →（必要时弹窗联网下载）→ 启动真正的主程序。
    ///
    /// 运行时查找顺序：
    ///   1) 本地目录 dependencies\runtime\{架构}\dotnet（引导器此前下载的，绿色、不进系统）；
    ///   2) 系统安装位置（%ProgramFiles%\dotnet，x86 看 ProgramFiles(x86)）；
    ///   3) 都没有 → 弹出下载窗体：从多个官方源下载"基础运行时 + WPF 框架"两个 zip，
    ///      叠加解压到本地目录（免管理员、免安装、零系统残留），然后注入 DOTNET_ROOT_X64/X86 启动主程序。
    ///
    /// 自身按文件名区分架构语义：文件名含 "x86"（即 Cship-x86.exe）管理 x86 主程序，否则管理 x64——
    /// 因此两个入口是同一份 AnyCPU 程序集改名而来，只需编译一次。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exePath = Application.ExecutablePath;
            string exeDir = AppDomain.CurrentDomain.SetupInformation.ApplicationBase
                            ?? Path.GetDirectoryName(exePath)
                            ?? AppContext.BaseDirectory;
            bool isX86 = Path.GetFileNameWithoutExtension(exePath)
                .IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0;
            string arch = isX86 ? "x86" : "x64";
            string envName = isX86 ? "DOTNET_ROOT_X86" : "DOTNET_ROOT_X64";
            string mainExe = Path.Combine(exeDir, isX86 ? "Cship.app-x86.exe" : "Cship.app.exe");
            string logPath = Path.Combine(exeDir, "dependencies", "runtime", "bootstrapper.log");

            if (!File.Exists(mainExe))
            {
                MessageBox.Show(
                    "主程序缺失：" + mainExe + Environment.NewLine +
                    "安装包不完整（可能解压时被拦截），请重新解压后再运行。",
                    "Cship", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            bool forceBootstrap = string.Equals(
                Environment.GetEnvironmentVariable("CSHIP_FORCE_BOOTSTRAP"), "1", StringComparison.Ordinal);

            // 1) 本地自带运行时（上次引导下载的）
            string localRoot = RuntimeLocator.LocalRuntimeRoot(exeDir, arch);
            if (!forceBootstrap && RuntimeLocator.HasDesktopRuntime(localRoot))
            {
                Log(logPath, "使用本地运行时：" + localRoot);
                return Launch(mainExe, exeDir, envName, localRoot, logPath);
            }

            // 2) 系统已安装（apphost 默认也能找到，无需注入环境变量）
            if (!forceBootstrap)
            {
                string sysRoot = RuntimeLocator.SystemRuntimeRoot(arch);
                if (RuntimeLocator.HasDesktopRuntime(sysRoot))
                {
                    Log(logPath, "使用系统运行时：" + sysRoot);
                    return Launch(mainExe, exeDir, null, null, logPath);
                }
            }

            // 3) 都没有 → 弹窗下载（forceBootstrap=1 时不走上面两个分支，用于强制重装/排障）
            Log(logPath, "未找到 .NET 8 桌面运行时，进入引导下载（arch=" + arch + (forceBootstrap ? "，强制" : "") + "）");
            using (var form = new BootstrapForm(exeDir, arch, envName, mainExe, logPath))
            {
                Application.Run(form);
            }
            return 0;
        }

        private static int Launch(string mainExe, string workDir, string envName, string rootDir, string logPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = mainExe,
                    UseShellExecute = false,
                    WorkingDirectory = workDir,
                };
                if (envName != null && rootDir != null)
                    psi.EnvironmentVariables[envName] = rootDir;
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                Log(logPath, "启动主程序失败：" + ex.Message);
                MessageBox.Show("无法启动主程序：" + ex.Message, "Cship",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>覆盖式小日志（排障用；只有引导阶段会写，不轮转）。</summary>
        public static void Log(string path, string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
                // 日志写不进去不影响功能（如目录只读）
            }
        }
    }
}
