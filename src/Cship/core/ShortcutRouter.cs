using System;
using System.Collections.Generic;
using System.Linq;
namespace Cship.Core;

/// <summary>Esc 处理层级：数值小者优先询问（00 §6：设置窗 &gt; 收纳板；分类页不占 Esc 层，2026-09-25 改版）。
/// 2026-10-04 勘误：枚举值原为 Board=1 / Settings=2，而 <see cref="ShortcutRouter.EscPressed"/> 是
/// **升序**询问 → 实际变成"收纳板先吃 Esc"，与档位注释/00 §6 相反（实机：设置窗开着按 Esc 关掉的是收纳板）。
/// 改为 Settings 值更小，落实"设置窗优先"。</summary>
public enum EscLayer
{
    Settings = 1,
    Board = 2,
}

/// <summary>窗口/面板实现此接口接入 Esc 路由。</summary>
public interface IEscHandler
{
    /// <summary>处理了 Esc 返回 true（消费掉）；返回 false 则继续询问下一层。</summary>
    bool TryHandleEsc();
}

/// <summary>
/// Esc 路由骨架（步骤 5，本步仅 API 与空实现）：EscPressed() 依注册层级依次询问，
/// 任一层消费即止。各窗口用 PreviewKeyDown / InputBindings 调用 EscPressed()。
/// </summary>
public static class ShortcutRouter
{
    static readonly object Gate = new();
    static readonly List<(EscLayer Layer, WeakReference<IEscHandler> Ref)> Handlers = new();

    public static void Register(IEscHandler handler, EscLayer layer)
    {
        lock (Gate)
        {
            Unregister(handler);
            Handlers.Add((layer, new WeakReference<IEscHandler>(handler)));
        }
    }

    public static void Unregister(IEscHandler handler)
    {
        lock (Gate)
        {
            // 顺带清理已被回收的死引用
            Handlers.RemoveAll(h => !h.Ref.TryGetTarget(out var live) || ReferenceEquals(live, handler));
        }
    }

    /// <summary>依次询问各层（层级小者优先），任一层消费即止。返回是否有层处理。</summary>
    public static bool EscPressed()
    {
        List<(EscLayer Layer, WeakReference<IEscHandler> Ref)> snapshot;
        lock (Gate)
            snapshot = Handlers.ToList();

        foreach (var entry in snapshot.OrderBy(h => h.Layer))
        {
            if (!entry.Ref.TryGetTarget(out var handler)) continue;
            if (handler.TryHandleEsc()) return true;
        }
        return false;
    }
}
