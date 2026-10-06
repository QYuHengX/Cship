using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cship.Core;

namespace Cship.Ui.Animations;

/// <summary>
/// 单个收纳板开合动画预设（步骤 03 §3 接口）：Open/Close 都只操作收纳板根内容的
/// RenderTransform（Translate/Scale）与 Opacity，完成后经 Anim 的复位语义清场
/// （动画移除 + 基值落定 + RenderTransform 置空），不残留任何属性——可重复开合、
/// 中途换预设也不互相污染。dockPos=悬浮窗三板组中心（物理像素）；anchorEdge=弹出方向
/// （up/down/left/right/center，auto 已由 BoardWindow 折算）。
/// </summary>
/// <param name="Id">settings 键 personal.boardAnim 的取值。</param>
/// <param name="I18nName">预设显示名的 i18n 键（05 步设置页枚举注册表生成选项）。</param>
public sealed record BoardAnimPreset(
    string Id,
    string I18nName,
    Action<BoardWindow, Point, string, Action?> Open,
    Action<BoardWindow, Point, string, Action?> Close);

/// <summary>
/// 收纳板开合动画预设注册表（步骤 03 §3【原文 L5、L63】，取代 02 的 BoardAnimController）：
/// expand（默认，= 00 §7 弹出动画口径）/ spitOut 吐出吸入 / slideEdge 滑出 / popBounce 弹跳 /
/// fadeSoft 淡化，共 5 套。注册表可扩展：新预设加一个 BoardAnimPreset 实例进 All 即可，
/// 05 步设置页枚举 All 生成选项与预览。
/// 输入遮罩（动画期间防连点）由 BoardWindow 在调用 Open/Close 前后装配（Attach/DetachAnimGuard）。
/// </summary>
public static class BoardAnimPresets
{
    /// <summary>center 方向下 slideEdge/fadeSoft 类"上滑"备用的位移（px）。</summary>
    const double CenterRisePx = 48;

    // ---- 注册表（03 §3 表）----

    public static readonly BoardAnimPreset Expand = new(
        "expand", "anim.expand", OpenExpand, CloseExpand);

    public static readonly BoardAnimPreset SpitOut = new(
        "spitOut", "anim.spitOut", OpenSpitOut, CloseSpitOut);

    public static readonly BoardAnimPreset SlideEdge = new(
        "slideEdge", "anim.slideEdge", OpenSlideEdge, CloseSlideEdge);

    public static readonly BoardAnimPreset PopBounce = new(
        "popBounce", "anim.popBounce", OpenPopBounce, ClosePopBounce);

    public static readonly BoardAnimPreset FadeSoft = new(
        "fadeSoft", "anim.fadeSoft", OpenFadeSoft, CloseFadeSoft);

    /// <summary>全部预设（05 步设置页枚举用；expand 恒第一=默认）。</summary>
    public static readonly IReadOnlyList<BoardAnimPreset> All = new[]
    {
        Expand, SpitOut, SlideEdge, PopBounce, FadeSoft,
    };

    /// <summary>当前设置对应的预设（键缺失/非法回退 expand）。</summary>
    public static BoardAnimPreset Current()
        => Find(SettingsStore.Instance.GetString("personal.boardAnim", "slideEdge")) ?? Expand;

