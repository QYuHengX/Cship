using System;
using Microsoft.Win32;
using Cship.Core;
using WinForms = System.Windows.Forms;

namespace Step7Test;

internal static class Program
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Cship";

    [STAThread]
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
        switch (mode)
        {
            case "screens": return ScreensProbe();
            case "icons": return Icons();
            case "icontoggle": return IconToggle();
            case "taskbar": return Taskbar();
            case "autostart": return AutostartProbe();
            case "procexit": return ProcessExitProbe();
            default:
                ScreensProbe();
                Icons();
                Taskbar();
                return 0;
        }
    }

    static int ScreensProbe()
    {
        Console.WriteLine("[screens] 真实 SystemBridge.MonitorModel（EDID 通道）");
        foreach (var s in WinForms.Screen.AllScreens)
        {
            string model = SystemBridge.MonitorModel(s.DeviceName);
            Console.WriteLine($"  {s.DeviceName}  {s.Bounds.Width}×{s.Bounds.Height}  primary={s.Primary}  model='{model}'"
                + (model.Length == 0 ? "  (回退基础信息)" : ""));
        }
        return 0;
    }

    static int Icons()
    {
        var v = SystemBridge.QueryDesktopIconsVisible();
        Console.WriteLine($"[icons] QueryDesktopIconsVisible = {(v.HasValue ? (v.Value ? "visible" : "hidden") : "unknown")}");
        return 0;
    }

    static int IconToggle()
    {
        Console.WriteLine($"[icons] before = {SystemBridge.QueryDesktopIconsVisible()}");
        try
        {
            Console.WriteLine($"[icons] hide -> toggled={SystemBridge.SetDesktopIconsVisible(false)}  actual={SystemBridge.QueryDesktopIconsVisible()}");
            System.Threading.Thread.Sleep(300);
            Console.WriteLine($"[icons] show -> toggled={SystemBridge.SetDesktopIconsVisible(true)}  actual={SystemBridge.QueryDesktopIconsVisible()}");
            Console.WriteLine($"[icons] show again -> toggled={SystemBridge.SetDesktopIconsVisible(true)}（幂等，应为 False）");
        }
        finally
        {
            SystemBridge.SetDesktopIconsVisible(true); // 无论如何都保证用户桌面图标可见
        }
        return 0;
    }

    static int Taskbar()
    {
        uint msg = SystemBridge.TaskbarCreatedMessage();
        Console.WriteLine($"[taskbar] TaskbarCreated = 0x{msg:X4}（二次调用同值：0x{SystemBridge.TaskbarCreatedMessage():X4}）");
        return 0;
    }

    /// <summary>
    /// 自启动两种形态：命令行的 exe 部分= Environment.ProcessPath 运行时解析（绝不硬编码），
    /// 因此"开发态=bin 输出 apphost / 打包态=发布 exe"只是同一个表达式在不同位置求值。
    /// 本夹具验证：写入后回读一致 + 命令形态正确 + 关闭幂等；测完**还原用户原值**。
    /// </summary>
    static int AutostartProbe()
    {
        string? original = ReadRunValue();
        Console.WriteLine($"[autostart] 测试前 Run 值 = {original ?? "<无>"}");
        Console.WriteLine($"[autostart] IsEnabled() = {Autostart.IsEnabled()}");
        Console.WriteLine($"[autostart] 本夹具 ProcessPath = {Environment.ProcessPath}");
        try
        {
            bool on = Autostart.SetEnabled(true);
            string? written = ReadRunValue();
            Console.WriteLine($"[autostart] SetEnabled(true) 返回={on}  写入值 = {written}");
            Console.WriteLine($"[autostart] IsEnabled() 回读 = {Autostart.IsEnabled()}");
            Console.WriteLine($"[autostart] 命令形态校验：引号包裹 exe + ' --autostart' = "
                + (written != null && written.StartsWith("\"") && written.EndsWith("\" --autostart")));

            bool off = Autostart.SetEnabled(false);
            Console.WriteLine($"[autostart] SetEnabled(false) 返回={off}  删除后值 = {ReadRunValue() ?? "<无>"}");
            Console.WriteLine($"[autostart] 关闭后 IsEnabled() = {Autostart.IsEnabled()}（应为 False）");
        }
        finally
        {
            RestoreRunValue(original);
            Console.WriteLine($"[autostart] 已还原 Run 值 = {ReadRunValue() ?? "<无>"}");
        }
        return 0;
    }

    /// <summary>ProcessExit 机制验证（07 §6 三条退出路径之一）：注册后 Environment.Exit 应触发。</summary>
    static int ProcessExitProbe()
    {
        bool fired = false;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            fired = true;
            Console.WriteLine("[procexit] AppDomain.ProcessExit 已触发（清理逻辑在此执行）");
        };
        Console.WriteLine("[procexit] 即将 Environment.Exit(0)…");
        Console.Out.Flush();
        Environment.Exit(0);
        Console.WriteLine("[procexit] 不应到达（fired=" + fired + "）");
        return 0;
    }

    static string? ReadRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    static void RestoreRunValue(string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (value == null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else key.SetValue(ValueName, value);
    }
}
