using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cship.Models;

namespace Cship.Ui.Components;

/// <summary>
/// 图标网格面板（步骤 1.1 的 WrapPanel 变体）：不自行流式布局，
/// 按 BoardItem.Row/Col 定位（单元格分配归 BoardWindow——支持 iconOrder 自定义位置
/// 与剩余项自动补位），行列间距由 CellWidth/CellHeight/ColSpacing/RowSpacing 决定。
/// 2026-08-29 批次六：单元格变化不再瞬跳——每格有当前/目标坐标，渲染帧按比例逼近
/// （约 120ms 收敛，与平滑滚动同口径）：换位、补位、腾位、缩放结束重排均为平滑过渡。
/// </summary>
public sealed class IconGridPanel : Panel
{
    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
        nameof(CellWidth), typeof(double), typeof(IconGridPanel), new PropertyMetadata(64.0));
    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
        nameof(CellHeight), typeof(double), typeof(IconGridPanel), new PropertyMetadata(104.0));
    public static readonly DependencyProperty ColSpacingProperty = DependencyProperty.Register(
        nameof(ColSpacing), typeof(double), typeof(IconGridPanel), new PropertyMetadata(12.0));
    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(IconGridPanel), new PropertyMetadata(12.0));

    public double CellWidth
    {
        get => (double)GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }
    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }
    public double ColSpacing
    {
        get => (double)GetValue(ColSpacingProperty);
        set => SetValue(ColSpacingProperty, value);
    }
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    const double Approach = 0.22;   // 渲染帧逼近比例（≈120ms 收敛，与滚轮平滑一致）
    const double Epsilon = 0.5;

    readonly Dictionary<UIElement, Point> _glide = new(); // 子元素当前绘制位置（向目标格逼近）
    bool _subscribed;

    static BoardItem? ItemOf(UIElement child)
        => (child as FrameworkElement)?.DataContext as BoardItem;

    void TargetOf(UIElement child, out double x, out double y)
    {
        x = 0;
        y = 0;
        var item = ItemOf(child);
        if (item != null && item.Col >= 0)
        {
            x = item.Col * (CellWidth + ColSpacing);
            y = Math.Max(0, item.Row) * (CellHeight + RowSpacing);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(CellWidth, double.PositiveInfinity));

        double maxCol = -1, maxRow = -1;
        foreach (UIElement child in InternalChildren)
        {
            var item = ItemOf(child);
            if (item == null || item.Col < 0) continue;
            maxCol = Math.Max(maxCol, item.Col);
            maxRow = Math.Max(maxRow, item.Row);
        }
        double height = maxRow < 0 ? 0 : (maxRow + 1) * (CellHeight + RowSpacing) - RowSpacing;
        // 宽度吃满视口（右键/命中区域全宽可用），纵向由内容撑开滚动
        double width = double.IsInfinity(availableSize.Width) ? (maxCol + 1) * (CellWidth + ColSpacing) - ColSpacing : availableSize.Width;
        if (double.IsNaN(width) || width < 0) width = 0;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            TargetOf(child, out double x, out double y);
            if (!_glide.TryGetValue(child, out var cur))
            {
                _glide[child] = new Point(x, y); // 首次出现直接落位（不做飞入）
                child.Arrange(new Rect(x, y, CellWidth, CellHeight));
                continue;
            }
            bool moving = Math.Abs(cur.X - x) > Epsilon || Math.Abs(cur.Y - y) > Epsilon;
            var draw = moving ? cur : new Point(x, y);
            _glide[child] = draw;
            child.Arrange(new Rect(draw.X, draw.Y, CellWidth, CellHeight));
            if (moving) StartGlide();
        }
        return finalSize;
    }

    void StartGlide()
    {
        if (_subscribed) return;
        _subscribed = true;
        CompositionTarget.Rendering += OnGlideFrame;
    }

    void OnGlideFrame(object? sender, EventArgs e)
    {
        bool moving = false;
        foreach (UIElement child in InternalChildren)
        {
            if (!_glide.TryGetValue(child, out var cur)) continue;
            TargetOf(child, out double tx, out double ty);
            double nx = cur.X + (tx - cur.X) * Approach;
            double ny = cur.Y + (ty - cur.Y) * Approach;
            if (Math.Abs(tx - nx) <= Epsilon && Math.Abs(ty - ny) <= Epsilon)
            {
                nx = tx;
                ny = ty;
            }
            else
            {
                moving = true;
            }
            _glide[child] = new Point(nx, ny);
        }
        InvalidateArrange(); // 用最新当前值重排
        if (!moving)
        {
            _subscribed = false;
            CompositionTarget.Rendering -= OnGlideFrame;
        }
    }

    protected override void OnVisualChildrenChanged(DependencyObject? added, DependencyObject? removed)
    {
        if (removed is UIElement uie)
            _glide.Remove(uie); // 容器销毁时清理，防泄漏
        base.OnVisualChildrenChanged(added, removed);
    }

    /// <summary>
    /// 清空落位记忆（2026-08-30 批次十一）：容器可能在 Row/Col 尚未分配（-1）时完成首次
    /// arrange——所有子元素按 (0,0) 落位并被 _glide 记住；布局分配补跑后调用本方法清记忆，
    /// 下次 arrange 视同首次出现直接落到正确格子（避免全板图标从 (0,0) 集体飞入）。
    /// </summary>
    public void ResetLayout() => _glide.Clear();
}
