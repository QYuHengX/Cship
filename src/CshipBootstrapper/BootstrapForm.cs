using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cship.Bootstrapper
{
    /// <summary>
    /// 首次运行引导窗体：从多个官方源下载"基础运行时 + WPF 框架"两个 zip，
    /// 叠加解压到 exe 旁 dependencies\runtime\{架构}\dotnet（绿色，不进系统），
    /// 校验后注入 DOTNET_ROOT_X64/X86 启动主程序。
    /// 任一源失败自动切下一个；"更换下载源"按钮可手动中断当前源立即换下一个。
    /// </summary>
    internal sealed class BootstrapForm : Form
    {
        private readonly string _exeDir, _arch, _envName, _mainExe, _logPath;
        private string _zipBase, _zipDesktop;

        private Label _title, _desc, _status;
        private Panel _bar;
        private Button _switchBtn, _cancelBtn;

        private volatile bool _cancelRequested;
        private volatile bool _switchRequested;
        private HttpWebRequest _activeRequest;

        private int _sourceIndex;            // 最近一次成功（或用户手动指定）的源，下一包优先用它
        private double _fraction;            // 总进度 0~1（两个包各占一半）
        private long _doneBytes, _totalBytes;
        private DateTime _lastUiAt = DateTime.MinValue;
        private DateTime _speedMark = DateTime.MinValue;
        private long _speedMarkBytes;
        private double _speedBps;
        private bool _running;

        public BootstrapForm(string exeDir, string arch, string envName, string mainExe, string logPath)
        {
            _exeDir = exeDir;
            _arch = arch;
            _envName = envName;
            _mainExe = mainExe;
            _logPath = logPath;

            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Cship 首次运行准备";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 218);
            BackColor = Color.FromArgb(32, 33, 36);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            _title = new Label
            {
                Text = "首次运行：需要获取 .NET 桌面运行时",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 18),
            };

            _desc = new Label
            {
                Text = "精简版不内置 .NET 8 运行时。将联网下载两个官方压缩包（共约 68 MB）并解压到程序目录："
                     + "不安装到系统、不写注册表、不需要管理员权限。完成后自动启动 Cship。",
                ForeColor = Color.FromArgb(178, 182, 188),
                AutoSize = false,
                Location = new Point(24, 54),
                Size = new Size(472, 56),
            };

            _bar = new Panel
            {
                Location = new Point(24, 118),
                Size = new Size(472, 10),
                BackColor = Color.FromArgb(58, 61, 66),
            };
            _bar.Paint += OnBarPaint;

            _status = new Label
            {
                Text = "准备中…",
                ForeColor = Color.FromArgb(140, 200, 255),
                AutoSize = false,
                Location = new Point(24, 138),
                Size = new Size(472, 20),
            };

            _switchBtn = MakeButton("更换下载源", new Point(24, 172), 116, OnSwitchSource);
            _cancelBtn = MakeButton("取消", new Point(148, 172), 80, (_, __) => { _cancelRequested = true; Close(); });

            Controls.AddRange(new Control[] { _title, _desc, _bar, _status, _switchBtn, _cancelBtn });
        }

        private Button MakeButton(string text, Point location, int width, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(width, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 55, 60),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 94, 100);
            b.Click += onClick;
            return b;
        }

        private void OnBarPaint(object sender, PaintEventArgs e)
        {
            var r = _bar.ClientRectangle;
            e.Graphics.Clear(_bar.BackColor);
            int w = (int)(r.Width * Math.Min(1.0, Math.Max(0.0, _fraction)));
            if (w > 0)
            using (var brush = new SolidBrush(Color.FromArgb(61, 209, 132)))
                e.Graphics.FillRectangle(brush, 0, 0, w, r.Height);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ = RunAsync();
        }

        // ---------- 主流程 ----------

        private async Task RunAsync()
        {
            if (_running) return;
            _running = true;
            _cancelRequested = false;
            _switchRequested = false;
            _switchBtn.Text = "更换下载源";
            try
            {
                SetStatus("正在查询 .NET 8 最新版本…");
                string version = await GetLatestVersionAsync();
                Program.Log(_logPath, "目标运行时版本：" + version);

                _zipBase = Path.Combine(Path.GetTempPath(), "cship-runtime-" + _arch + "-base.zip");
                _zipDesktop = Path.Combine(Path.GetTempPath(), "cship-runtime-" + _arch + "-desktop.zip");
                try
                {
                    await DownloadPackageAsync(0, version, _zipBase);
                    if (_cancelRequested) throw new OperationCanceledException();
                    await DownloadPackageAsync(1, version, _zipDesktop);
                    if (_cancelRequested) throw new OperationCanceledException();

                    SetStatus("正在解压到程序目录…");
                    _fraction = 0.94;
                    _bar.Invalidate();
                    await Task.Run(() => Extract());
                    if (_cancelRequested) throw new OperationCanceledException();

                    SetStatus("完成，正在启动 Cship…");
                    _fraction = 1.0;
                    _bar.Invalidate();
                    Program.Log(_logPath, "运行时就绪：" + RuntimeLocator.LocalRuntimeRoot(_exeDir, _arch));
                    LaunchMain();
                }
                finally
                {
                    TryDelete(_zipBase);
                    TryDelete(_zipDesktop);
                }
                Close();
            }
            catch (OperationCanceledException)
            {
                // 用户取消，窗体已在关闭流程中
            }
            catch (Exception ex)
            {
                if (_cancelRequested || IsDisposed) return;
                Program.Log(_logPath, "引导失败：" + ex.Message);
                _running = false;
                _switchBtn.Text = "重试";
                SetStatus("失败：" + ex.Message);
                MessageBox.Show(
                    "运行时获取失败：" + ex.Message + Environment.NewLine + Environment.NewLine +
                    "可检查网络后点「重试」或「更换下载源」；网络不可用时，请改用完整版（离线、免下载）。",
                    "Cship", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task<string> GetLatestVersionAsync()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(KnownSources.MetaUrl);
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;
                _activeRequest = req;
                using (var resp = await req.GetResponseAsync())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = await reader.ReadToEndAsync();
                    Match m = Regex.Match(json, "\"latest-runtime\"\\s*:\\s*\"([0-9.]+)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                Program.Log(_logPath, "版本查询失败，使用内置版本：" + ex.Message);
            }
            return KnownSources.FallbackVersion;
        }

        private async Task DownloadPackageAsync(int pkgIndex, string version, string destPath)
        {
            string[] templates = KnownSources.Packages[pkgIndex];
            int start = Math.Min(Math.Max(_sourceIndex, 0), templates.Length - 1);
            _switchRequested = false;
            Exception last = null;
            for (int i = 0; i < templates.Length; i++)
            {
                int idx = (start + i) % templates.Length;
                string url = string.Format(templates[idx], version, _arch);
                try
                {
                    SetStatus("下载源 " + (idx + 1) + "/" + templates.Length + " · " + HostOf(url));
                    _fraction = pkgIndex * 0.5;
                    _bar.Invalidate();
                    await DownloadAsync(url, destPath, pkgIndex);
                    _sourceIndex = idx;
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (_cancelRequested) throw new OperationCanceledException();
                    last = ex;
                    Program.Log(_logPath, (_switchRequested ? "手动切换源（" : "源失败（") + HostOf(url) + "）：" + ex.Message);
                    _switchRequested = false;
                    SetStatus("源 " + (idx + 1) + " 失败，切换下一个…");
                    if (!_switchRequested) await Task.Delay(300);
                }
            }
            throw new InvalidOperationException("所有下载源均失败：" + (last != null ? last.Message : "未知原因"), last);
        }

        private async Task DownloadAsync(string url, string destPath, int pkgIndex)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 20000;
            req.ReadWriteTimeout = 60000;
            req.UserAgent = "Cship-Bootstrapper/0.1";
            _activeRequest = req;

            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            using (Stream src = resp.GetResponseStream())
            using (var dst = File.Create(destPath, 81920, FileOptions.Asynchronous))
            {
                long total = resp.ContentLength;      // 官方 CDN 会给出，chunked 时为 -1
                _totalBytes = total;
                _doneBytes = 0;
                _speedMark = DateTime.UtcNow;
                _speedMarkBytes = 0;
                _speedBps = 0;

                var buffer = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, n);
                    _doneBytes += n;
                    UpdateProgress(pkgIndex, total);
                }
            }

            long got = new FileInfo(destPath).Length;
            if (got < 20L * 1024 * 1024)
                throw new IOException("下载不完整（仅 " + got / 1024 + " KB）");
        }

        private void UpdateProgress(int pkgIndex, long total)
        {
            double pkgFraction = total > 0 ? Math.Min(1.0, (double)_doneBytes / total) : 0.5;
            _fraction = pkgIndex * 0.5 + pkgFraction * 0.5;

            DateTime now = DateTime.UtcNow;
            if ((now - _speedMark).TotalSeconds >= 0.7)
            {
                _speedBps = (_doneBytes - _speedMarkBytes) / (now - _speedMark).TotalSeconds;
                _speedMark = now;
                _speedMarkBytes = _doneBytes;
            }
            if ((now - _lastUiAt).TotalMilliseconds < 180) return;
            _lastUiAt = now;

            string progressText = total > 0
                ? (_doneBytes / 1048576.0).ToString("0.0") + " / " + (total / 1048576.0).ToString("0.0") + " MB"
                : (_doneBytes / 1048576.0).ToString("0.0") + " MB";
            SetStatus("下载中 · " + progressText + " · " + (_speedBps / 1048576.0).ToString("0.0") + " MB/s"
                + "    （点「更换下载源」可立即换源）");
            _bar.Invalidate();
        }

        private void Extract()
        {
            string rtRoot = Path.Combine(_exeDir, "dependencies", "runtime", _arch);
            string staging = Path.Combine(rtRoot, ".staging");
            string final = Path.Combine(rtRoot, "dotnet");

            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            // 两个包叠加解压：基础运行时给 host\fxr + Microsoft.NETCore.App，
            // 桌面包给 Microsoft.WindowsDesktop.App。逐条覆盖式解压（两包的许可文件同名）。
            foreach (string zip in new[] { _zipBase, _zipDesktop })
            {
                using (var archive = ZipFile.OpenRead(zip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;   // 纯目录条目，建目录即可跳过
                        string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                        if (!target.StartsWith(Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase))
                            continue;                                     // 防 zip 路径穿越
                        string dir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        entry.ExtractToFile(target, true);
                    }
                }
            }

            if (!RuntimeLocator.HasDesktopRuntime(staging))
                throw new IOException("解压校验未通过（缺 host\\fxr 或 Microsoft.WindowsDesktop.App 8.x）");

            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.Move(staging, final);
        }

        private void LaunchMain()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _mainExe,
                UseShellExecute = false,
                WorkingDirectory = _exeDir,
            };
            psi.EnvironmentVariables[_envName] = RuntimeLocator.LocalRuntimeRoot(_exeDir, _arch);
            Process.Start(psi);
        }

        // ---------- UI 小工具 ----------

        private void OnSwitchSource(object sender, EventArgs e)
        {
            if (_running)
            {
                // 中断当前下载 → DownloadPackageAsync 视为源失败，自动尝试下一个
                _switchRequested = true;
                try { if (_activeRequest != null) _activeRequest.Abort(); }
                catch { /* 忽略 */ }
            }
            else
            {
                // 失败后的"重试"：从头再来
                _ = RunAsync();
            }
        }

        private void SetStatus(string text)
        {
            if (!IsDisposed) _status.Text = text;
        }

        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* 临时文件删不掉不影响功能 */ }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cancelRequested = true;
            try { if (_activeRequest != null) _activeRequest.Abort(); }
            catch { /* 忽略 */ }
            base.OnFormClosing(e);
        }
    }
}
