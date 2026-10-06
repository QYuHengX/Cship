using System;
using Microsoft.Win32;
namespace Cship.Core;

/// <summary>
/// 开机自启动（步骤 05 GlobalPage）：写/删 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// 的 "Cship" 键（00 §10.3 唯一允许的注册表写区）。值=运行时解析的可执行入口：
/// 发布态为 Cship.exe 绝对路径；开发态（dotnet run）为构建输出的 apphost 路径，
/// 带 --autostart 参数走静默启动（App.LaunchedWithAutostart）。
/// </summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Cship";

    /// <summary>自启动命令行：exe 绝对路径 + --autostart（开发态为 bin 输出的 apphost，STEP_LOG 记录）。</summary>
    static string Command()
    {
        string exe = Environment.ProcessPath ?? System.IO.Path.Combine(Paths.AppDir(), "Cship.exe");
        return $"\"{exe}\" --autostart";
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取自启动状态失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 写/删自启动键。返回**是否成功**（07 §1：组策略/权限限制下写键可能失败，
    /// 调用方据此回滚开关 UI 状态并气泡告知——不能让开关显示"已开"而注册表里什么都没有）。
    /// </summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null)
            {
                Logger.Warn("写自启动注册表失败：CreateSubKey 返回 null（可能被组策略限制）");
                return false;
            }
            if (enabled)
            {
                key.SetValue(ValueName, Command());
                Logger.Info($"开机自启动开：{Command()}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("开机自启动关：Run 键已删除");
            }
            // 回读校验：注册表写入可能被重定向/静默丢弃（真实状态以回读为准）
            bool actual = IsEnabled();
            if (actual != enabled)
            {
                Logger.Warn($"自启动写入后回读不一致：期望={enabled} 实际={actual}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"写自启动注册表失败：{ex.Message}");
            return false;
        }
    }
}
