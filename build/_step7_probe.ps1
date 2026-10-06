# 07 步系统集成探针（只读为主；taskbar 模式仅广播消息，不动 explorer）
# 用法: pwsh -NoProfile -File build\_step7_probe.ps1 <screens|icons|taskbar|icontoggle>
param([string]$Mode = 'screens')

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class P7
{
    // 用 class（引用语义）：PowerShell 的 [ref] 对 struct 会传副本，导致 cb 丢失 → 调用失败
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName = "";
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString = "";
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID = "";
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey = "";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, [In, Out] DISPLAY_DEVICE d, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string cls, string win);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string win);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string s);

    public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    // WPF 窗口的 Process.MainWindowHandle 常为 0（不可靠）→ 用 EnumWindows 数"该进程的可见顶层窗口"
    public static int VisibleWindowCount(uint pid)
    {
        int n = 0;
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero);
        return n;
    }

    public const int WM_COMMAND = 0x0111;
    public const int WM_TOGGLE_DESKTOP = 0x7402;
    public static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;

    // PowerShell 会把 $null 转成 ""（空串）而不是 NULL 指针 → 必须由 C# 侧传真正的 null
    public static bool EnumAdapters(uint i, DISPLAY_DEVICE d) => EnumDisplayDevices(null, i, d, 0);
    public static bool EnumMonitor(string adapter, DISPLAY_DEVICE d) => EnumDisplayDevices(adapter, 0, d, 0);

    public static IntPtr DefView()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            var dv = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) return dv;
        }
        IntPtr w = IntPtr.Zero;
        while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
        {
            var dv = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) return dv;
        }
        return IntPtr.Zero;
    }

    public static string IconState()
    {
        var dv = DefView();
        if (dv == IntPtr.Zero) return "DefView=NOT_FOUND";
        var lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
        if (lv == IntPtr.Zero) return "SysListView32=NOT_FOUND";
        return "SysListView32 visible=" + IsWindowVisible(lv);
    }

    public static void Toggle() { var dv = DefView(); if (dv != IntPtr.Zero) SendMessage(dv, WM_COMMAND, (IntPtr)WM_TOGGLE_DESKTOP, IntPtr.Zero); }

    public static uint BroadcastTaskbarCreated()
    {
        uint msg = RegisterWindowMessage("TaskbarCreated");
        IntPtr r;
        SendMessageTimeout(HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero, 0x0002, 3000, out r);
        return msg;
    }

    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;

    /// <summary>该进程所有可见顶层窗口的矩形（返回 "x,y,w,h" 列表）。</summary>
    public static string[] VisibleWindowRects(uint pid)
    {
        var list = new System.Collections.Generic.List<string>();
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h))
            {
                RECT r; if (GetWindowRect(h, out r) && r.Right > r.Left && r.Bottom > r.Top)
                    list.Add($"{r.Left},{r.Top},{r.Right - r.Left},{r.Bottom - r.Top}");
            }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    /// <summary>前台窗口是否属于该进程（点击前安全检查：避免把点击送进别人的全屏程序）。</summary>
    public static bool ForegroundIs(uint pid)
    {
        uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p);
        return p == pid;
    }

    /// <summary>仅当点击点命中的窗口属于该进程时才点击（防误点桌面/其他程序）。</summary>
    public static string ClickIfOwned(uint pid, int x, int y)
    {
        uint owner; GetWindowThreadProcessId(WindowFromPoint(new POINT { X = x, Y = y }), out owner);
        if (owner != pid) return $"SKIP: point({x},{y}) belongs to pid={owner}, not {pid}";
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        return $"CLICKED ({x},{y})";
    }

    /// <summary>在两点之间扫掠鼠标（只移动、不点击）——用于驱动 hoverReveal 的 MouseMove 路径。
    /// 起点命中窗口不属于该进程时拒绝执行（防误动其他程序）。</summary>
    public static string SweepIfOwned(uint pid, int x1, int y1, int x2, int y2, int steps)
    {
        uint owner; GetWindowThreadProcessId(WindowFromPoint(new POINT { X = x1, Y = y1 }), out owner);
        if (owner != pid) return $"SKIP: start({x1},{y1}) belongs to pid={owner}, not {pid}";
        for (int i = 0; i <= steps; i++)
        {
            int x = x1 + (x2 - x1) * i / steps;
            int y = y1 + (y2 - y1) * i / steps;
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(12);
        }
        return $"SWEPT ({x1},{y1})->({x2},{y2}) in {steps} steps";
    }

    // WM_DISPLAYCHANGE（SystemEvents.DisplaySettingsChanged 的底层广播）——不改变真实显示设置，
    // 只驱动监听链路，用于验证"热插拔/分辨率变化"的处理路径可达
    public const int WM_DISPLAYCHANGE = 0x007E;
    public static void BroadcastDisplayChange()
    {
        IntPtr r;
        SendMessageTimeout(HWND_BROADCAST, WM_DISPLAYCHANGE, (IntPtr)32, (IntPtr)(1920 | (1080 << 16)), 0x0002, 3000, out r);
    }
}
'@

switch ($Mode) {
    'screens' {
        Write-Host '--- adapters + monitors (EnumDisplayDevices) ---'
        for ($i = 0; $i -lt 8; $i++) {
            $ad = New-Object P7+DISPLAY_DEVICE
            $ad.cb = [Runtime.InteropServices.Marshal]::SizeOf([type]'P7+DISPLAY_DEVICE')
            if (-not [P7]::EnumAdapters([uint32]$i, $ad)) { break }
            $mon = New-Object P7+DISPLAY_DEVICE
            $mon.cb = $ad.cb
            $ok = [P7]::EnumMonitor($ad.DeviceName, $mon)
            Write-Host ("{0}  adapter='{1}'  monitor='{2}'" -f $ad.DeviceName, $ad.DeviceString, $(if ($ok) { $mon.DeviceString } else { '<none>' }))
        }
        Write-Host '--- WinForms Screen.AllScreens ---'
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
            Write-Host ("{0}  bounds={1}  primary={2}" -f $_.DeviceName, $_.Bounds, $_.Primary)
        }
    }
    'icons' { Write-Host ([P7]::IconState()) }
    'win' { Write-Host ([P7]::VisibleWindowCount([uint32]$args[0])) }
    'icontoggle' {
        Write-Host ('before: ' + [P7]::IconState())
        [P7]::Toggle()
        Start-Sleep -Milliseconds 400
        Write-Host ('after toggle 1: ' + [P7]::IconState())
        [P7]::Toggle()
        Start-Sleep -Milliseconds 400
        Write-Host ('after toggle 2 (restored): ' + [P7]::IconState())
    }
    'taskbar' {
        $m = [P7]::BroadcastTaskbarCreated()
        Write-Host ("TaskbarCreated registered=0x{0:X4} and broadcast to all top-level windows" -f $m)
    }
    'display' {
        [P7]::BroadcastDisplayChange()
        Write-Host 'WM_DISPLAYCHANGE (0x007E) broadcast to all top-level windows'
    }
    'rects' { [P7]::VisibleWindowRects([uint32]$args[0]) | ForEach-Object { Write-Host $_ } }
    'fg' { Write-Host ('foreground is target pid: ' + [P7]::ForegroundIs([uint32]$args[0])) }
    'click' { Write-Host ([P7]::ClickIfOwned([uint32]$args[0], [int]$args[1], [int]$args[2])) }
    'sweep' { Write-Host ([P7]::SweepIfOwned([uint32]$args[0], [int]$args[1], [int]$args[2], [int]$args[3], [int]$args[4], [int]$args[5])) }
}
