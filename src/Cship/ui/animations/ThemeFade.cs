using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cship.Ui.Animations;

/// <summary>
/// 主题切换过渡（步骤 03 任务 6，供 06 步 ThemeEngine 调用）：
/// <see cref="Transition"/> 先把窗口当前观感截成位图快照，盖上一个等尺寸 Image（z 顶端），
/// 再执行 applyNewTheme()，最后快照 300ms（AnimTuning.ThemeFade）淡出后移除——
/// 新旧主题交叉淡化，颜色级插值不需要，快照淡化即可满足"切换有过渡"【原文 L28】。
/// 窗口未渲染（尺寸为 0）或内容不是 Grid 时直接执行 applyNewTheme（无过渡降级）。
/// </summary>
public static class ThemeFade
{
    /// <summary>主题切换快照交叉淡化。applyNewTheme 内应完成该窗口全部主题换装。</summary>
    public static void Transition(Window window, Action applyNewTheme)
    {
        if (window.Content is not Grid root || root.ActualWidth < 1 || root.ActualHeight < 1)
        {
            applyNewTheme();
            return;
        }

        RenderTargetBitmap snapshot;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(root);
            int pxW = Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX));
            int pxH = Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY));
            snapshot = new RenderTargetBitmap(pxW, pxH, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            snapshot.Render(root);
        }
        catch
        {
            applyNewTheme(); // 截图失败不挡换装
            return;
        }

        var overlay = new Image
        {
            Source = snapshot,
            Width = root.ActualWidth,
            Height = root.ActualHeight,
            Stretch = Stretch.None,
            IsHitTestVisible = false,
        };
        Panel.SetZIndex(overlay, int.MaxValue);
        root.Children.Add(overlay);

        applyNewTheme(); // 新主题在快照之下换装，观感为零闪烁

        Anim.Run(overlay, UIElement.OpacityProperty, 1, 0, AnimTuning.ThemeFade, EaseStyle.EaseInOutQuad,
            onDone: () => root.Children.Remove(overlay));
    }
}
