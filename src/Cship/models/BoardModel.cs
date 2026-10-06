using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;

namespace Cship.Models;

/// <summary>
/// 收纳板数据模型（步骤 2.2）：过滤（advanced.showApps/showFiles/showFolders）+ 排序
/// （名称/大小/类型/修改时间 × 升/降，默认名称升序）。Items 为**可见集合**，
/// 源全量保留在内部 _source；刷新用差量对账（复用实例、Move 调序），保选中与滚动位置。
/// </summary>
public sealed class BoardModel
{
    public enum SortKey { Name, Size, Type, Mtime }

    readonly List<BoardItem> _source = new();
    SortKey _key = SortKey.Name;
    bool _asc = true;
    bool _apps = true, _files = true, _folders = true;

    public ObservableCollection<BoardItem> Items { get; } = new();

    public (SortKey Key, bool Ascending) CurrentSort => (_key, _asc);

    public bool ShowApps { get => _apps; set { if (_apps != value) { _apps = value; Reconcile(); } } }
    public bool ShowFiles { get => _files; set { if (_files != value) { _files = value; Reconcile(); } } }
    public bool ShowFolders { get => _folders; set { if (_folders != value) { _folders = value; Reconcile(); } } }

    /// <summary>
    /// 分类视图过滤（步骤 04 · 2026-09-25 改版）：null=主板全部内容（"全部"分类）；
    /// 设置后需手动调用 <see cref="Reconcile"/> 对账（与 Show* 开关叠加）。
    /// </summary>
    public Func<BoardItem, bool>? ViewFilter { get; set; }

    // 用户"移除"（不在收纳板显示，2026-08-29）：按文件名过滤，随 state.removedItems 持久化
    HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    public ISet<string> Removed => _removed;

    /// <summary>设置移除名单并立即对账（修订B语义：名单仅由"刷新"重建式拉取复位复活，
    /// watcher 差量刷新不复活）。</summary>
    public void SetRemoved(IEnumerable<string> names)
    {
        _removed = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        Reconcile();
    }

    /// <summary>把文件加入移除名单并对账（右键菜单"移除"）。**保留在源集合中**（修订B语义：
    /// 移除=仅当前视图隐藏，拖入/刷新可复活——_source 恒为桌面全量，复活只需移出名单）。</summary>
    public void RemoveName(string name)
    {
        if (_removed.Add(name))
            Reconcile(); // Pass 过滤 → 从可见集合摘除
    }

    /// <summary>把文件移出移除名单并复活（修订B语义：拖入/刷新可复活）。</summary>
    public void RestoreName(string name)
    {
        if (_removed.Remove(name))
            Reconcile(); // Pass 放行 → 重新进入可见集合
    }

    public IEnumerable<BoardItem> All => _source;
    public int VisibleCount => Items.Count;

    /// <summary>当前过滤+排序配置下的期望可见序列（刷新重建先算序用，清单三·任务12）。</summary>
    public List<BoardItem> DesiredSequence() => ComputeDesired();

    /// <summary>整源替换（扫描结果），并按当前过滤+排序对账可见集合。</summary>
    public void ReplaceSource(IEnumerable<BoardItem> scanned)
    {
        SetSource(scanned);
        Reconcile();
    }

    /// <summary>只替换源不对账（配合 Reconcile/ReconcileBatched 分步用）。</summary>
    public void SetSource(IEnumerable<BoardItem> scanned)
    {
        _source.Clear();
        _source.AddRange(scanned);
    }

    /// <summary>单项移除（拖入垃圾桶成功后立即调用，不等 watcher）。</summary>
    public void RemoveItem(BoardItem item)
    {
        _source.Remove(item);
        Items.Remove(item);
    }

    /// <summary>新增单项（拖入收纳的"仅收纳板快捷方式"，2026-08-29 批次六）：
    /// 进源集合（此后刷新对账不丢）并追加到可见集合尾部，随后由 BoardWindow 按落点重排。</summary>
    public void AddItem(BoardItem item)
    {
        _source.Add(item);
        Items.Add(item);
    }

