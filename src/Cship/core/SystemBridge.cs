using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
namespace Cship.Core;

/// <summary>
/// 全部 P/Invoke 的唯一归宿（00 §6）。当前：托盘图标句柄销毁；01b 补丁新增悬浮窗
/// low 档置底所需的 WINDOWPOS 结构与常量、medium 档全屏前台检测占位；
/// DWM 材质、真全屏检测等系统调用随后续步骤集中迁入这里。
/// </summary>
internal static class SystemBridge
{
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // ---- 01b：悬浮窗 low 档拦截 WM_WINDOWPOSCHANGING 强制 HWND_BOTTOM 所需 ----

    internal const int WM_WINDOWPOSCHANGING = 0x0046;
    internal const uint SWP_NOZORDER = 0x0004;
    internal static readonly IntPtr HWND_BOTTOM = (IntPtr)1;

    // 设置窗压到收纳板正上方（2026-10-02）：不激活、不动位置尺寸，仅改 z 序
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    internal const uint GW_HWNDPREV = 0x0003; // z 序中"紧邻其上方"的窗口
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOPMOST = 0x0008;
    // 置顶带开关（SetWindowPos 的 hWndInsertAfter 特殊值）
    internal static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = (IntPtr)(-2);

    /// <summary>把窗口置顶/取消置顶（不动位置尺寸、不抢焦点）。用于"菜单优先级跟随所属窗口"
    /// （清单05任务8：Popup 是独立顶层 HWND，WPF 只在创建时落定一次，需按宿主窗口实时同步）。</summary>
    internal static void SetTopmost(IntPtr hwnd, bool topmost)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    /// <summary>窗口是否带置顶样式（WS_EX_TOPMOST）。</summary>
    internal static bool IsTopmost(IntPtr hwnd) => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    /// <summary>把 hwnd 插到 insertAfter 句柄的**正下方**（z 序紧邻其后；不抢焦点）。
    /// 注意 Win32 语义：SetWindowPos 的 hWndInsertAfter 是"排在目标窗口之上"的那个窗口
    /// （实测：SetWindowPos(A, B, ...) 后 A 在 B 之下，2026-10-04 错误修复⑧的根因）。</summary>
    internal static void SetWindowPosAfter(IntPtr hwnd, IntPtr insertAfter)
        => SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>把 hwnd 放到 belowHwnd 的正上方（同一 z 序带内；不抢焦点、不动位置尺寸）。
    /// 做法=取"当前位于 belowHwnd 上方"的那个窗口（无则 HWND_TOP）作为 hWndInsertAfter。
    /// 若上方紧邻是置顶窗口，直接插其之下会把非置顶的设置窗也带进置顶带（会压住别的程序窗口）——
    /// 此时退回 HWND_TOP：仍在收纳板之上，且不越级到置顶带（错误修复⑧的"不在其他窗口之上"）。</summary>
    internal static void SetWindowPosAbove(IntPtr hwnd, IntPtr belowHwnd)
    {
        var above = GetWindow(belowHwnd, GW_HWNDPREV);
        if (above != IntPtr.Zero && IsTopmost(above)) above = IntPtr.Zero;
        SetWindowPos(hwnd, above, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>hwnd 当前是否在 other 之上（沿 z 序自 other 向上走）。</summary>
    internal static bool IsWindowAbove(IntPtr hwnd, IntPtr other)
    {
        for (var h = GetWindow(other, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
            if (h == hwnd) return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    /// <summary>
    /// 前台窗口是否为所选屏真全屏（2026-10-03 实装，Tab/Tab+Alt 决策用）：
    /// GetForegroundWindow → GetWindowRect **完整覆盖该屏边界**（最大化窗口只盖工作区不算），
    /// 排除自身进程、桌面壳（Progman/WorkerW）与 DWM cloaked 窗口（UWP 壳宿主等）。
    /// </summary>
    internal static bool IsForegroundFullScreen(WinForms.Screen screen)
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || screen == null) return false;
            if (GetWindowThreadProcessId(fg, out uint pid) != 0 && pid == (uint)Environment.ProcessId)
                return false; // 自己的窗口（收纳板/设置窗置顶时）不算全屏遮挡
            if (!GetWindowRect(fg, out var r)) return false;
            var b = screen.Bounds;
            if (r.Left > b.Left || r.Top > b.Top || r.Right < b.Right || r.Bottom < b.Bottom)
                return false; // 未完整盖满该屏（最大化窗口 rect=工作区，到不了这里）
            var sb = new System.Text.StringBuilder(64);
            if (GetClassName(fg, sb, 64) > 0)
            {
                string cls = sb.ToString();
                if (cls is "Progman" or "WorkerW") return false; // 桌面壳
            }
            if (DwmGetWindowAttribute(fg, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false; // DWM 遮蔽窗口（UWP 壳宿主等）不可见
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal const int DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>把窗口置前（托盘弹出菜单需要拿到键盘焦点才能接住 Esc；标准托盘菜单做法）。</summary>
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();

    /// <summary>某物理坐标处窗口所在屏的 DPI 缩放（托盘菜单绝对定位把物理像素折 DIP 用）。
    /// 逐屏 DPI 下不同显示器缩放不同，必须按点位查而不是按主屏。</summary>
    internal static double ScaleAt(System.Windows.Point physical)
    {
        try
        {
            var hwnd = WindowFromPoint(new POINT { X = (int)physical.X, Y = (int)physical.Y });
            uint dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : GetDpiForSystem();
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    // ---- 材质：系统合成器模糊（真·图层堆叠，2026-10-06 口径）----

    /// <summary>WCA_ACCENT_POLICY 的载体（user32 未文档化导出，但自 Vista 起长期稳定存在）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    /// <summary>强调策略：<see cref="AccentState"/> 决定窗口背后由 DWM 画什么。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor; // ABGR（0xAABBGGRR）
        public int AnimationId;
    }

    internal const int WCA_ACCENT_POLICY = 19;
    internal const int ACCENT_DISABLED = 0;
    /// <summary>DWM 在窗口背后做高斯模糊（Win10 1607+；实测对本项目分层窗有效）。</summary>
    internal const int ACCENT_ENABLE_BLURBEHIND = 3;

    /// <summary>
    /// 设置窗口的强调策略（材质引擎调用）。**未文档化 API**：返回 0 表示调用失败
    /// （老系统 / 未来移除），调用方据此回退到"纯静态材质层"，不黑底。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    // ---- 02：桌面图标提取（IShellItemImageFactory，资源管理器同款通道）----

    /// <summary>
    /// 【补充决策】放弃 SHGetImageList/ImageList 通道：本机实测 SHGetImageList 恒返回
    /// E_NOINTERFACE（STA/MTA、有无 comctl32 v6 manifest 均复现，系统层不可用）。
    /// 改用 SHCreateItemFromParsingName + IShellItemImageFactory.GetImage（256px，ICONONLY），
    /// 天然支持 exe/.lnk（shell 解析目标图标）/文件夹，无需图像列表。
    /// </summary>
    static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    const uint SIIGBF_BIGGERSIZEOK = 0x1;
    const uint SIIGBF_ICONONLY = 0x4;
    const uint COINIT_APARTMENTTHREADED = 0x2;
    const uint COINIT_DISABLE_OLE1DDE = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc,
        ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll")]
    internal static extern int GetObject(IntPtr hObject, int cBytes, ref BITMAP bitmap);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines,
        byte[] bits, ref BITMAPINFOHEADER bi, uint usage);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(IntPtr hwnd, ref POINT lpPoint);

    // ---- OLE 拖放诊断（2026-08-30 批次十一：定位"板内反复 DragLeave"时 OLE 眼中的目标窗口）----

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

    // ---- 低级键盘钩子（清单二·任务7：Tab 开合收纳板 / Alt+Tab 强制开合）----

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;   // 快捷键录入态配平用（2026-10-05）
    internal const int WM_SYSKEYDOWN = 0x0104; // Alt 按住时的 keydown（Alt+Tab 走这个）
    internal const int WM_SYSKEYUP = 0x0105;   // Alt 按住时的 keyup（2026-10-05）
    internal const uint VK_TAB = 0x09;
    internal const uint VK_MENU = 0x12; // Alt

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    /// <summary>键位即时状态：高位 0x8000=按下（Alt 是否按住用）。</summary>
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    // ---- 低级鼠标钩子（收纳板"中键点板外收起"，2026-08-29 批次八）----

    internal const int WH_MOUSE_LL = 14;
    internal const int WM_MBUTTONDOWN = 0x0207;

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>
    /// 提取文件/文件夹的真实大图标并编码为 PNG 字节（步骤 2.1）。
    /// 通道（2026-08-29 修复 Steam .url 等快捷方式空白图标）：
    /// ① .url：解析 [InternetShortcut] 的 IconFile/IconIndex → SHDefExtractIcon（256px 直取）；
    /// ② 工厂通道（IShellItemImageFactory，资源管理器同款）；若结果与"通用空白页图标"一致
    ///    （实测本机 .url/.txt 经工厂通道会拿到通用图标且状态码成功，误当真图标缓存）则视为失败；
    /// ③ SHGetFileInfo（走 shell 图标处理器，返回 32px）兜底；④ 工厂结果原样兜底。
    /// 返回 null 表示失败（调用方按空图标处理）。
    /// </summary>
    internal static byte[]? ExtractIconPng(string path, bool isDirectory)
    {
        int coInit = 1; // S_OK=0 / S_FALSE=1：仅成功初始化才配对 CoUninitialize
        try
        {
            coInit = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);

            if (!isDirectory && string.Equals(Path.GetExtension(path), ".url", StringComparison.OrdinalIgnoreCase))
            {
                var url = TryUrlIconFile(path);
                if (url != null) return url;
            }

            var fact = TryFactory(path);
            if (fact != null)
            {
                if (LooksLikeGenericIcon(fact.Value))
                {
                    Logger.Warn($"工厂通道返回通用空白图标，转回退通道 {path}");
                }
                else
                {
                    return EncodeIconPng(fact.Value);
                }
            }

            var shell = TryShellFileInfoIcon(path);
            if (shell != null) return shell;

            if (fact != null) return EncodeIconPng(fact.Value); // 哪怕疑似通用也强过空图标
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"图标提取失败 {path}：{ex.Message}");
            return null;
        }
        finally
        {
            if (coInit == 0 || coInit == 1) CoUninitialize();
        }
    }

    internal readonly record struct RawIcon(int W, int H, byte[] Pixels);

    /// <summary>工厂通道：SHCreateItemFromParsingName + IShellItemImageFactory.GetImage（256px，ICONONLY）。</summary>
    static RawIcon? TryFactory(string path)
    {
        var iid = IID_IShellItemImageFactory;
        int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        if (hr != 0 || factory == null)
        {
            Logger.Warn($"图标提取失败（SHCreateItem）hr=0x{hr:X8} {path}");
            return null;
        }
        try
        {
            hr = factory.GetImage(new NativeSize { Width = 256, Height = 256 },
                SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY, out IntPtr hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                Logger.Warn($"图标提取失败（GetImage）hr=0x{hr:X8} {path}");
                return null;
            }
            try
            {
                return HBitmapToRaw(hBitmap);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        finally
        {
            // RCW 显式释放（内存优化 · 2026-10-05）：首次扫描几百个条目，若全等 GC 终结，
            // 底层 COM 引用与包装对象会积压到一次 GC 才释放。
            try { Marshal.ReleaseComObject(factory); } catch { /* 已分离的 RCW：忽略 */ }
        }
    }

    /// <summary>
    /// .url 通道：解析 [InternetShortcut] IconFile/IconIndex → SHDefExtractIcon（256px）。
    /// Steam 等创建的 .url 均带本地 IconFile（.ico/.exe）；文件缺失/远端 URL 时返回 null 走后续通道。
    /// </summary>
    static byte[]? TryUrlIconFile(string urlPath)
    {
        try
        {
            string? iconFile = null;
            int index = 0;
            foreach (var line in File.ReadLines(urlPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                if (string.Equals(key, "IconFile", StringComparison.OrdinalIgnoreCase))
                    iconFile = val;
                else if (string.Equals(key, "IconIndex", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(val, out var idx))
                    index = idx;
            }
            if (string.IsNullOrEmpty(iconFile) || !File.Exists(iconFile))
                return null;
            int hr = SHDefExtractIcon(iconFile, index, 0, out IntPtr hLarge, out IntPtr hSmall, 256);
            if (hSmall != IntPtr.Zero) DestroyIcon(hSmall); // 防御：个别 shell 扩展无视 nIconSize 也回小图标
            if (hr != 0 || hLarge == IntPtr.Zero)
            {
                if (hLarge != IntPtr.Zero) DestroyIcon(hLarge);
                return null;
            }
            try
            {
                var raw = HIconToRaw(hLarge);
                return raw == null ? null : EncodeIconPng(raw.Value);
            }
            finally
            {
                DestroyIcon(hLarge);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($".url IconFile 提取失败 {urlPath}：{ex.Message}");
            return null;
        }
    }

    /// <summary>SHGetFileInfo 兜底通道：走 shell 图标处理器（能正确解析 .url/.txt 等
    /// 按文件类型的关联图标），返回 32px HICON。</summary>
    static byte[]? TryShellFileInfoIcon(string path)
    {
        try
        {
            var info = new SHFILEINFOW();
            IntPtr ok = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(),
                SHGFI_ICON | SHGFI_LARGEICON);
            if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;
            try
            {
                var raw = HIconToRaw(info.hIcon);
                return raw == null ? null : EncodeIconPng(raw.Value);
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"SHGetFileInfo 图标兜底失败 {path}：{ex.Message}");
            return null;
        }
    }

    // ---- 通用空白图标识别（2026-08-29）----

    const uint SHGFI_ICON = 0x000000100;
    const uint SHGFI_LARGEICON = 0x000000000;
    const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHDefExtractIcon(string iconFile, int iconIndex, uint uFlags,
        out IntPtr hIconLarge, out IntPtr hIconSmall, uint nIconSize);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ICONINFO
    {
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetIconInfo(IntPtr hIcon, ref ICONINFO info);

    static RawIcon? _genericRef; // 32×32 通用文档图标基准签名（惰性构建）
    static bool _genericRefProbed; // 失败也记一次，避免每个图标都重跑一遍 SHGetFileInfo（性能）

    /// <summary>用"不存在的假扩展名"经 SHGFI_USEFILEATTRIBUTES 取系统默认文档图标作为基准。</summary>
    static RawIcon? GenericReference()
    {
        if (_genericRefProbed) return _genericRef;
        _genericRefProbed = true;
        var info = new SHFILEINFOW();
        IntPtr ok = SHGetFileInfo("__cship__.__generic__", FILE_ATTRIBUTE_NORMAL, ref info,
            (uint)Marshal.SizeOf<SHFILEINFOW>(), SHGFI_USEFILEATTRIBUTES | SHGFI_ICON | SHGFI_LARGEICON);
        if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            _genericRef = HIconToRaw(info.hIcon);
            return _genericRef;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>工厂结果降采样到 32×32 后与通用文档图标基准逐像素比对（平均差 &lt; 3 视为同图）。</summary>
    static bool LooksLikeGenericIcon(RawIcon raw)
    {
        var reference = GenericReference();
        if (reference == null || raw.W <= 0 || raw.H <= 0) return false;
        const int n = 32;
        long diff = 0;
        for (int y = 0; y < n; y++)
        {
            int sy = y * raw.H / n;
            for (int x = 0; x < n; x++)
            {
                int sx = x * raw.W / n;
                int a = (sy * raw.W + sx) * 4;
                int b = (y * n + x) * 4;
                diff += Math.Abs(raw.Pixels[a] - reference.Value.Pixels[b])
                        + Math.Abs(raw.Pixels[a + 1] - reference.Value.Pixels[b + 1])
                        + Math.Abs(raw.Pixels[a + 2] - reference.Value.Pixels[b + 2])
                        + Math.Abs(raw.Pixels[a + 3] - reference.Value.Pixels[b + 3]);
            }
        }
        return diff / (double)(n * n * 4) < 3.0;
    }

    // ---- 位图/图标 → 原始像素与 PNG 编码 ----

    /// <summary>HBITMAP（32bpp BGRA 预乘）→ RawIcon。</summary>
    static RawIcon? HBitmapToRaw(IntPtr hBitmap)
    {
        var bmp = new BITMAP();
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bmp) == 0)
            return null;
        int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
        if (w <= 0 || h <= 0 || w > 2048 || h > 2048)
            return null;
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            var bi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            var pixels = new byte[w * h * 4];
            if (GetDIBits(hdc, hBitmap, 0, (uint)h, pixels, ref bi, 0) == 0)
                return null;
            return new RawIcon(w, h, pixels);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>HICON → RawIcon（经 ICONINFO.hbmColor 走 GetDIBits；无彩色位图返回 null）。</summary>
    static RawIcon? HIconToRaw(IntPtr hIcon)
    {
        var info = new ICONINFO();
        if (!GetIconInfo(hIcon, ref info))
            return null;
        try
        {
            if (info.hbmColor == IntPtr.Zero)
                return null; // 单色图标
            var raw = HBitmapToRaw(info.hbmColor);
            return raw;
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
    }

    /// <summary>RawIcon → PNG 字节：alpha 包围盒裁边 + 四周 8% 边距（所有图标观感归一化）。
    /// 2026-08-28：修"图标过小/顶格溢出"观感不一。
    /// 2026-10-01：追加"纯色底小内容二次裁切"——不透明满铺底色块内居中一枚极小内容
    /// （图吧工具箱/微星小飞机类）按内容差色重裁，把内容放大到与其他图标一致的观感。</summary>
    static byte[]? EncodeIconPng(RawIcon raw)
    {
        int w = raw.W, h = raw.H;
        var pixels = raw.Pixels;

        // alpha 包围盒（预乘 alpha 下 A=0 处 RGB 亦为 0）
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                if (pixels[row + x * 4 + 3] == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0)
            return null; // 全透明

        // 纯色底小内容二次裁切（命中则替换裁切区）
        var content = UniformBgContentCrop(pixels, w, h, minX, minY, maxX, maxY);
        if (content is { } c)
        {
            minX = c.X0; minY = c.Y0; maxX = c.X1; maxY = c.Y1;
        }

        // 四周补 8% 边距（以内容长边为基准），钳在源画布内
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        int pad = Math.Max(2, (int)Math.Round(Math.Max(cw, ch) * 0.08));
        int cx = Math.Max(0, minX - pad);
        int cy = Math.Max(0, minY - pad);
        int cx2 = Math.Min(w, maxX + 1 + pad);
        int cy2 = Math.Min(h, maxY + 1 + pad);
        int nw = cx2 - cx, nh = cy2 - cy;

        var cropped = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            Buffer.BlockCopy(pixels, (cy + y) * w * 4 + cx * 4, cropped, y * nw * 4, nw * 4);

        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            nw, nh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, cropped, nw * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    readonly record struct ContentRect(int X0, int Y0, int X1, int Y1);

    /// <summary>
    /// 纯色底小内容裁切（2026-10-01）：alpha 满铺（≥94% 双向）且边缘色均匀（视为底色块）时，
    /// 找与底色差异显著的内容包围盒；内容线性尺寸 ≤ 满铺区的 72% 才裁——只治"大底小内容"，
    /// 正常满铺图标（logo 占满底的 tile）不受影响。底色估计优先不透明边缘（A≥200，预乘≈直值），
    /// 不足时接受**半透明均匀底**（A≥10，未预乘比较 + alpha 一致性）。
    /// 两遍尝试：第一遍取最外圈底色；若"内容"几乎占满（>72%，常见=黑/白半透明包边被误当底），
    /// 第二遍改取**内圈**底色（避开包边）再裁——治 Wett 工具箱等"包边+浅底+极小内容"的图标。
    /// </summary>
    static ContentRect? UniformBgContentCrop(byte[] px, int w, int h, int minX, int minY, int maxX, int maxY)
    {
        int bw = maxX - minX + 1, bh = maxY - minY + 1;
        if (bw < w * 94 / 100 || bh < h * 94 / 100) return null; // 非满铺底不管

        int inset = Math.Max(2, Math.Min(bw, bh) / 32);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int off = attempt == 0 ? 0 : inset;
            int rx0 = minX + off, ry0 = minY + off, rx1 = maxX - off, ry1 = maxY - off;
            if (rx1 - rx0 < 8 || ry1 - ry0 < 8) break;

            // 底色估计：不透明边缘优先，退而接受半透明均匀底
            bool translucent = false;
            int br = 0, bg = 0, bb = 0, bgA = 255;
            bool haveBg = EstimateEdgeBg(px, w, rx0, ry0, rx1, ry1, minAlpha: 200, translucent: false,
                ref br, ref bg, ref bb, ref bgA);
            if (!haveBg)
            {
                translucent = EstimateEdgeBg(px, w, rx0, ry0, rx1, ry1, minAlpha: 10, translucent: true,
                    ref br, ref bg, ref bb, ref bgA);
                haveBg = translucent;
            }
            if (!haveBg)
                continue; // 本圈取不到底 → 下一圈

            // 内容包围盒：与底色显著差色（未预乘任一通道差 >48）或 alpha 显著异底（|ΔA|>64）的实色像素
            int cx0 = w, cy0 = h, cx1 = -1, cy1 = -1;
            for (int y = ry0; y <= ry1; y++)
            {
                int row = y * w * 4;
                for (int x = rx0; x <= rx1; x++)
                {
                    int i = row + x * 4;
                    int a = px[i + 3];
                    if (a < 64) continue;
                    // 半透明底：同底色同 alpha 的像素不算内容；不透明底：实色同底色不算内容，
                    // 抗锯齿半像素（a<200）围绕内容一并计入边界
                    bool sameAsBg = DiffFromBg(px, i, br, bg, bb) <= 48 &&
                                    (translucent ? Math.Abs(a - bgA) <= 64 : a >= 200);
                    if (sameAsBg) continue;
                    if (x < cx0) cx0 = x;
                    if (x > cx1) cx1 = x;
                    if (y < cy0) cy0 = y;
                    if (y > cy1) cy1 = y;
                }
            }
            if (cx1 < 0) continue;
            int kw = cx1 - cx0 + 1, kh = cy1 - cy0 + 1;
            if (kw < 3 || kh < 3) continue;
            if (kw > bw * 72 / 100 || kh > bh * 72 / 100) continue; // 内容不算小 → 正常图标，不裁（或换内圈再试）
            return new ContentRect(cx0, cy0, cx1, cy1);
        }

        // 第三遍（镂空边框类）：内圈近乎全透（≥95% 像素 A<10）说明 alpha 满铺来自一圈细边框，
        // 真正的内容=不透明主体（A≥64）——裁到只剩主体，边框随裁切丢弃（遮罩会补回瓦片观感）。
        {
            int rx0 = minX + inset, ry0 = minY + inset, rx1 = maxX - inset, ry1 = maxY - inset;
            if (rx1 - rx0 >= 8 && ry1 - ry0 >= 8)
            {
                int solid = 0, total = 0;
                for (int x = rx0; x <= rx1; x++)
                    for (int k = 0; k < 2; k++)
                    {
                        total++;
                        if (px[((k == 0 ? ry0 : ry1) * w + x) * 4 + 3] >= 10) solid++;
                    }
                for (int y = ry0; y <= ry1; y++)
                    for (int k = 0; k < 2; k++)
                    {
                        total++;
                        if (px[(y * w + (k == 0 ? rx0 : rx1)) * 4 + 3] >= 10) solid++;
                    }
                if (total > 0 && solid * 20 <= total)
                {
                    // 内容扫描再内缩 1/16：镂空边框常贴一条不透明细线（A≥64 铺满外沿），
                    // 不排除会撑满包围盒；真内容（居中小 logo）离边远，内缩无损
                    int m = Math.Max(inset, Math.Min(bw, bh) / 16);
                    int sx0 = minX + m, sy0 = minY + m, sx1 = maxX - m, sy1 = maxY - m;
                    if (sx1 - sx0 >= 8 && sy1 - sy0 >= 8)
                    {
                        int cx0 = w, cy0 = h, cx1 = -1, cy1 = -1;
                        for (int y = sy0; y <= sy1; y++)
                        {
                            int row = y * w * 4;
                            for (int x = sx0; x <= sx1; x++)
                            {
                                if (px[row + x * 4 + 3] < 80) continue;
                                if (x < cx0) cx0 = x;
                                if (x > cx1) cx1 = x;
                                if (y < cy0) cy0 = y;
                                if (y > cy1) cy1 = y;
                            }
                        }
                        if (cx1 >= 0)
                        {
                            int kw = cx1 - cx0 + 1, kh = cy1 - cy0 + 1;
                            if (kw >= 3 && kh >= 3 && kw <= bw * 72 / 100 && kh <= bh * 72 / 100)
                                return new ContentRect(cx0, cy0, cx1, cy1);
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>像素（预乘）与底色的未预乘最大通道差。</summary>
    static int DiffFromBg(byte[] px, int i, int br, int bg, int bb)
    {
        int a = px[i + 3];
        if (a <= 0) return 0;
        int r = px[i + 2] * 255 / a, g = px[i + 1] * 255 / a, b = px[i] * 255 / a;
        return Math.Max(Math.Abs(r - br), Math.Max(Math.Abs(g - bg), Math.Abs(b - bb)));
    }

    /// <summary>沿满铺区边缘估计均匀底色。opaque 阈值 A≥minAlpha；translucent=true 时按
    /// 未预乘色比较 + alpha 一致性（±24），bgA 返回底 alpha（不透明路径恒 255）。</summary>
    static bool EstimateEdgeBg(byte[] px, int w, int minX, int minY, int maxX, int maxY,
        int minAlpha, bool translucent, ref int br, ref int bg, ref int bb, ref int bgA)
    {
        long r = 0, g = 0, b = 0;
        long a = 0;
        int n = 0;
        for (int x = minX; x <= maxX; x++)
            for (int k = 0; k < 2; k++)
            {
                int i = ((k == 0 ? minY : maxY) * w + x) * 4;
                if (px[i + 3] < minAlpha) continue;
                int av = px[i + 3];
                r += (long)px[i + 2] * 255 / av; g += (long)px[i + 1] * 255 / av; b += (long)px[i] * 255 / av;
                a += av;
                n++;
            }
        for (int y = minY; y <= maxY; y++)
            for (int k = 0; k < 2; k++)
            {
                int i = (y * w + (k == 0 ? minX : maxX)) * 4;
                if (px[i + 3] < minAlpha) continue;
                int av = px[i + 3];
                r += (long)px[i + 2] * 255 / av; g += (long)px[i + 1] * 255 / av; b += (long)px[i] * 255 / av;
                a += av;
                n++;
            }
        int need = (maxX - minX + 1) + (maxY - minY + 1); // 边缘周长的一半以上有实色像素
        if (n < need) return false;
        br = (int)(r / n); bg = (int)(g / n); bb = (int)(b / n);
        int meanA = (int)(a / n);
        if (!translucent)
        {
            bgA = 255;
            meanA = 255;
        }
        else
        {
            bgA = meanA;
        }

        // 均匀性复查：色差与 alpha 偏差超限的边缘像素占比 >2% → 渐变/纹理底，放弃
        int deviant = 0, total = 0;
        for (int x = minX; x <= maxX; x += 2)
            for (int k = 0; k < 2; k++)
            {
                int i = ((k == 0 ? minY : maxY) * w + x) * 4;
                if (px[i + 3] < minAlpha) continue;
                total++;
                int av = px[i + 3];
                int rr = px[i + 2] * 255 / av, gg = px[i + 1] * 255 / av, bbb = px[i] * 255 / av;
                if (Math.Max(Math.Abs(rr - br), Math.Max(Math.Abs(gg - bg), Math.Abs(bbb - bb))) > 30)
                    deviant++;
                else if (Math.Abs(av - meanA) > 32) // 半透明底的圆角渐隐沿不判为花纹
                    deviant++;
            }
        for (int y = minY; y <= maxY; y += 2)
            for (int k = 0; k < 2; k++)
            {
                int i = (y * w + (k == 0 ? minX : maxX)) * 4;
                if (px[i + 3] < minAlpha) continue;
                total++;
                int av = px[i + 3];
                int rr = px[i + 2] * 255 / av, gg = px[i + 1] * 255 / av, bbb = px[i] * 255 / av;
                if (Math.Max(Math.Abs(rr - br), Math.Max(Math.Abs(gg - bg), Math.Abs(bbb - bb))) > 30)
                    deviant++;
                else if (Math.Abs(av - meanA) > 32) // 半透明底的圆角渐隐沿不判为花纹
                    deviant++;
            }
        return total > 0 && deviant * 50 <= total;
    }

    // ---- 02：ShellExecuteEx（属性页 / 固定到开始屏幕等 verb）----

    internal const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    internal const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    /// <summary>以指定 verb 执行（SEE_MASK_INVOKEIDLIST：properties / pintohome 等）。</summary>
    internal static bool ShellExecuteVerb(string path, string verb)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST,
                lpVerb = verb,
                lpFile = path,
                nShow = SW_SHOWNORMAL,
            };
            return ShellExecuteEx(ref info);
        }
        catch (Exception ex)
        {
            Logger.Warn($"ShellExecuteEx({verb}) 失败 {path}：{ex.Message}");
            return false;
        }
    }

    // ---- 桌面图标显隐（步骤 05 GlobalPage；07 步加固）：Progman → SHELLDLL_DefView → SysListView32，
    //      WorkerW 路径兜底（SHELLDLL_DefView 有时挂在 WorkerW 下）；WM_COMMAND 0x7402 为**切换**指令，
    //      程序内以 DesktopIconsHidden 跟踪当前状态，只在需要变更时发。

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    internal const int WM_COMMAND = 0x0111;
    internal const int WM_TOGGLE_DESKTOP = 0x7402; // 切换桌面图标显隐（发往 SHELLDLL_DefView）

    /// <summary>程序视角下桌面图标当前是否被本程序隐藏（WM_COMMAND 0x7402 是切换而非置位，需自行跟踪）。
    /// 07 步起它只是**兜底记忆**：优先以 <see cref="QueryDesktopIconsVisible"/> 的真实查询为准。</summary>
    internal static bool DesktopIconsHidden { get; private set; }

    /// <summary>
    /// 桌面图标**真实**可见性（07 步加固）：直接问 SysListView32 的 WS_VISIBLE，而不是靠自记状态。
    /// 0x7402 是"切换"而非"置位"，进程被杀 / 资源管理器重启后自记状态必然失真——查询真实状态
    /// 才能保证"崩溃恢复"与"TaskbarCreated 重放"两条路径都落在正确的一侧。找不到窗口返回 null（未知）。
    /// </summary>
    internal static bool? QueryDesktopIconsVisible()
    {
        var defView = FindDesktopDefView();
        if (defView == IntPtr.Zero) return null;
        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        if (listView == IntPtr.Zero) return null; // 桌面图标视图未建立（未知，不可据此切换）
        return IsWindowVisible(listView);
    }

    /// <summary>
    /// 按目标状态切换桌面图标显隐（07 步：状态以真实查询为准，重复调用同状态为 no-op）。
    /// 返回是否**执行了切换**。真实状态查不到时退回自记状态（老路径行为），绝不盲目切换——
    /// 0x7402 是切换指令，状态未知时发出去可能把"已隐藏"变成"显示"。
    /// </summary>
    internal static bool SetDesktopIconsVisible(bool visible)
    {
        bool? actual = QueryDesktopIconsVisible();
        bool currentlyVisible = actual ?? !DesktopIconsHidden;
        if (visible == currentlyVisible)
        {
            DesktopIconsHidden = !currentlyVisible; // 同步自记状态（真实查询优先）
            return false;
        }
        var defView = FindDesktopDefView();
        if (defView == IntPtr.Zero)
        {
            Logger.Warn("桌面图标显隐切换失败：未找到 SHELLDLL_DefView");
            return false;
        }
        SendMessage(defView, WM_COMMAND, (IntPtr)WM_TOGGLE_DESKTOP, IntPtr.Zero);
        DesktopIconsHidden = !visible;
        Logger.Info($"桌面图标显隐 → {(visible ? "显示" : "隐藏")}（WM_COMMAND 0x7402；"
            + $"切换前实测={(actual.HasValue ? (actual.Value ? "可见" : "隐藏") : "未知")}）");
        return true;
    }

    /// <summary>
    /// 崩溃恢复（07 §2）：把"上次未正常退出"的桌面图标状态拉回已知的一侧。
    /// 若实测图标可见则无需动作；实测隐藏（上次进程被杀留下的状态）→ 先恢复可见，
    /// 之后调用方再按当前设置应用隐藏意图，从而把"切换型 API"变成幂等的"置位"。
    /// 返回是否真的恢复了（供调用方决定是否气泡告知）。
    /// </summary>
    internal static bool RecoverDesktopIconsIfNeeded()
    {
        var actual = QueryDesktopIconsVisible();
        if (actual == null)
        {
            Logger.Warn("桌面图标状态恢复：查询不到真实状态，跳过（不改动用户桌面）");
            return false;
        }
        if (actual.Value) return false; // 已可见：无需恢复
        Logger.Info("检测到桌面图标处于隐藏态（上次非正常退出），强制恢复可见");
        return SetDesktopIconsVisible(true);
    }

    /// <summary>定位 SHELLDLL_DefView：先 Progman，失败则遍历 WorkerW 兄弟层。</summary>
    static IntPtr FindDesktopDefView()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
        }
        IntPtr worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            var defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
        }
        return IntPtr.Zero;
    }

    // ---- 07 §2：资源管理器重启（TaskbarCreated）----

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);

    static uint _taskbarCreatedMsg;
    static bool _taskbarCreatedProbed;

    /// <summary>TaskbarCreated 消息号（07 §2：资源管理器重启后重放隐藏意图）。
    /// **按进程缓存**（技术指引）：该消息号由系统在本次会话内分配，多次注册返回同一值，
    /// 但每次 P/Invoke 都要进内核查表，钩子回调里不宜重复调用。</summary>
    internal static uint TaskbarCreatedMessage()
    {
        if (!_taskbarCreatedProbed)
        {
            _taskbarCreatedProbed = true;
            try { _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated"); }
            catch (Exception ex) { Logger.Warn($"注册 TaskbarCreated 消息失败：{ex.Message}"); }
        }
        return _taskbarCreatedMsg;
    }

    // ---- 07 §3：显示器枚举信息（型号名，零依赖）----
    // 【补充决策】步骤文件原文建议 EDID(WmiMonitorID)，但 WMI 需 System.Management 包
    // （.NET Core+ 不再是框架自带）——违反"零第三方依赖"铁律。改用 user32 的
    // EnumDisplayDevices：对每个适配器（\\.\DISPLAYn）取它挂载的监视器 DeviceString，
    // 即"DELL U2723QE"这类型号名，与 EDID 的 0xFC 描述符同源且无需解析字节、无需超时。

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    /// <summary>
    /// 适配器设备名（如 <c>\\.\DISPLAY1</c>）→ 该适配器上监视器的型号名；取不到返回空串。
    /// 与 <c>Screen.DeviceName</c> 同一命名空间，可直接按名匹配。任何异常都回退空串（调用方给基础信息）。
    ///
    /// 两条通道（本机实测：AMD 驱动下 DeviceString 恒为 "Generic PnP Monitor"，只有 EDID 通道可用）：
    /// ① 驱动直接给出的友好名（Intel/NVIDIA 常见 "DELL U2723QE"）；
    /// ② **EDID**：<c>EDD_GET_DEVICE_INTERFACE_NAME</c> 拿到 <c>\\?\DISPLAY#&lt;硬件ID&gt;#&lt;实例&gt;#...</c>，
    ///    按它精确定位注册表 <c>HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\&lt;硬件ID&gt;\&lt;实例&gt;\Device Parameters</c>
    ///    的 EDID 字节块，解析描述符 0xFC（型号名）。这是零依赖的 EDID 路线（WMI 需 System.Management 包，禁用）。
    /// </summary>
    internal static string MonitorModel(string adapterDeviceName)
    {
        try
        {
            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(adapterDeviceName, 0, ref mon, EDD_GET_DEVICE_INTERFACE_NAME))
                return "";
            string friendly = (mon.DeviceString ?? "").Trim();
            // "Generic PnP Monitor" 是"读不到真实型号"的通用占位，不当作型号展示
            bool generic = friendly.Length == 0
                || friendly.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase);
            if (!generic) return friendly;

            string edid = EdidModel(mon.DeviceID);
            if (edid.Length > 0) return edid;
            return "";
        }
        catch (Exception ex)
        {
            Logger.Warn($"显示器型号枚举失败 {adapterDeviceName}：{ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 设备接口名 <c>\\?\DISPLAY#LHCFFFF#7&amp;141258ed&amp;6&amp;UID260#{guid}</c> →
    /// 注册表 EDID → 描述符 0xFC 的型号名；任何一步失败返回空串。
    /// </summary>
    static string EdidModel(string deviceInterfaceName)
    {
        try
        {
            // 取第 2、3 段：硬件 ID 与实例（实例段在 '#' 之前，可能含 '&amp;'）
            var parts = (deviceInterfaceName ?? "").Split('#');
            if (parts.Length < 4) return "";
            string hwid = parts[1];
            string instance = parts[2];
            if (hwid.Length == 0 || instance.Length == 0) return "";

            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{hwid}\{instance}\Device Parameters");
            if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 128) return "";
            return ParseEdidModel(edid);
        }
        catch (Exception ex)
        {
            Logger.Warn($"EDID 解析失败 {deviceInterfaceName}：{ex.Message}");
            return "";
        }
    }

    /// <summary>EDID 描述符块（偏移 54/72/90/108，各 18 字节）中 0xFC 描述符的 13 字节型号名。</summary>
    static string ParseEdidModel(byte[] edid)
    {
        for (int off = 54; off + 18 <= edid.Length && off <= 108; off += 18)
        {
            // 显示器描述符：前两字节 0，第三字节 0，第四字节 = 描述符类型（0xFC = 显示器名）
            if (edid[off] != 0 || edid[off + 1] != 0 || edid[off + 2] != 0 || edid[off + 3] != 0xFC)
                continue;
            var sb = new System.Text.StringBuilder(13);
            for (int i = 5; i < 18; i++)
            {
                byte b = edid[off + i];
                if (b == 0x0A || b == 0x00) break; // 换行/填充：名称结束
                if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }
        return "";
    }

    // ---- 07 §3：GetMonitorInfoW（工作区/边界；与 WinForms.Screen 同源但可拿到设备名）----

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ---- 内存优化（2026-09-27）：工作集整理 ----

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    internal static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    /// <summary>
    /// 请求系统尽量换出当前工作集（min/max = -1,-1）。常驻托盘程序在"板已关、无交互"的
    /// 时机调用，可把任务管理器里的占用压回基线；被换出的页大多进备用列表，再次访问是
    /// 软页错误级开销，不影响功能与动画正确性。失败静默（旧系统/权限限制均无碍）。
    /// </summary>
    internal static void TrimWorkingSet()
    {
        try
        {
            SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
        }
        catch
        {
            // 整理失败不影响运行
        }
    }
}
