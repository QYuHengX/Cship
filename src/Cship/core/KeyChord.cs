using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace Cship.Core;

/// <summary>
/// 键盘组合（主键 + 修饰键），用于两个全局快捷键（开合收纳板 / 强制开合收纳板）的可配置化
/// （2026-10-05 需求③）。存储串形如 <c>Tab</c> / <c>Alt+Tab</c> / <c>Ctrl+Shift+K</c>，
/// 修饰键固定按 Ctrl+Alt+Shift+Win 排序，保证同义组合只有一种字面量（便于相等比较与去重）。
/// </summary>
public readonly record struct KeyChord(int Vk, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;
    const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3,
        VkLMenu = 0xA4, VkRMenu = 0xA5;
    const int VkEscape = 0x1B;

    /// <summary>默认：普通开合 = Tab。</summary>
    public static readonly KeyChord DefaultToggle = new(0x09, false, false, false, false);

    /// <summary>默认：强制开合 = Alt+Tab（与清单二·任务7 原硬编码一致）。</summary>
    public static readonly KeyChord DefaultForce = new(0x09, false, true, false, false);

    /// <summary>按下的只是修饰键本身（不是可用主键，录入时继续等待）。
    /// ⚠ 低级钩子的 vkCode 给的是**左右分体码**（VK_LCONTROL=0xA2 等）而不是通用码 VK_CONTROL
    /// （2026-10-05 实机抓出：只判 0x10/0x11/0x12 会把"先按 Ctrl"记成 LeftCtrl），故左右各码一并算</summary>
    public bool IsModifierOnly => Vk is VkShift or VkControl or VkMenu or VkLWin or VkRWin
        or VkLShift or VkRShift or VkLControl or VkRControl or VkLMenu or VkRMenu;

    /// <summary>Esc（录入态的取消键）。</summary>
    public bool IsEscape => Vk == VkEscape;

    /// <summary>以 vk 为主键、按当前**物理**按键状态补修饰键。修饰键走 GetAsyncKeyState 而非
    /// 钩子消息：低级钩子吞掉按键不影响该读数，所以录制态照样能取到 Alt/Ctrl 的真实按下态。</summary>
    public static KeyChord FromVk(int vk)
        => new(vk, Down(VkControl), Down(VkMenu), Down(VkShift), Down(VkLWin) || Down(VkRWin));

    static bool Down(int vk) => (SystemBridge.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>规范化存储串。</summary>
    public string ToStorage()
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Alt) sb.Append("Alt+");
        if (Shift) sb.Append("Shift+");
        if (Win) sb.Append("Win+");
        sb.Append(KeyName(Vk));
        return sb.ToString();
    }

    /// <summary>界面展示串（" + " 分隔，和存储串同构）。</summary>
    public string ToDisplay() => ToStorage().Replace("+", " + ");

    /// <summary>解析设置值；非法（空串/只有修饰键/两个主键/未知键名）回退 fallback。</summary>
    public static KeyChord ParseOr(string? text, KeyChord fallback)
        => TryParse(text, out var chord) ? chord : fallback;

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, alt = false, shift = false, win = false;
        int? vk = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
                case "win" or "windows": win = true; continue;
            }
            if (vk != null) return false;               // 出现第二个主键：非法
            if (!TryKeyName(part, out int v)) return false;
            vk = v;
        }
        if (vk == null) return false;                   // 只有修饰键（或空）：非法
        var parsed = new KeyChord(vk.Value, ctrl, alt, shift, win);
        if (parsed.IsModifierOnly) return false;
        chord = parsed;
        return true;
    }

    /// <summary>常用键的友好名（其余走 <see cref="Key"/> 枚举名）。</summary>
    static readonly Dictionary<int, string> Friendly = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x14] = "CapsLock",
        [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown",
        [0x23] = "End", [0x24] = "Home", [0x25] = "Left", [0x26] = "Up", [0x27] = "Right",
        [0x28] = "Down", [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
        [0x5B] = "LWin", [0x5C] = "RWin",
        [0xBA] = "Semicolon", [0xBB] = "Plus", [0xBC] = "Comma", [0xBD] = "Minus",
        [0xBE] = "Period", [0xBF] = "Slash", [0xC0] = "Tilde",
        [0xDB] = "BracketLeft", [0xDC] = "Backslash", [0xDD] = "BracketRight", [0xDE] = "Quote",
    };

    /// <summary>vk → 键名（虚拟键表查不到时退 <see cref="KeyInterop"/> 枚举名，再退 VK 十六进制）。</summary>
    public static string KeyName(int vk)
    {
        if (Friendly.TryGetValue(vk, out string? name)) return name;
        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key == Key.None ? $"VK{vk:X2}" : key.ToString();
    }

    static bool TryKeyName(string name, out int vk)
    {
        foreach (var kv in Friendly)
            if (string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase)) { vk = kv.Key; return true; }
        // 纯数字串会命中 Enum.TryParse 的数值分支（"5" → (Key)5），不是键名，拒绝
        if (name.All(char.IsDigit)) { vk = 0; return false; }
        if (Enum.TryParse(name, ignoreCase: true, out Key key) && key != Key.None)
        {
            int v = KeyInterop.VirtualKeyFromKey(key);
            if (v != 0) { vk = v; return true; }
        }
        vk = 0;
        return false;
    }
}
