using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cship.Core;

/// <summary>
/// 运行期存储维护（2026-10-05 · 项目体积加固）：
/// 清理 <c>dependencies\config</c> 下**可再生**的两块内容，防长期使用后无界膨胀。
/// <list type="bullet">
///   <item>iconcache：图标缓存。键含盐值版本（见 DesktopScanner.GetIconCachePath），
///         盐值一升级全量旧文件永不命中；桌面项增删改名也会留下孤儿。</item>
///   <item>assets：自定义背景图/悬浮窗层图的副本。原实现"选图即复制副本，清空设置不删副本"，
///         换过几次图就会留下十几 MB 的死副本。</item>
/// </list>
/// 两条清理都只删"明确可再生/无引用"的文件，后台线程执行、失败静默，绝不阻断或拖慢启动。
/// </summary>
internal static class StorageMaintenance
{
    /// <summary>iconcache 条目数上限。正常桌面（数百项）不会触发；超过说明积累了失效盐值/孤儿。</summary>
    const int IconCacheMaxFiles = 600;

    /// <summary>iconcache 总大小上限（单条最大约 150KB，600 条约 90MB 理论峰值，实测远低于此）。</summary>
    const long IconCacheMaxBytes = 32L * 1024 * 1024;

    /// <summary>assets 宽限期：只清理"创建已满 10 分钟"的副本，保护"刚复制、设置尚未写入"的中间态。</summary>
    static readonly TimeSpan AssetGrace = TimeSpan.FromMinutes(10);

    static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };

    /// <summary>后台跑一轮维护（启动时调用一次即可）。</summary>
    public static void RunAsync()
    {
        Task.Run(() =>
        {
            try { PruneIconCache(); }
            catch (Exception ex) { Logger.Warn($"图标缓存清理失败：{ex.Message}"); }
            try { PruneOrphanAssets(); }
            catch (Exception ex) { Logger.Warn($"素材副本清理失败：{ex.Message}"); }
        });
    }

    /// <summary>超限时先淘汰"超过 30 天未写入"的缓存，仍超限再按写入时间淘汰到一半。</summary>
    static void PruneIconCache()
    {
        var dir = new DirectoryInfo(Paths.IconCacheDir());
        if (!dir.Exists) return;
        var files = dir.GetFiles("*.png");
        if (files.Length == 0) return;
        long total = files.Sum(f => f.Length);
        if (files.Length <= IconCacheMaxFiles && total <= IconCacheMaxBytes) return;

        int removed = 0;
        var stale = files.Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30))
                         .OrderBy(f => f.LastWriteTimeUtc).ToList();
        foreach (var f in stale)
            if (TryDelete(f)) removed++;

        long remaining = total;
        var alive = files.Where(f => f.Exists).OrderBy(f => f.LastWriteTimeUtc).ToList();
        remaining = alive.Sum(f => f.Length);
        if (alive.Count > IconCacheMaxFiles || remaining > IconCacheMaxBytes)
        {
            int target = alive.Count / 2;
            for (int i = 0; i < target; i++)
                if (TryDelete(alive[i])) removed++;
        }
        if (removed > 0)
            Logger.Info($"图标缓存清理：原 {files.Length} 个/{total / 1024}KB，删除 {removed} 个");
    }

    /// <summary>删除未被 settings 引用的素材副本（宽限期内的新文件一律保留）。</summary>
    static void PruneOrphanAssets()
    {
        var dir = new DirectoryInfo(Paths.AssetsDir());
        if (!dir.Exists) return;
        var referenced = ReferencedAssets();
        var cutoff = DateTime.UtcNow - AssetGrace;
        int removed = 0;
        foreach (var f in dir.GetFiles("*"))
        {
            if (!ImageExts.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)) continue; // 非图片不动
            if (referenced.Contains(f.Name)) continue;
            if (f.CreationTimeUtc > cutoff) continue; // 刚复制进来：设置可能还没写入
            if (TryDelete(f)) removed++;
        }
        if (removed > 0)
            Logger.Info($"素材副本清理：删除 {removed} 个无引用文件（assets）");
    }

    /// <summary>当前设置引用的 assets 文件名集合（背景图 + 悬浮窗三层图）。</summary>
    static HashSet<string> ReferencedAssets()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var s = SettingsStore.Instance;

        // personal.boardBg = { image: "<文件名>", crop: {...} }
        var bg = s.GetRawValue("personal.boardBg");
        if (bg.ValueKind == JsonValueKind.Object
            && bg.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String
            && img.GetString() is { Length: > 0 } bgName)
            set.Add(Path.GetFileName(bgName));

        // personal.dockCustom.images = ["<文件名>", ...]（相对 assets 引用，见 FloatingDock.ReadArray）
        var imgs = s.GetRawValue("personal.dockCustom.images");
        if (imgs.ValueKind == JsonValueKind.Array)
            foreach (var v in imgs.EnumerateArray())
                if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } name)
                    set.Add(Path.GetFileName(name));

        return set;
    }

    static bool TryDelete(FileInfo file)
    {
        try { file.Delete(); return true; }
        catch { return false; } // 被占用/权限不足：留待下次
    }
}
