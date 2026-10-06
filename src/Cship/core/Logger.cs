using System;
using System.IO;
namespace Cship.Core;

/// <summary>
/// 基础版日志（步骤 3.4）：按天写 logs\cship_yyyyMMdd.log，保留 7 天自动清理。
/// 线程安全；日志自身的任何失败都静默，绝不反过来弄崩程序。
/// </summary>
public static class Logger
{
    static readonly object Gate = new();
    static bool _initialized;

    public static void Init()
    {
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true;
        }
        CleanOldLogs();
        Info("logger init");
    }

    public static void Info(string msg) => Write("INFO", msg, null);
    public static void Warn(string msg) => Write("WARN", msg, null);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", msg, ex);

    /// <summary>单日日志大小上限（07 §6 口径：5MB/天）。超过则把当天主文件轮转为
    /// cship_yyyyMMdd.1.log（覆盖上一份轮转件）后重开主文件，防异常循环把日志写到爆盘。</summary>
    const long MaxBytesPerDay = 5 * 1024 * 1024;

    static void Write(string level, string msg, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                if (!_initialized) return; // Init 之前的消息不落盘（EnsureDirs 已保证目录就绪）
                var now = DateTime.Now;
                var file = Path.Combine(Paths.LogsDir(), $"cship_{now:yyyyMMdd}.log");
                var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
                if (ex != null) line += Environment.NewLine + ex;
                RollIfOversize(file);
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志失败静默
        }
    }

    /// <summary>当天日志超上限则轮转（主文件 → .1.log，旧 .1 覆盖）。按天清理的
    /// cship_*.log 通配会连带清掉轮转件。</summary>
    static void RollIfOversize(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            if (!fi.Exists || fi.Length < MaxBytesPerDay) return;
            string rolled = Path.ChangeExtension(file, null) + ".1.log";
            if (File.Exists(rolled)) File.Delete(rolled);
            File.Move(file, rolled);
        }
        catch
        {
            // 轮转失败（文件被占用等）不阻断日志写入
        }
    }

    static void CleanOldLogs()
    {
        try
        {
            var dir = new DirectoryInfo(Paths.LogsDir());
            if (!dir.Exists) return;
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var f in dir.GetFiles("cship_*.log"))
                if (f.LastWriteTime < cutoff)
                    f.Delete();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("log cleanup failed: " + ex.Message);
        }
    }
}