    public void SetSort(SortKey key, bool ascending)
    {
        _key = key;
        _asc = ascending;
        _comparer = null; // 排序口径变了：重建比较器
        Reconcile();
    }

    /// <summary>可见集合对账：移除消失项 → 复用实例按需 Move/Insert 到目标位（最小操作，滚动不跳）。</summary>
    public void Reconcile()
    {
        _generation++;
        var desired = ComputeDesired();
        if (desired.Count == 0)
        {
            if (Items.Count > 0) Items.Clear();
            return;
        }
        // 性能（2026-10-05）：原实现用 List.Contains/IndexOf 做成员判定，n 个项一次对账 O(n²)
        // 次引用比较（watcher 每 0.5s 防抖刷新都会跑）。BoardItem 未重写 Equals/GetHashCode=
        // 引用相等语义，用 ReferenceEqualityComparer 的 HashSet 把"是否仍在期望序列"降到 O(1)。
        var desiredSet = new HashSet<BoardItem>(desired, ReferenceEqualityComparer.Instance);
        for (int i = Items.Count - 1; i >= 0; i--)
            if (!desiredSet.Contains(Items[i]))
                Items.RemoveAt(i);
        for (int target = 0; target < desired.Count; target++)
        {
            var want = desired[target];
            int current = Items.IndexOf(want);
            if (current < 0)
                Items.Insert(target, want);
            else if (current != target)
                Items.Move(current, target);
        }
    }

    int _generation;

    /// <summary>
    /// 首次大批量加载（&gt;300 项）：清空后分批插入，每批让出 UI 线程（Background 优先级），
    /// 避免一次全量生成容器卡死 UI（步骤文件 1.1 / 技术指引，取舍=分批加载而非虚拟化）。
    /// 期间若有新对账/重扫（代次变化），旧批次作废不再插入。
    /// </summary>
    public int LastBatchTarget { get; private set; }

    public void ReconcileBatched(int batchSize, Action<int>? onBatch = null)
    {
        var desired = ComputeDesired();
        LastBatchTarget = desired.Count;
        int generation = ++_generation;
        Items.Clear();
        for (int start = 0; start < desired.Count; start += batchSize)
        {
            int s = start;
            Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                if (generation != _generation) return; // 期间已被 ReplaceSource/Reconcile 取代
                foreach (var item in desired.Skip(s).Take(batchSize))
                    Items.Add(item);
                onBatch?.Invoke(Items.Count);
            }, DispatcherPriority.Background);
        }
    }

    List<BoardItem> ComputeDesired()
    {
        return _source
            .Where(Pass)
            .OrderBy(item => item, Comparer)
            .ToList();
    }

    bool Pass(BoardItem item) => (ViewFilter?.Invoke(item) ?? true) && !_removed.Contains(item.Name) && item.Type switch
    {
        BoardItemType.Apps => _apps,
        BoardItemType.Files => _files,
        BoardItemType.Folders => _folders,
        _ => true,
    };

    IComparer<BoardItem>? _comparer;
    IComparer<BoardItem> Comparer => _comparer ??= new SortComparer(_key, _asc);

    sealed class SortComparer : IComparer<BoardItem>
    {
        // 文化敏感比较的 CompareInfo 缓存（StringComparer 内部已缓存）：避免每对比较重建
        static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;
        readonly SortKey _key;
        readonly bool _asc;
        public SortComparer(SortKey key, bool asc) { _key = key; _asc = asc; }

        public int Compare(BoardItem? a, BoardItem? b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            int result = _key switch
            {
                SortKey.Size => Nullable.Compare(a.SizeBytes, b.SizeBytes),
                SortKey.Type => ((int)a.Type).CompareTo((int)b.Type),
                SortKey.Mtime => a.Mtime.CompareTo(b.Mtime),
                _ => NameComparer.Compare(a.Name, b.Name),
            };
            return _asc ? result : -result;
        }
    }
}
