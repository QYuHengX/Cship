using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
namespace Cship.Core;

/// <summary>托盘菜单动作标识（步骤 06 §3；自绘菜单项 → <see cref="TrayController.Request"/>）。</summary>
public enum TrayAction
{
    Preferences,
    Show,
    Reset,
    Restart,
    Exit,
}

/// <summary>
/// 托盘常驻（步骤 3；步骤 06 §3 菜单自绘化）：NotifyIcon（左键单击切换收纳板、右键弹自绘玻璃菜单）
/// + 程序图标（与 exe / 任务栏 / 任务管理器一致）。
/// **06 步变更**：不再使用 WinForms <c>ContextMenuStrip</c>——右键改为经 <see cref="MenuRequested"/>
/// 把"光标物理坐标"交给 UI 层，由 <c>GlassPopup</c> 自绘玻璃菜单弹出（跟随主题 + 材质）；
/// 动作仍复用既有四个事件，**不新增动作链路**。语言热切换由"每次弹出即按当前语言构建"天然满足，
/// 故移除了原先订阅 <c>I18n.LanguageChanged</c> 的静态订阅（等价迁移，批次九教训）。
/// </summary>
public sealed class TrayController : IDisposable
{
    readonly NotifyIcon _tray = new();
    Icon? _icon;

    /// <summary>左键单击：收纳板开着→收起（02 步接入）；未开→悬浮窗呼吸动画。</summary>
    public event Action? ToggleBoardRequested;

    /// <summary>显示悬浮窗（清单二·任务3）：悬浮窗右键隐藏后，托盘"显示"复现之。</summary>
    public event Action? ShowDockRequested;

    /// <summary>偏好设置（05 步接通）。</summary>
    public event Action? PreferencesRequested;

    /// <summary>复位：悬浮窗回到所选屏工作区顶部居中。</summary>
    public event Action? ResetRequested;

    /// <summary>重启：放单实例锁 → 拉起新进程 → 本进程退出。</summary>
    public event Action? RestartRequested;

    /// <summary>关闭：保存状态并完全退出进程。</summary>
    public event Action? ExitRequested;

    /// <summary>右键菜单请求（06 §3）：参数=光标物理像素坐标。UI 层用 GlassPopup 就地弹出。</summary>
    public event Action<System.Windows.Point>? MenuRequested;

    /// <summary>
    /// 托盘菜单动作（06 §3）：自绘菜单的每一项经它触发既有事件——保持"动作链路只有这四个事件"
    /// 的既有契约（不因菜单换成自绘而新增动作路径），同时让事件在声明类型内被真实使用。
    /// </summary>
    public void Request(TrayAction action)
    {
        switch (action)
        {
            case TrayAction.Preferences: PreferencesRequested?.Invoke(); break;
            case TrayAction.Show: ShowDockRequested?.Invoke(); break;
            case TrayAction.Reset: ResetRequested?.Invoke(); break;
            case TrayAction.Restart: RestartRequested?.Invoke(); break;
            case TrayAction.Exit: ExitRequested?.Invoke(); break;
        }
    }

    public TrayController()
    {
        _icon = TrayIcon.Load();
        _tray.Icon = _icon;
        _tray.Text = I18n.Tr("tray.tooltip"); // 文案经 I18n（07 §7：不留硬编码用户可见文字）
        _tray.Visible = true;
        _tray.MouseUp += OnMouseUp;
    }

