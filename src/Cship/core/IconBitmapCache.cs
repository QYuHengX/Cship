using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace Cship.Core;

/// <summary>
/// 图标位图共享缓存（内存优化 · 2026-09-27）：
/// 按 (路径, 解码宽) 缓存已冻结的 BitmapImage，同一路径的所有消费者共享同一实例。
/// 优化点：
/// ① 图标缓存 PNG 逐一 new BitmapImage 解码 → 同路径共享，多容器/弹跳幽灵/拖影零重复解码；
/// ② 设置变更/主题切换触发 RefreshContainers 全量重刷时不再重新解码（路径未变直接命中缓存）；
/// ③ 容量上限 + LRU 淘汰，防 iconcache 文件被外部大规模更换后无界增长。
/// 缓存值均为 Freeze() 后的不可变实例，任意线程共享安全。
/// 06 步 MaterialEngine / 后续分类页缩略图等需要位图的地方可直接取用。
/// </summary>
internal static class IconBitmapCache
{
    /// <summary>容量上限（按条目数；单条最大 192px 解码 ≈147KB，上限 ≈36MB 理论峰值，
    /// 实际桌面项数量级远低于此）。</summary>
    const int Capacity = 128;

    /// <summary>单次淘汰量：满员时清掉最久未访问的一批（≈1/4），而非整表清空。
    /// 全清策略在条目数 &gt; 上限时会让每次新增都触发全体重新解码（解码抖动风暴）；
    /// 按访问时间淘汰只丢弃冷条目，屏上可见图标不会被误伤。</summary>
    const int EvictCount = 32;

    static readonly object Gate = new();
    readonly static Dictionary<(string Path, int DecodeWidth), Entry> _cache = new();

    sealed class Entry
    {
        public required BitmapImage Bmp { get; init; }
        public long LastAccess { get; set; }
    }

    /// <summary>取共享位图；未命中返回 null（调用方自行决定回退）。</summary>
    public static BitmapImage? Get(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Gate)
        {
            if (!_cache.TryGetValue((path, decodeWidth), out var hit)) return null;
            hit.LastAccess = Stopwatch.GetTimestamp();
            return hit.Bmp;
        }
    }

    /// <summary>取共享位图，未命中则从磁盘解码并放入缓存；失败返回 null。</summary>
    public static BitmapImage? GetOrLoad(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var key = (path, decodeWidth);
        lock (Gate)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                hit.LastAccess = Stopwatch.GetTimestamp();
                return hit.Bmp;
            }
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
                bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            lock (Gate)
            {
                if (_cache.TryGetValue(key, out var raced))
                {
                    raced.LastAccess = Stopwatch.GetTimestamp();
                    return raced.Bmp; // 并发重复解码时复用先到者
                }
                EvictIfNeeded();
                _cache[key] = new Entry { Bmp = bmp, LastAccess = Stopwatch.GetTimestamp() };
            }
            return bmp;
        }
        catch
        {
            return null; // 缓存文件被外部清掉/损坏的兜底，与原行为一致
        }
    }

    /// <summary>调用方须持有 <see cref="Gate"/>：满员时按最近访问时间淘汰最冷的一批。</summary>
    static void EvictIfNeeded()
    {
        if (_cache.Count < Capacity) return;
        foreach (var victim in _cache.OrderBy(kv => kv.Value.LastAccess).Take(EvictCount).ToList())
            _cache.Remove(victim.Key);
    }
}
