using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;
using Cship.Models;

namespace Cship.Core;

/// <summary>
/// 桌面枚举（步骤 2.1）：用户桌面 + 公共桌面（同名用户优先），排除隐藏/系统属性与 desktop.ini；
/// 类型判定（.exe/.bat/.cmd→apps；.lnk 解析目标后判定；商店/协议类 lnk 与非 http(s) 协议 .url→apps，
/// 清单三·任务16）；图标提取进 config\iconcache（sha1(path+mtime)）；
/// FileSystemWatcher 防抖 500ms 后经 RefreshRequested（UI 线程）通知刷新。
/// </summary>
public sealed class DesktopScanner : IDisposable
{
    /// <summary>桌面内容变化（防抖 500ms 后在 UI 线程触发）。</summary>
    public event Action? RefreshRequested;

    public static string UserDesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    static string? _commonDir;

    /// <summary>公共桌面：HKLM\...\Shell Folders 的 "Common Desktop" 值（读不到则空）。</summary>
    public static string CommonDesktopDir
    {
        get
        {
            if (_commonDir != null) return _commonDir;
            _commonDir = "";
            try
            {
                _commonDir = (ReadShellFolders(RegistryView.Default) ?? ReadShellFolders(RegistryView.Registry64)) ?? "";
            }
            catch (Exception ex)
            {
                Logger.Warn($"读取公共桌面路径失败：{ex.Message}");
            }
            return _commonDir;
        }
    }