    /// <summary>
    /// 托盘气泡提示（07 §1/§2/§3：自启动写键失败、崩溃恢复图标、热插拔迁移等边界事件告知）。
    /// 文案一律由调用方经 <see cref="I18n"/> 取（中英齐全），此处只负责显示。
    /// 任何失败静默——气泡是"锦上添花"，绝不能反过来影响主流程。
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = text;
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(5000);
        }
        catch (Exception ex)
        {
            Logger.Warn($"托盘气泡显示失败：{ex.Message}");
        }
    }

    /// <summary>"显示"项是否可用（悬浮窗处于右键隐藏态时启用；UI 层构建菜单时读它决定是否列出该项）。</summary>
    public bool ShowEnabled { get; private set; }

    /// <summary>托盘"显示"项启用/禁用（悬浮窗右键隐藏时启用，复现后禁用；App 接线调用）。</summary>
    public void SetShowEnabled(bool enabled) => ShowEnabled = enabled;

    /// <summary>托盘"偏好设置"项启用/禁用（保留接口；设置窗链路恒可用）。</summary>
    public void SetPreferencesEnabled(bool enabled) => PreferencesEnabled = enabled;

    /// <summary>"偏好设置"项是否可用。</summary>
    public bool PreferencesEnabled { get; private set; }

    void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleBoardRequested?.Invoke();
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            // 托盘图标几何：取不到（NotifyIcon 不暴露 hWnd/uID）时用光标位置——
            // 右键本就发生在图标上，光标即图标所在处（步骤 06 §3 允许的兜底）
            var p = Cursor.Position;
            MenuRequested?.Invoke(new System.Windows.Point(p.X, p.Y));
        }
    }

    public void Dispose()
    {
        _tray.MouseUp -= OnMouseUp;
        _tray.Visible = false;
        _tray.Dispose();
        // Icon 由本类持有（自文件/关联图标加载）：Dispose 即释放 GDI 句柄
        _icon?.Dispose();
        _icon = null;
    }

    /// <summary>托盘/exe 图标（步骤 06：素材 CShip.png 转制的 app.ico；与 exe 图标同源）。</summary>
    static class TrayIcon
    {
        public static Icon Load()
        {
            // ① resources\icons\app.ico（与 ApplicationIcon 同源，多尺寸：16/24/32/48/64/128/256）
            try
            {
                string path = Path.Combine(Paths.IconsDir(), "app.ico");
                if (File.Exists(path))
                    return new Icon(path, SystemInformation.SmallIconSize);
            }
            catch (Exception ex)
            {
                Logger.Warn($"托盘图标加载失败（回退 exe 关联图标）：{ex.Message}");
            }
            // ② exe 关联图标（任务管理器/任务栏同款）——保证三处观感一致
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var icon = Icon.ExtractAssociatedIcon(exe);
                    if (icon != null) return icon;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"exe 关联图标提取失败（回退自绘）：{ex.Message}");
            }
            // ③ 自绘三层方板（最终兜底，保证托盘永不空白）
            return DrawFallback();
        }

        static Icon DrawFallback()
        {
            const int size = 256;
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float plate = size * 0.60f;
                float step = size * 0.115f;
                float origin = (size - plate - 2 * step) / 2f;
                DrawPlate(g, origin + 2 * step, origin + 2 * step, plate,
                    Color.FromArgb(70, 255, 255, 255), Color.FromArgb(60, 27, 27, 27));
                DrawPlate(g, origin + step, origin + step, plate,
                    Color.FromArgb(140, 255, 255, 255), Color.FromArgb(120, 27, 27, 27));
                DrawPlate(g, origin, origin, plate,
                    Color.FromArgb(235, 249, 249, 249), Color.FromArgb(210, 27, 27, 27));
            }
            var handle = bmp.GetHicon();
            try
            {
                using var temp = Icon.FromHandle(handle);
                return (Icon)temp.Clone(); // 克隆后句柄由调用方 DestroyIcon 释放
            }
            finally
            {
                SystemBridge.DestroyIcon(handle);
            }
        }

        static void DrawPlate(Graphics g, float x, float y, float side, Color fill, Color border)
        {
            using var path = RoundedRect(x, y, side, side, side * 0.22f);
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
            using (var pen = new Pen(border, side * 0.035f))
                g.DrawPath(pen, path);
        }

        static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            float d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
