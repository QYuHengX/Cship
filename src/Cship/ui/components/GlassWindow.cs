using System;
using System.Windows;
using System.Windows.Media;
namespace Cship.Ui.Components;

/// <summary>
/// 透明无边框窗口基类（步骤 4.1）：None + AllowsTransparency + 不进任务栏。
/// 收纳板/设置窗/玻璃菜单后续都从这里继承；附带 PerMonitorV2 下
/// 物理像素 ↔ WPF DIP 的手动换算辅助（PointToScreen + PresentationSource 变换矩阵）。
/// </summary>
public class GlassWindow : Window
{
    protected GlassWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = false; // 01b 补丁：基类不再写死置顶，策略由派生类经 ApplyTopmostStrategy 声明
        ResizeMode = ResizeMode.NoResize;
        SnapsToDevicePixels = true;
    }

    /// <summary>
    /// 置顶策略钩子（01b 补丁，规格=步骤 4.8 末段）：派生类在此声明自己的 Topmost 行为——
    /// FloatingDock 按 advanced.dockPriority 三档走 ApplyPriority；BoardWindow 等
    /// 其他派生类不受该键影响，需自设置顶策略（默认保持不置顶）。
    /// 基类在 OnSourceInitialized 末尾调用一次，此时句柄已建、可安全取 HwndSource。
    /// </summary>
    protected virtual void ApplyTopmostStrategy() { }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTopmostStrategy();
    }

    /// <summary>窗口当前所在屏的水平 DPI 缩放（句柄未建时按 1.0，PerMonitorV2 下逐屏可能不同）。</summary>
    public double DpiScaleX
    {
        get
        {
            var t = DeviceTransform();
            return t == null ? 1.0 : t.Value.M11;
        }
    }

    /// <summary>窗口当前所在屏的垂直 DPI 缩放。</summary>
    public double DpiScaleY
    {
        get
        {
            var t = DeviceTransform();
            return t == null ? 1.0 : t.Value.M22;
        }
    }

    Matrix? DeviceTransform()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice;
    }

    /// <summary>窗口左上角（无衬窗，客户区=窗口）当前所在的物理像素坐标。</summary>
    public Point PhysicalOrigin()
    {
        var source = PresentationSource.FromVisual(this);
        if (source == null)
            return new Point(Left, Top);
        return PointToScreen(new Point(0, 0));
    }

    /// <summary>按物理像素坐标移动窗口（内部除以当前屏 DPI 折回 DIP）。</summary>
    public void MoveToPhysical(double physicalX, double physicalY)
    {
        Left = physicalX / DpiScaleX;
        Top = physicalY / DpiScaleY;
    }

    /// <summary>把元素裁成圆角矩形（透明窗口的圆角兜底；内容自身仍带 CornerRadius）。</summary>
    public static void ApplyRoundedClip(FrameworkElement element, double radius)
    {
        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        element.Clip = new RectangleGeometry(new Rect(new Point(0, 0), size), radius, radius);
    }
}
