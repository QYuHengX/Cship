using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Cship.Bootstrapper
{
    /// <summary>
    /// 精简版引导器（精简版交付里用户双击的 Cship.exe / Cship-x86.exe 就是它，**全目录仅此两个 exe**）。
    ///
    /// 目标机器可能没有 .NET 8，所以本程序面向 net48（Win10/11 系统自带 .NET Framework 4.8）。
    /// 主程序（framework-dependent 单文件）作为嵌入资源打进本程序集，职责：
    ///   1) 按需把内嵌的主程序释放到 dependencies\app\（hash 比对，仅在变化时写入）；
    ///   2) 找到可用的 .NET 8 桌面运行时（本地 dependencies\runtime\{架构}\dotnet → 系统安装位），
    ///      都没有则弹窗联网下载（多源 + 进度条）；
    ///   3) 注入 DOTNET_ROOT_X64/X86 启动主程序，并转发自身命令行参数（如开机自启的 --autostart）。
    ///
    /// 自身按文件名区分架构语义：文件名含 "x86"（即 Cship-x86.exe）管理 x86 主程序，否则管理 x64——
    /// 因此两个入口是同一份 AnyCPU 程序集改名而来，只需各嵌入对应架构的主程序后分别输出。
    /// </summary>
    internal static class Program
    {
        /// <summary>主程序嵌入资源名（csproj 的 LogicalName，由 build.ps1 以 -p:MainAppExe 注入文件）。</summary>
        const string EmbeddedResourceName = "Cship.AppExe";

        [STAThread]
        private static int Main(string[] args)
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
            string appExe = AppTargetPath(exeDir, isX86);
            string forward = JoinArgs(args);
            string logPath = Path.Combine(exeDir, "dependencies", "runtime", "bootstrapper.log");

            // 0) 主程序就位（内嵌资源释放；运行中被文件锁则沿用现有文件，单实例机制会去激活）
            try
            {
                EnsureAppExe(exeDir, isX86, logPath);
            }
            catch (Exception ex)
            {
                Program.Log(logPath, "释放主程序失败：" + ex.Message);
                MessageBox.Show(
                    "无法就位主程序：" + ex.Message + Environment.NewLine +
                    "若反复出现，请重新解压安装包。",
                    "Cship", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            bool forceBootstrap = string.Equals(
                Environment.GetEnvironmentVariable("CSHIP_FORCE_BOOTSTRAP"), "1", StringComparison.Ordinal);

            // 1) 本地自带运行时（此前引导下载的）
            string localRoot = RuntimeLocator.LocalRuntimeRoot(exeDir, arch);
            if (!forceBootstrap && RuntimeLocator.HasDesktopRuntime(localRoot))
            {
                Program.Log(logPath, "使用本地运行时：" + localRoot);
                return Launch(appExe, exeDir, envName, localRoot, forward, logPath);
            }

            // 2) 系统已安装（apphost 默认也能找到，无需注入环境变量）
            if (!forceBootstrap)
            {
                string sysRoot = RuntimeLocator.SystemRuntimeRoot(arch);
                if (RuntimeLocator.HasDesktopRuntime(sysRoot))
                {
                    Program.Log(logPath, "使用系统运行时：" + sysRoot);
                    return Launch(appExe, exeDir, null, null, forward, logPath);
                }
            }

            // 3) 都没有 → 弹窗下载（forceBootstrap=1 时不走上面两个分支，用于强制重装/排障）
            Program.Log(logPath, "未找到 .NET 8 桌面运行时，进入引导下载（arch=" + arch + (forceBootstrap ? "，强制" : "") + "）");
            using (var form = new BootstrapForm(exeDir, arch, envName, appExe, forward, logPath))
            {
                Application.Run(form);
            }
            return 0;
        }

        /// <summary>释放目标：dependencies\app\Cship.app[.x86].exe（主程序的 deps 由 Paths 向上回溯到程序根）。</summary>
        internal static string AppTargetPath(string exeDir, bool isX86)
            => Path.Combine(exeDir, "dependencies", "app", isX86 ? "Cship.app-x86.exe" : "Cship.app.exe");

        /// <summary>内嵌主程序 → 目标文件。hash 一致则跳过；目标被锁（正在运行）则沿用现有文件。</summary>
        static void EnsureAppExe(string exeDir, bool isX86, string logPath)
        {
            string target = AppTargetPath(exeDir, isX86);
            byte[] embedded = ReadEmbeddedMainExe();

            if (File.Exists(target) && SameBytes(target, embedded))
            {
                Program.Log(logPath, "主程序已是当前版本，跳过释放");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            try
            {
                File.WriteAllBytes(target, embedded);
                Program.Log(logPath, "已释放主程序：" + target + "（" + embedded.Length + " 字节）");
            }
            catch (IOException ex)
            {
                if (!File.Exists(target)) throw;
                // 主程序正在运行：Windows 锁定运行中的 exe 无法覆写。沿用现有文件——
                // 启动会命中单实例互斥并激活已有实例；真正升级由用户退出主程序后下次引导完成。
                Program.Log(logPath, "释放被文件锁拦截（主程序可能在运行），沿用现有文件：" + ex.Message);
            }
        }

        static byte[] ReadEmbeddedMainExe()
        {
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "引导器未内嵌主程序（精简版必须经 build\\build.ps1 构建，主程序以 -p:MainAppExe 注入）");
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        static bool SameBytes(string file, byte[] expected)
        {
            try
            {
                byte[] actual;
                using (var fs = File.OpenRead(file))
                using (var sha = SHA256.Create())
                    actual = sha.ComputeHash(fs);
                using (var ms = new MemoryStream(expected))
                using (var sha = SHA256.Create())
                {
                    byte[] want = sha.ComputeHash(ms);
                    if (actual.Length != want.Length) return false;
                    for (int i = 0; i < actual.Length; i++)
                        if (actual[i] != want[i]) return false;
                    return true;
                }
            }
            catch
            {
                return false;   // 读不动（锁/权限）→ 视为不同，走释放路径再按锁策略处理
            }
        }

        static int Launch(string appExe, string workDir, string envName, string rootDir, string forward, string logPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = appExe,
                    UseShellExecute = false,
                    WorkingDirectory = workDir,
                    Arguments = forward,
                };
                if (envName != null && rootDir != null)
                    psi.EnvironmentVariables[envName] = rootDir;
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                Program.Log(logPath, "启动主程序失败：" + ex.Message);
                MessageBox.Show("无法启动主程序：" + ex.Message, "Cship",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>转发命令行（如开机自启的 --autostart）；含空格的参数加引号。</summary>
        static string JoinArgs(string[] args)
        {
            if (args == null || args.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a.IndexOf(' ') >= 0) sb.Append('"').Append(a).Append('"');
                else sb.Append(a);
            }
            return sb.ToString();
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