    /// <summary>按 Id 查预设；未命中返回 null。</summary>
    public static BoardAnimPreset? Find(string? id)
        => id == null ? null : All.FirstOrDefault(p => string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    // ---- 公共小件 ----

    static UIElement RootOf(BoardWindow board) => (UIElement)board.Content;

    /// <summary>滑入轴与起始符号：up=自下方滑上来(+Y)，down=自上方(-Y)，left=自左方(-X)，right=自右方(+X)。</summary>
    static (bool Horizontal, double Sign)? SlideOf(string edge) => edge switch
    {
        "up" => (false, +1),
        "down" => (false, -1),
        "left" => (true, -1),
        "right" => (true, +1),
        _ => null, // center：无屏幕边
    };

    /// <summary>悬浮窗中心换算到收纳板内容坐标系（DIP）；失败回退板中心（多屏夹边瞬间等罕见场景）。</summary>
    static Point OriginInBoard(BoardWindow board, Point dockPosPhysical)
    {
        try
        {
            return board.PointFromScreen(dockPosPhysical);
        }
        catch
        {
            return new Point(board.ActualWidth / 2, board.ActualHeight / 2);
        }
    }

    /// <summary>悬浮窗中心相对板中心的位移向量（DIP）——spitOut 的压缩点/吸回落点。</summary>
    static (double Dx, double Dy) DockOffset(BoardWindow board, Point dockPosPhysical)
    {
        var o = OriginInBoard(board, dockPosPhysical);
        return (o.X - board.ActualWidth / 2, o.Y - board.ActualHeight / 2);
    }

    /// <summary> detachment 收尾：清 RenderTransform 再回调（动画的基值复位已由 Anim 完成）。</summary>
    static void Finish(UIElement root, Action? done)
    {
        root.RenderTransform = null;
        done?.Invoke();
    }

    // ---- expand（默认；= 00 §7 弹出动画：锚边屏幕外丝滑滑入，center 自中心缩放）----

    static void OpenExpand(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        var root = RootOf(board);
        var slide = SlideOf(edge);
        if (slide is { } s)
        {
            double dist = (s.Horizontal ? board.ActualWidth : board.ActualHeight) + AnimTuning.SlideOutMarginPx;
            var tf = new TranslateTransform(s.Horizontal ? s.Sign * dist : 0, s.Horizontal ? 0 : s.Sign * dist);
            root.RenderTransform = tf;
            var prop = s.Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            Anim.Run(tf, prop, s.Sign * dist, 0, AnimTuning.Enter, EaseStyle.EaseOutCubic, () => Finish(root, done));
            Anim.Run(root, UIElement.OpacityProperty, AnimTuning.FadeOpenFrom, 1, AnimTuning.Enter, EaseStyle.EaseOutCubic);
            return;
        }
        var origin = OriginInBoard(board, dockPos);
        var scale = new ScaleTransform(0.1, 0.1, origin.X, origin.Y);
        root.RenderTransform = scale;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, 0.1, 1, AnimTuning.Enter, EaseStyle.EaseOutCubic, ok),
            ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, 0.1, 1, AnimTuning.Enter, EaseStyle.EaseOutCubic, ok),
            ok => Anim.Run(root, UIElement.OpacityProperty, 0, 1, AnimTuning.Enter, EaseStyle.EaseOutCubic, ok),
        }, () => Finish(root, done));
    }

    static void CloseExpand(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        var root = RootOf(board);
        var slide = SlideOf(edge);
        if (slide is { } s)
        {
            double dist = (s.Horizontal ? board.ActualWidth : board.ActualHeight) + AnimTuning.SlideOutMarginPx;
            var tf = new TranslateTransform(0, 0);
            root.RenderTransform = tf;
            var prop = s.Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            Anim.Run(tf, prop, null, s.Sign * dist, AnimTuning.Exit, EaseStyle.EaseInCubic, () => Finish(root, done));
            Anim.Run(root, UIElement.OpacityProperty, null, AnimTuning.FadeOpenFrom, AnimTuning.Exit, EaseStyle.EaseInCubic);
            return;
        }
        var origin = OriginInBoard(board, dockPos);
        var scale = new ScaleTransform(1, 1, origin.X, origin.Y);
        root.RenderTransform = scale;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, null, 0.1, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
            ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, null, 0.1, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
            ok => Anim.Run(root, UIElement.OpacityProperty, null, 0, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
        }, () => Finish(root, done));
    }

    // ---- spitOut 吐出/吸入 ----

    static void OpenSpitOut(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        _ = edge; // 吐出方向由悬浮窗与板的相对位置决定（压缩点=悬浮窗处）
        var root = RootOf(board);
        var (dx, dy) = DockOffset(board, dockPos);
        var translate = new TranslateTransform(dx, dy);
        var scale = new ScaleTransform(AnimTuning.SpitScaleMin, AnimTuning.SpitScaleMin, dx + board.ActualWidth / 2, dy + board.ActualHeight / 2);
        root.RenderTransform = new TransformGroup { Children = { translate, scale } };
        Anim.Sequence(new Action<Action>[]
        {
            // ① 在悬浮窗处压缩呈现（150ms easeIn）：0.1 → 0.4，同点淡入
            next => Anim.Parallel(new Action<Action>[]
            {
                ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, 0.1, AnimTuning.SpitScaleMin, AnimTuning.SpitPress, EaseStyle.EaseInQuad, ok),
                ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, 0.1, AnimTuning.SpitScaleMin, AnimTuning.SpitPress, EaseStyle.EaseInQuad, ok),
                ok => Anim.Run(root, UIElement.OpacityProperty, 0, 1, AnimTuning.SpitPress, EaseStyle.EaseInQuad, ok),
            }, next),
            // ② 向锚边方向（=悬浮窗→板心）easeOutBack 弹开（350ms，峰值过冲 ≤1.08）
            next => Anim.Parallel(new Action<Action>[]
            {
                ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, null, 1, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
                ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, null, 1, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
                ok => Anim.Run(translate, TranslateTransform.XProperty, null, 0, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
                ok => Anim.Run(translate, TranslateTransform.YProperty, null, 0, AnimTuning.SpitPop, EaseStyle.EaseOutBack, ok),
            }, () => Finish(root, next)),
        }, done);
    }

    static void CloseSpitOut(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        _ = edge;
        var root = RootOf(board);
        var (dx, dy) = DockOffset(board, dockPos);
        var translate = new TranslateTransform(0, 0);
        // 缩放中心与打开段同点（=悬浮窗在板内坐标），末端板心恰好收进悬浮窗点
        var scale = new ScaleTransform(1, 1, board.ActualWidth / 2 + dx, board.ActualHeight / 2 + dy);
        root.RenderTransform = new TransformGroup { Children = { translate, scale } };
        Anim.Sequence(new Action<Action>[]
        {
            // ① 被"吸回"悬浮窗：反向收缩 250ms easeIn
            next => Anim.Parallel(new Action<Action>[]
            {
                ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, null, AnimTuning.SpitScaleMin, AnimTuning.SpitSuck, EaseStyle.EaseInQuad, ok),
                ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, null, AnimTuning.SpitScaleMin, AnimTuning.SpitSuck, EaseStyle.EaseInQuad, ok),
                ok => Anim.Run(translate, TranslateTransform.XProperty, null, dx, AnimTuning.SpitSuck, EaseStyle.EaseInQuad, ok),
                ok => Anim.Run(translate, TranslateTransform.YProperty, null, dy, AnimTuning.SpitSuck, EaseStyle.EaseInQuad, ok),
            }, next),
            // ② 末端缩至 0.2 淡出
            next => Anim.Parallel(new Action<Action>[]
            {
                ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, null, AnimTuning.SpitScaleEnd, AnimTuning.SpitSuckTail, EaseStyle.EaseInQuad, ok),
                ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, null, AnimTuning.SpitScaleEnd, AnimTuning.SpitSuckTail, EaseStyle.EaseInQuad, ok),
                ok => Anim.Run(root, UIElement.OpacityProperty, null, 0, AnimTuning.SpitSuckTail, EaseStyle.EaseInQuad, ok),
            }, () => Finish(root, next)),
        }, done);
    }

    // ---- slideEdge 滑出（依锚边从屏幕边滑入；纯平移不淡入，与 expand 区分）----

    static void OpenSlideEdge(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        var root = RootOf(board);
        var slide = SlideOf(edge);
        if (slide is { } s)
        {
            double dist = (s.Horizontal ? board.ActualWidth : board.ActualHeight) + AnimTuning.SlideOutMarginPx;
            var tf = new TranslateTransform(s.Horizontal ? s.Sign * dist : 0, s.Horizontal ? 0 : s.Sign * dist);
            root.RenderTransform = tf;
            var prop = s.Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            Anim.Run(tf, prop, s.Sign * dist, 0, AnimTuning.SlideEdgeOpen, EaseStyle.EaseOutCubic, () => Finish(root, done));
            return;
        }
        // center 无屏幕边：退化为自下方上滑+淡入
        var tfC = new TranslateTransform(0, CenterRisePx);
        root.RenderTransform = tfC;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(tfC, TranslateTransform.YProperty, CenterRisePx, 0, AnimTuning.SlideEdgeOpen, EaseStyle.EaseOutCubic, ok),
            ok => Anim.Run(root, UIElement.OpacityProperty, 0, 1, AnimTuning.SlideEdgeOpen, EaseStyle.EaseOutCubic, ok),
        }, () => Finish(root, done));
    }

    static void CloseSlideEdge(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        var root = RootOf(board);
        var slide = SlideOf(edge);
        if (slide is { } s)
        {
            double dist = (s.Horizontal ? board.ActualWidth : board.ActualHeight) + AnimTuning.SlideOutMarginPx;
            var tf = new TranslateTransform(0, 0);
            root.RenderTransform = tf;
            var prop = s.Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            Anim.Run(tf, prop, null, s.Sign * dist, AnimTuning.Exit, EaseStyle.EaseInCubic, () => Finish(root, done));
            return;
        }
        var tfC = new TranslateTransform(0, 0);
        root.RenderTransform = tfC;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(tfC, TranslateTransform.YProperty, null, CenterRisePx, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
            ok => Anim.Run(root, UIElement.OpacityProperty, null, 0, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
        }, () => Finish(root, done));
    }

    // ---- popBounce 弹跳（scale 0→1.06→1 两段；关闭缩至 0.9 淡出）----

    static void OpenPopBounce(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        _ = edge;
        var root = RootOf(board);
        var origin = OriginInBoard(board, dockPos);
        var scale = new ScaleTransform(0, 0, origin.X, origin.Y);
        root.RenderTransform = scale;
        double peakAt = AnimTuning.PopBounce * 0.6; // 过冲点在 60% 处
        Anim.RunKeys(scale, ScaleTransform.ScaleXProperty, new (double, double, EaseStyle)[]
        {
            (0, 0, EaseStyle.EaseOutQuad),
            (AnimTuning.PopBouncePeak, peakAt, EaseStyle.EaseOutQuad),
            (1, AnimTuning.PopBounce, EaseStyle.EaseInOutQuad),
        }, () => Finish(root, done));
        Anim.RunKeys(scale, ScaleTransform.ScaleYProperty, new (double, double, EaseStyle)[]
        {
            (0, 0, EaseStyle.EaseOutQuad),
            (AnimTuning.PopBouncePeak, peakAt, EaseStyle.EaseOutQuad),
            (1, AnimTuning.PopBounce, EaseStyle.EaseInOutQuad),
        });
        Anim.Run(root, UIElement.OpacityProperty, 0, 1, AnimTuning.Micro, EaseStyle.Linear);
    }

    static void ClosePopBounce(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        _ = edge;
        var root = RootOf(board);
        var origin = OriginInBoard(board, dockPos);
        var scale = new ScaleTransform(1, 1, origin.X, origin.Y);
        root.RenderTransform = scale;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(scale, ScaleTransform.ScaleXProperty, null, 0.9, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
            ok => Anim.Run(scale, ScaleTransform.ScaleYProperty, null, 0.9, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
            ok => Anim.Run(root, UIElement.OpacityProperty, null, 0, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
        }, () => Finish(root, done));
    }

    // ---- fadeSoft 淡化（opacity 0→1 + 上移 8px；关闭倒放）----

    static void OpenFadeSoft(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        _ = dockPos;
        _ = edge;
        var root = RootOf(board);
        var tf = new TranslateTransform(0, AnimTuning.SoftFadeRise);
        root.RenderTransform = tf;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(tf, TranslateTransform.YProperty, AnimTuning.SoftFadeRise, 0, AnimTuning.SoftFade, EaseStyle.EaseOutCubic, ok),
            ok => Anim.Run(root, UIElement.OpacityProperty, 0, 1, AnimTuning.SoftFade, EaseStyle.EaseOutCubic, ok),
        }, () => Finish(root, done));
    }

    static void CloseFadeSoft(BoardWindow board, Point dockPos, string edge, Action? done)
    {
        _ = dockPos;
        _ = edge;
        var root = RootOf(board);
        var tf = new TranslateTransform(0, 0);
        root.RenderTransform = tf;
        Anim.Parallel(new Action<Action>[]
        {
            ok => Anim.Run(tf, TranslateTransform.YProperty, null, AnimTuning.SoftFadeRise, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
            ok => Anim.Run(root, UIElement.OpacityProperty, null, 0, AnimTuning.Exit, EaseStyle.EaseInCubic, ok),
        }, () => Finish(root, done));
    }
}
