using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Cship.Core;

/// <summary>
/// 全局低级键盘钩子：把"每一次按键按下"以 <see cref="KeyChord"/>（主键 + 修饰键）形式交给决策回调，
/// 决策返回 true=吞掉。原先只认硬编码的 Tab / Alt（清单二·任务7），2026-10-05 需求③ 起键位由设置决定，
/// 故钩子层不再判断键位，只在"录入态"与"决策态"之间分流。
/// 安装/卸载与 App 生命周期对齐（委托持引用防 GC）。钩子在 UI 线程安装（App.OnStartup），
/// 回调亦在 UI 线程消息循环内到达，决策回调内可直接读 WPF 状态、用 Dispatcher.BeginInvoke 排队开合动作。
/// ⚠ 本程序按用户明确裁决吞掉 Alt+Tab，系统任务切换器不再触发（STEP_LOG 清单二备注）。
/// </summary>
public sealed class KeyboardHotkey : IDisposable
{
    static Func<KeyChord, bool>? _captureSink;

    /// <summary>快捷键录入模式（设置窗"快捷键"区）：非 null 时**吞掉一切键盘事件**并把组合交给它，
    /// 返回 true=继续录入。必须由钩子层拦截而不能靠 WPF 键盘事件——Tab/Alt+Tab 本就被本钩子吞掉，
    /// 录制时若放行 Alt+Tab 会弹出系统任务切换器。静态是因为全进程只装一个钩子。</summary>
    public static Func<KeyChord, bool>? CaptureSink
    {
        get => _captureSink;
        set
        {
            _captureSink = value;
            _captureDown.Clear();
            _heldMods.Clear();
            if (value != null) SeedHeldMods(); // 进入录入前已按住的修饰键（如按住 Ctrl 再点框）
        }
    }

    // 录入态自持的修饰键记账（2026-10-05 实机抓出）：**被低级钩子吞掉的键不会进系统输入队列，
    // 因而不会更新 async 键态** —— 靠 GetAsyncKeyState 读"按了 Ctrl 再按 K"会得到无修饰的 K
    //（前两版实测分别记成 LeftCtrl / K）。故录入期改由本类自己数：修饰键按下即记账、抬起即销账，
    // 主键按下时按记账结果组组合。
    static readonly HashSet<int> _heldMods = new();

    // 录入期"按下被吞掉"的键：其抬起必须一并吞掉，否则系统会看到"只有抬起没有按下"的半对事件。
    // 反向也成立：录入开始前就已按住的键（按下没被吞）抬起必须放行，否则系统会以为该键一直按着。
    static readonly HashSet<int> _captureDown = new();

    IntPtr _hook;
    SystemBridge.LowLevelKeyboardProc? _proc; // 持引用防 GC 回收钩子
    readonly Func<KeyChord, bool> _onKey;

    /// <param name="onKey">按键按下（KEYDOWN / SYSKEYDOWN）时的同步决策回调：返回 true=吞掉。
    /// UI 动作请在其内 Dispatcher.BeginInvoke 排队，勿在钩子回调里做耗时操作（会拖慢全系统键盘）。</param>
    public KeyboardHotkey(Func<KeyChord, bool> onKey)
    {
        _onKey = onKey;
        _proc = HookProc;
        _hook = SystemBridge.SetWindowsHookEx(SystemBridge.WH_KEYBOARD_LL, _proc,
            SystemBridge.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Logger.Warn("全局键盘钩子安装失败（快捷键功能降级为不可用，不影响其它行为）");
    }

    IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                int msg = (int)wParam;
                bool down = msg == SystemBridge.WM_KEYDOWN || msg == SystemBridge.WM_SYSKEYDOWN;
                bool up = msg == SystemBridge.WM_KEYUP || msg == SystemBridge.WM_SYSKEYUP;
                if (down || up)
                {
                    var info = Marshal.PtrToStructure<SystemBridge.KBDLLHOOKSTRUCT>(lParam);
                    int vk = (int)info.vkCode;

                    var sink = _captureSink;
                    if (sink != null)
                    {
                        // 录入态：按下/抬起全吞（成对，系统不会看到半对事件）
                        if (down)
                        {
                            _captureDown.Add(vk);
                            int mod = NormalizeModifier(vk);
                            if (mod != 0) _heldMods.Add(mod);
                            sink(ChordFromHeld(vk));
                        }
                        else if (_captureDown.Remove(vk))
                        {
                            int mod = NormalizeModifier(vk);
                            if (mod != 0) _heldMods.Remove(mod);
                        }
                        else
                        {
                            return SystemBridge.CallNextHookEx(_hook, nCode, wParam, lParam); // 录入前就按住的键：抬起照常放行
                        }
                        return (IntPtr)1;
                    }

                    if (down && _onKey(KeyChord.FromVk(vk)))
                        return (IntPtr)1; // 吞掉
                }
            }
            catch (Exception ex)
            {
                // 钩子回调内绝不能抛：万一决策/录入回调出错，本次按键按"放行"处理
                Logger.Warn($"全局键盘钩子回调异常（本次按键放行）：{ex.Message}");
            }
        }
        return SystemBridge.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>录入期按记账结果组组合（修饰键按下时间点，vk 即修饰键自身 → 只带修饰键的组合）。</summary>
    static KeyChord ChordFromHeld(int vk)
        => new(vk, _heldMods.Contains(0x11), _heldMods.Contains(0x12), _heldMods.Contains(0x10),
            _heldMods.Contains(0x5B) || _heldMods.Contains(0x5C));

    /// <summary>修饰键 vk → 通用码（低级钩子给左右分体码：VK_LCONTROL=0xA2 等）；非修饰键返回 0。</summary>
    static int NormalizeModifier(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => 0x10, // Shift
        0x11 or 0xA2 or 0xA3 => 0x11, // Ctrl
        0x12 or 0xA4 or 0xA5 => 0x12, // Alt
        0x5B or 0x5C => vk,           // Win（左右各算一个）
        _ => 0,
    };

    static void SeedHeldMods()
    {
        foreach (int mod in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((SystemBridge.GetAsyncKeyState(mod) & 0x8000) != 0)
                _heldMods.Add(mod);
    }

    public void Dispose()
    {
        CaptureSink = null;
        if (_hook == IntPtr.Zero) return;
        SystemBridge.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }
}
