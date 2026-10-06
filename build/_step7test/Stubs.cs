using System;

namespace Cship.Core;

/// <summary>夹具用的 Logger 替身（只打印到控制台，不落盘）。</summary>
internal static class Logger
{
    public static void Info(string msg) => Console.WriteLine("  [INFO] " + msg);
    public static void Warn(string msg) => Console.WriteLine("  [WARN] " + msg);
    public static void Error(string msg, Exception? ex = null) => Console.WriteLine("  [ERROR] " + msg + (ex != null ? " :: " + ex.Message : ""));
}