    static string? ReadShellFolders(RegistryView view)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders");
        return key?.GetValue("Common Desktop") as string;
    }

    readonly DispatcherTimer _debounce;
    readonly List<FileSystemWatcher> _watchers = new();
    bool _disposed;
    int _fsEventPending; // 批量文件事件合并投递标志（见 OnFsEvent）

    public DesktopScanner()
    {
        // 优先级 Normal（07 实测教训）：默认 Background 在大桌面刷新（数百枚容器同时果冻入场 +
        // 解码）期间会被渲染工作饿死——实测批量删除 500 文件后重扫被推迟到 8.4s；提到 Normal 后
        // 计时器按点触发，重扫及时。tick 本身只做一次事件转发（扫描在 Task.Run 里），不会卡渲染。
        _debounce = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RefreshRequested?.Invoke();
        };
        SetupWatcher(UserDesktopDir);
        var common = CommonDesktopDir;
        if (!string.IsNullOrEmpty(common) && Directory.Exists(common)
            && !string.Equals(common, UserDesktopDir, StringComparison.OrdinalIgnoreCase))
            SetupWatcher(common);
    }

    /// <summary>全量扫描（图标缓存命中的开销很小，可放后台线程）。</summary>
    public List<BoardItem> Scan()
    {
        var result = new List<BoardItem>();
        var userItems = EnumerateDir(UserDesktopDir);
        var commonItems = EnumerateDir(CommonDesktopDir);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in userItems)
        {
            result.Add(item);
            seen.Add(item.Name);
        }
        foreach (var item in commonItems)
            if (seen.Add(item.Name)) // 同名（不分大小写）用户桌面优先【2.1】
                result.Add(item);
        return result;
    }

    static List<BoardItem> EnumerateDir(string dir)
    {
        var list = new List<BoardItem>();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
        DirectoryInfo root;
        try
        {
            root = new DirectoryInfo(dir);
        }
        catch
        {
            return list;
        }
        FileSystemInfo[] entries;
        try
        {
            entries = root.GetFileSystemInfos();
        }
        catch (Exception ex)
        {
            Logger.Warn($"桌面目录枚举失败 {dir}：{ex.Message}");
            return list;
        }
        foreach (var fsi in entries)
        {
            try
            {
                var attrs = fsi.Attributes;
                if (attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System)) continue;
                if (string.Equals(fsi.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(BuildItem(fsi));
            }
            catch (Exception ex)
            {
                Logger.Warn($"桌面项跳过 {fsi.Name}：{ex.Message}");
            }
        }
        return list;
    }

    static BoardItem BuildItem(FileSystemInfo fsi)
    {
        bool isDir = fsi is DirectoryInfo;
        string target = "";
        var type = Classify(fsi.FullName, isDir, ref target);
        var item = new BoardItem
        {
            Name = fsi.Name,
            Path = fsi.FullName,
            Type = type,
            TargetPath = target,
            Mtime = fsi.LastWriteTime,
            SizeBytes = isDir ? null : ((FileInfo)fsi).Length,
        };
        item.IconPath = GetIconCachePath(item);
        return item;
    }

    /// <summary>为任意路径构建陈列项（拖入收纳的"仅收纳板快捷方式"用，2026-08-29 批次六）。
    /// 源不存在也能构建（图标/大小缺失降级），调用方自行设置 IsVirtual。</summary>
    public static BoardItem BuildFromPath(string path)
    {
        bool isDir = Directory.Exists(path);
        string target = "";
        var type = Classify(path, isDir, ref target);
        var item = new BoardItem
        {
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Path = path,
            Type = type,
            TargetPath = target,
        };
        if (isDir)
        {
            item.Mtime = SafeMtime(path);
        }
        else if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            item.Mtime = fi.LastWriteTime;
            item.SizeBytes = fi.Length;
        }
        if (string.IsNullOrEmpty(item.Name))
            item.Name = path;
        item.IconPath = GetIconCachePath(item);
        return item;
    }

    static DateTime SafeMtime(string dir)
    {
        try { return new DirectoryInfo(dir).LastWriteTime; }
        catch { return DateTime.MinValue; }
    }

    static BoardItemType Classify(string path, bool isDir, ref string lnkTarget)
    {
        if (isDir) return BoardItemType.Folders;
        var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
        if (ext is ".exe" or ".bat" or ".cmd") return BoardItemType.Apps;
        if (ext == ".lnk")
        {
            lnkTarget = ResolveLnk(path);
            if (!string.IsNullOrEmpty(lnkTarget))
            {
                if (Directory.Exists(lnkTarget)) return BoardItemType.Folders;
                var t = Path.GetExtension(lnkTarget)?.ToLowerInvariant() ?? "";
                if (t is ".exe" or ".bat" or ".cmd") return BoardItemType.Apps;
                // 商店/协议类快捷方式（清单三·任务16）：shell: 伪路径、AppsFolder 枚举、
                // @{...} 商店框架包标识、WindowsApps 目录下的目标 → 软件
                if (IsStoreLikeTarget(lnkTarget)) return BoardItemType.Apps;
            }
            // UWP/商店快捷方式（清单三修订A·问题3）：TargetPath 经 COM 解析常为**空**
            // （微软商店、Minecraft Launcher 等），AUMID 藏在 lnk 的 IDList/字符串段——字节扫描识别
            if (LnkLooksLikeStore(path)) return BoardItemType.Apps;
        }
        if (ext == ".url")
        {
            // .url 书签（清单三·任务16）：steam 等建的是 steam://rungameid/... 协议链——
            // 协议非 http(s) → 软件；网页书签仍归文件
            var url = ReadUrlTarget(path);
            if (!string.IsNullOrEmpty(url)
                && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return BoardItemType.Apps;
        }
        return BoardItemType.Files;
    }

    /// <summary>lnk 目标是否为商店/协议类（清单三·任务16）。</summary>
    static bool IsStoreLikeTarget(string target)
    {
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
        if (target.StartsWith("@{", StringComparison.OrdinalIgnoreCase)) return true; // 商店框架包
        if (target.Contains("AppsFolder", StringComparison.OrdinalIgnoreCase)) return true;
        return target.Contains($"{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
            || target.Contains("/WindowsApps/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// lnk 字节级商店特征扫描（清单三修订A·问题3）：UWP 快捷方式（微软商店/Minecraft Launcher 等）
    /// 的 TargetPath 经 COM 解析为空，AUMID（如 Microsoft.WindowsStore_8wekyb3d8bbwe!App）
    /// 藏在 lnk 的 IDList/字符串段——按 UTF-16LE 与 ASCII 解码后找特征标记。
    /// </summary>
    static bool LnkLooksLikeStore(string path)
    {
        try
        {
            // 只读前 64KB：lnk 的 AUMID/IDList 特征都在头部，原实现 File.ReadAllBytes 会把
            // 异常大的快捷方式整读进来再做两份全量字符串解码（UTF-16 + ASCII 各放大一份）。
            const int maxBytes = 64 * 1024;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
            int len = (int)Math.Min(fs.Length, maxBytes);
            if (len <= 0) return false;
            var bytes = new byte[len];
            int read = fs.Read(bytes, 0, len);
            if (read != len) Array.Resize(ref bytes, read);
            string utf16 = System.Text.Encoding.Unicode.GetString(bytes);
            string ascii = System.Text.Encoding.ASCII.GetString(bytes);
            foreach (var probe in new[] { "AppsFolder", "_8wekyb3d8bbwe", "!App", "WindowsApps", "shell:", "@{" })
            {
                if (utf16.Contains(probe, StringComparison.OrdinalIgnoreCase)
                    || ascii.Contains(probe, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"lnk 商店特征扫描失败 {path}：{ex.Message}");
        }
        return false;
    }

    /// <summary>读 .url 文本找 URL= 行（steam 创建的协议链快捷方式；失败返回 null 归 files）。</summary>
    static string? ReadUrlTarget(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var t = line.Trim();
                if (t.StartsWith("URL=", StringComparison.OrdinalIgnoreCase) && t.Length > 4)
                    return t[4..].Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"url 解析失败 {path}：{ex.Message}");
        }
        return null;
    }

    /// <summary>WScript.Shell 的 COM 类型（ProgID→CLSID 注册表解析一次即可，全进程复用）。</summary>
    static Type? _wshType;
    static bool _wshTypeProbed;

    static Type? WshShellType()
    {
        if (_wshTypeProbed) return _wshType;
        _wshTypeProbed = true;
        try { _wshType = Type.GetTypeFromProgID("WScript.Shell"); }
        catch { _wshType = null; }
        return _wshType;
    }

    /// <summary>.lnk 目标解析（动态 COM WScript.Shell，零引用；失败返回空串归 files）。</summary>
    static string ResolveLnk(string lnkPath)
    {
        object? wsh = null, sc = null;
        try
        {
            var type = WshShellType();
            if (type == null) return "";
            wsh = Activator.CreateInstance(type)!;
            dynamic shell = wsh;
            sc = shell.CreateShortcut(lnkPath);
            dynamic shortcut = sc;
            string target = shortcut.TargetPath ?? "";
            return target;
        }
        catch (Exception ex)
        {
            Logger.Warn($"lnk 解析失败 {lnkPath}：{ex.Message}");
            return "";
        }
        finally
        {
            if (sc != null) Marshal.ReleaseComObject(sc);
            if (wsh != null) Marshal.ReleaseComObject(wsh);
        }
    }

    /// <summary>图标缓存：config\iconcache\&lt;sha1(path+mtime)&gt;.png，命中不重提取。
    /// 文件夹不进缓存：统一用内置 resources\icons\folder.png（2026-08-29 用户反馈），
    /// 资源缺失时回退系统提取。缓存键带 v5 盐：v4→v5 因"半透明均匀底小内容裁切"补全（2026-10-01）；v4 曾治不透明白底小内容
    /// （2026-10-01，治图吧工具箱/微星小飞机类过小图标）；v2 曾把"工厂通道返回的通用
    /// 空白页图标"当成功缓存（.url/.txt 等），升盐全量重提取。</summary>
    // ---- 07 §4 图标提取：单消费者后台队列 + 渐次回填 ----
    // 旧实现：`Scan()` 在后台线程里**同步**逐个提取图标（每项一次 shell COM + 写盘），
    // 冷缓存下 500 项 ≈ 6.6s 才有结果 → 板要等全部图标提完才出内容，"图标渐次补齐"不成立。
    // 新实现：Scan() 只算**确定性缓存路径**并立即返回；未命中的项排入单消费者队列，
    // 由一条 BelowNormal 后台线程逐个提取、写盘，完成后按名字广播 `IconReady` → UI 只重载那一枚。
    // 收益：① 板立即出内容、图标渐次补齐；② shell 调用天然串行（不会瞬间打爆 COM/磁盘队列）；
    // ③ 拖入/建快捷方式路径也不再在 UI 线程碰 shell。

    readonly record struct IconJob(string Name, string SourcePath, string CachePath, bool IsDir);

    static readonly System.Collections.Concurrent.BlockingCollection<IconJob> IconQueue = new();
    static readonly HashSet<string> IconInFlight = new(StringComparer.OrdinalIgnoreCase); // 在排队/提取中
    static readonly HashSet<string> IconFailed = new(StringComparer.OrdinalIgnoreCase);   // 提取失败（不无限重试）
    static readonly object IconGate = new();
    static int _iconWorkerStarted;

    /// <summary>某枚图标提取完成并已写盘（07 §4）：参数=条目名。后台线程触发，消费方自行调度到 UI。</summary>
    public static event Action<string>? IconReady;

    static void EnsureIconWorker()
    {
        if (Interlocked.Exchange(ref _iconWorkerStarted, 1) == 1) return;
        var thread = new Thread(IconWorkerLoop)
        {
            IsBackground = true,               // 进程退出不阻塞
            Name = "CshipIconExtract",
            Priority = ThreadPriority.BelowNormal, // 让位给 UI/渲染
        };
        thread.Start();
        Logger.Info("图标提取后台队列已启动（单消费者，渐次回填）");
    }

    static void IconWorkerLoop()
    {
        foreach (var job in IconQueue.GetConsumingEnumerable())
        {
            try
            {
                if (File.Exists(job.CachePath) && new FileInfo(job.CachePath).Length > 0)
                {
                    RaiseIconReady(job.Name); // 排队期间已被别的路径补齐
                    continue;
                }
                var png = SystemBridge.ExtractIconPng(job.SourcePath, job.IsDir);
                if (png == null || png.Length == 0)
                {
                    lock (IconGate) IconFailed.Add(job.CachePath);
                    Logger.Warn($"图标提取无结果（该项按空图标显示）：{job.SourcePath}");
                    continue;
                }
                File.WriteAllBytes(job.CachePath, png);
                RaiseIconReady(job.Name);
            }
            catch (Exception ex)
            {
                lock (IconGate) IconFailed.Add(job.CachePath);
                Logger.Warn($"图标提取失败 {job.SourcePath}：{ex.Message}");
            }
            finally
            {
                lock (IconGate) IconInFlight.Remove(job.CachePath);
            }
        }
    }

    static void RaiseIconReady(string name)
    {
        try { IconReady?.Invoke(name); }
        catch (Exception ex) { Logger.Warn($"图标就绪广播失败 {name}：{ex.Message}"); }
    }

    /// <summary>排入提取队列（去重：在飞/已失败的不重复排）。</summary>
    static void EnqueueIcon(string name, string sourcePath, string cachePath, bool isDir)
    {
        lock (IconGate)
        {
            if (IconFailed.Contains(cachePath)) return;
            if (!IconInFlight.Add(cachePath)) return;
        }
        EnsureIconWorker();
        IconQueue.Add(new IconJob(name, sourcePath, cachePath, isDir));
    }

    /// <summary>
    /// 图标缓存路径：命中即返回；未命中返回**确定性路径**并把提取排入后台队列（文件稍后才出现）。
    /// 路径 = config\iconcache\&lt;sha1(v7|path|mtime)&gt;.png（v7 见下方盐值说明）。
    /// 文件夹不进缓存：统一用内置 resources\icons\folder.png。
    /// </summary>
    static string GetIconCachePath(BoardItem item)
    {
        try
        {
            if (item.Type == BoardItemType.Folders)
            {
                string builtin = Paths.FolderIconFile();
                if (File.Exists(builtin)) return builtin;
            }
            string key = Sha1Hex($"v7|{item.Path}|{item.Mtime.Ticks}"); // v7：镂空边框内容扫描内缩 1/16 排除贴边不透明细线
            string cache = Path.Combine(Paths.IconCacheDir(), key + ".png");
            if (File.Exists(cache) && new FileInfo(cache).Length > 0)
                return cache; // 命中：零 shell 调用
            EnqueueIcon(item.Name, item.Path, cache, item.Type == BoardItemType.Folders);
            return cache; // 未就绪也返回该路径：UI 先拿到空图标，IconReady 到达后重载
        }
        catch (Exception ex)
        {
            Logger.Warn($"图标缓存路径失败 {item.Path}：{ex.Message}");
            return "";
        }
    }

    static string Sha1Hex(string text)
    {
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        var sb = new System.Text.StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    void SetupWatcher(string dir)
    {
        try
        {
            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = false,
                // 64KB 缓冲（07 §5）：默认 8KB 在大桌面批量操作（百文件复制/删除）时溢出，
                // 溢出即丢事件 → 列表与实际不一致。
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += OnFsEvent;
            watcher.Deleted += OnFsEvent;
            watcher.Changed += OnFsEvent;
            watcher.Renamed += OnFsEvent;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            Logger.Warn($"桌面监视启动失败 {dir}：{ex.Message}");
        }
    }

    /// <summary>监视器内部错误（典型=缓冲溢出）：不停用监听（原实现一停就永久失明），
    /// 改为触发一次全量重扫把丢失的变化补回来（07 §5）。</summary>
    void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Logger.Warn($"桌面监视缓冲异常，触发全量重扫：{e.GetException().Message}");
        RequestRefreshFromWatcherThread();
    }

    /// <summary>从 FileSystemWatcher 内部线程安全地请求一次防抖刷新（回 UI 线程）。</summary>
    void RequestRefreshFromWatcherThread()
    {
        try
        {
            _debounce.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                _debounce.Stop();
                _debounce.Start();
            });
        }
        catch
        {
            // 应用退出、Dispatcher 已关：忽略
        }
    }

    void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher 内部线程：防抖计时器必须回 UI 线程操作【技术指引】。
        // 批量操作（复制/删除上百文件）会打出成百上千条事件，若逐条 BeginInvoke 会灌爆
        // Dispatcher 队列；用 pending 标志把同一防抖窗口内的事件合并成一次投递。
        if (Interlocked.Exchange(ref _fsEventPending, 1) == 1) return;
        try
        {
            _debounce.Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _fsEventPending, 0);
                if (_disposed) return;
                _debounce.Stop();
                _debounce.Start();
            });
        }
        catch
        {
            Interlocked.Exchange(ref _fsEventPending, 0); // Dispatcher 已关：复位，不卡死后续事件
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce.Stop();
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // 释放失败不阻断
            }
        }
        _watchers.Clear();
    }
}
