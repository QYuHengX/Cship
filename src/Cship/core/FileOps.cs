using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace Cship.Core;

/// <summary>
/// 文件操作集合（步骤 2 / 00 §6 FileOps）：打开、回收站删除、资源管理器定位、
/// 剪贴板复制、重命名、属性页、固定开始屏幕、桌面新建。
/// 行为铁律（00 §10.1）：删除一律进回收站，绝不永久删除。
/// </summary>
public static class FileOps
{
    public static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>名称合法性（内联重命名/新建用）：非空、不含非法字符、非点号保留名。</summary>
    public static bool IsValidName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name.IndexOfAny(InvalidChars) < 0
           && name.Trim('.') != "";

    public static bool Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"打开失败 {path}：{ex.Message}");
            return false;
        }
    }

    public static bool RecycleDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"回收站删除失败 {path}：{ex.Message}");
            return false;
        }
    }

    public static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn($"定位失败 {path}：{ex.Message}");
        }
    }

    /// <summary>文件路径复制为 Shell FileDropList（须在 STA UI 线程调用）。</summary>
    public static void CopyToClipboard(IEnumerable<string> paths)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        System.Windows.Clipboard.SetFileDropList(list);
    }

    /// <summary>重命名（同目录内）；失败返回 false（调用方提示）。</summary>
    public static bool Rename(string oldPath, string newName)
    {
        try
        {
            string dir = Path.GetDirectoryName(oldPath) ?? "";
            string target = Path.Combine(dir, newName);
            if (File.Exists(target) || Directory.Exists(target))
                return false;
            if (Directory.Exists(oldPath))
                Directory.Move(oldPath, target);
            else
                File.Move(oldPath, target);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"重命名失败 {oldPath} → {newName}：{ex.Message}");
            return false;
        }
    }

    public static void ShowProperties(string path) => SystemBridge.ShellExecuteVerb(path, "properties");

    public static bool PinToStart(string path) => SystemBridge.ShellExecuteVerb(path, "pintohome");

    /// <summary>
    /// 拖文件入收纳板 = 收纳（2026-08-29 用户反馈）：把拖入的文件/文件夹复制到用户桌面
    /// （同名自动 "名称 (2)" 递增），落盘后 watcher 约 0.5s 刷新自然陈列，刷新也不会移除。
    /// 返回成功复制的条数。
    /// </summary>
    public static int CopyIntoDesktop(IEnumerable<string> paths)
    {
        int ok = 0;
        string dir = DesktopScanner.UserDesktopDir;
        foreach (var src in paths)
        {
            try
            {
                if (string.IsNullOrEmpty(src) || !File.Exists(src) && !Directory.Exists(src))
                    continue;
                string name = UniqueName(dir, Path.GetFileNameWithoutExtension(src),
                                         Path.GetExtension(src));
                string dest = Path.Combine(dir, name);
                if (Directory.Exists(src))
                    CopyDirectory(src, dest);
                else
                    File.Copy(src, dest);
                ok++;
                Logger.Info($"拖拽收纳：{src} → {dest}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"拖拽收纳失败 {src}：{ex.Message}");
            }
        }
        return ok;
    }

    /// <summary>递归复制目录（拖拽收纳用；轻量实现，不复制 ACL/属性时间）。</summary>
    static void CopyDirectory(string srcDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(srcDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (var sub in Directory.EnumerateDirectories(srcDir))
            CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
    }

    /// <summary>
    /// 把 path 移入 targetDir（拖到文件夹图标中心圈内松手 → 移入该文件夹，2026-10-05 清单06任务3）。
    /// 同名冲突自动 "(2)" 递增，**绝不覆盖**（数据安全优先，与拖拽收纳 UniqueName 同口径）。
    /// 返回实际落点绝对路径；非法/失败返回 null（缺失、目标非目录、目标即自身、
    /// 目标位于自身之内=自嵌套）。后台线程调用（可能是跨卷复制）。
    /// </summary>
    public static string? MoveInto(string path, string targetDir)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(targetDir)) return null;
            if (!File.Exists(path) && !Directory.Exists(path)) return null;
            if (!Directory.Exists(targetDir)) return null;
            string src = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string dir = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(src, dir, StringComparison.OrdinalIgnoreCase)) return null;      // 自身
            if (dir.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;                                                                  // 自嵌套

            string name = Path.GetFileName(src);
            string dest = Path.Combine(dir, name);
            if (File.Exists(dest) || Directory.Exists(dest)) // 已有同名 → "(2)" 递增，不覆盖
                dest = Path.Combine(dir, UniqueName(dir, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name)));
            if (Directory.Exists(src))
                Directory.Move(src, dest);
            else
                File.Move(src, dest);
            Logger.Info($"移入文件夹：{src} → {dest}");
            return dest;
        }
        catch (Exception ex)
        {
            Logger.Warn($"移入文件夹失败 {path} → {targetDir}：{ex.Message}");
            return null;
        }
    }

    /// <summary>dir 内不冲突的唯一名：base.ext → base (2).ext → base (3).ext …</summary>
    public static string UniqueName(string dir, string baseName, string ext)
    {
        string candidate = baseName + ext;
        int n = 2;
        while (File.Exists(Path.Combine(dir, candidate)) || Directory.Exists(Path.Combine(dir, candidate)))
            candidate = $"{baseName} ({n++}){ext}";
        return candidate;
    }

    /// <summary>桌面新建文件夹，返回完整路径（失败 null）。</summary>
    public static string? CreateFolder(string dir)
    {
        try
        {
            string name = UniqueName(dir, I18n.Tr("new.folder.name"), "");
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(path);
            return path;
        }
        catch (Exception ex)
        {
            Logger.Warn($"新建文件夹失败：{ex.Message}");
            return null;
        }
    }

    public static string? CreateTextFile(string dir)
    {
        try
        {
            string name = UniqueName(dir, I18n.Tr("new.textfile.name"), ".txt");
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, "");
            return path;
        }
        catch (Exception ex)
        {
            Logger.Warn($"新建文本文档失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>创建 .lnk 快捷方式（WScript.Shell 动态 COM，零引用）；返回完整路径（失败 null）。</summary>
    public static string? CreateShortcut(string target)
    {
        object? wsh = null, lnk = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            wsh = Activator.CreateInstance(type)!;
            string baseName = Path.GetFileNameWithoutExtension(target);
            if (string.IsNullOrEmpty(baseName)) baseName = "shortcut";
            string name = UniqueName(DesktopScanner.UserDesktopDir, baseName + " - " + I18n.Tr("new.shortcut.name"), ".lnk");
            string path = Path.Combine(DesktopScanner.UserDesktopDir, name);
            dynamic shell = wsh;
            lnk = shell.CreateShortcut(path);
            dynamic shortcut = lnk;
            shortcut.TargetPath = target;
            shortcut.Save();
            return path;
        }
        catch (Exception ex)
        {
            Logger.Warn($"创建快捷方式失败 {target}：{ex.Message}");
            return null;
        }
        finally
        {
            if (lnk != null) Marshal.ReleaseComObject(lnk);
            if (wsh != null) Marshal.ReleaseComObject(wsh);
        }
    }

    /// <summary>
    /// 文件是否正被占用（指示灯"文件正在使用"检测，2026-10-02）：独占打开失败≈有程序持有句柄。
    /// 必须在后台线程调用（每次尝试一次磁盘 IO）。注意记事本等程序加载后可能不长期持有句柄，
    /// 此类"已打开"场景由调用方的"最近经板打开"记录补充（BoardWindow._recentlyOpened）。
    /// </summary>
    public static bool IsFileInUse(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
        catch { return false; }
    }

    // 进程路径缓存（性能 · 2026-10-05）：Process.MainModule 对其他用户/提权进程必抛异常，
    // 而指示灯轮询是 0.3~2s 的常驻循环——原实现每拍全量重扫 = 每拍上百次首次异常 + 句柄 churn。
    // 改为按 PID 缓存解析结果，只对"新出现的 PID"查 MainModule；TTL 30s 兜底 PID 复用/进程换名。
    // 只增不删的集合在退出 PID 回收时收缩（见下），不会无界增长。
    const long ProcCacheTtlMs = 30_000;
    static readonly object ProcGate = new();
    static readonly Dictionary<int, (string Path, long At)> _procPathCache = new();

    /// <summary>正在运行进程的全路径与文件名集合（指示灯检测用，步骤 3.6）。</summary>
    public static (HashSet<string> FullPaths, HashSet<string> Names) RunningProcesses()
    {
        var full = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processes = Process.GetProcesses();
        long now = Environment.TickCount64;
        lock (ProcGate)
        {
            var live = new HashSet<int>(processes.Length);
            foreach (var p in processes)
            {
                int pid = p.Id;
                live.Add(pid);
                if (_procPathCache.TryGetValue(pid, out var cached) && now - cached.At < ProcCacheTtlMs)
                {
                    AddProc(full, names, cached.Path);
                    p.Dispose();
                    continue;
                }
                string? file = null;
                try
                {
                    file = p.MainModule?.FileName;
                }
                catch
                {
                    // 系统进程 MainModule 多数拒绝访问，跳过
                }
                finally
                {
                    p.Dispose();
                }
                if (string.IsNullOrEmpty(file))
                    _procPathCache.Remove(pid); // 查不到不占位，下一拍重试
                else
                {
                    _procPathCache[pid] = (file, now);
                    AddProc(full, names, file);
                }
            }
            // 已退出的 PID 从缓存移除（PID 复用时不会被旧路径蒙混）
            if (_procPathCache.Count > live.Count)
                foreach (var pid in _procPathCache.Keys.Where(k => !live.Contains(k)).ToList())
                    _procPathCache.Remove(pid);
        }
        return (full, names);
    }

    static void AddProc(HashSet<string> full, HashSet<string> names, string file)
    {
        full.Add(file);
        names.Add(Path.GetFileName(file));
    }
}
