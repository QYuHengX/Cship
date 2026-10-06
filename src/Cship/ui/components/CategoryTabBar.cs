using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Cship.Core;
using Cship.Models;
using Cship.Ui.Animations;

namespace Cship.Ui.Components;

/// <summary>
/// 收纳板顶部横向分类按钮行（步骤 04 · 2026-09-25 改版，原文 L18-23）：
/// 默认 全部/软件/文件夹/文件（"全部"=主板内容，常驻不持久化）；按钮宽度随文字自适应；
/// 溢出折叠为行尾"更多 ▾"（点击向下展开下拉显示被折叠项）；右键=移除/重命名（"全部"置灰；
/// 内置项移除=同步写 catShow* 子开关、可经设置找回；重命名=仅改显示名）；
/// 拖动图标悬停按钮高亮、松开=加入该分类（复制语义，仅成员登记）。
/// 分类视图=主网格过滤（BoardModel.ViewFilter），本控件只负责按钮行与成员数据（state.categories）。
/// </summary>
public sealed class CategoryTabBar : Grid
{
    const double TabGap = 6;        // 按钮间距
    const double RowHeight = 24;    // 按钮行高（XAML 同步）
    const double DropdownGap = 4;   // "更多"下拉与按钮的间距

    readonly StackPanel _tabsHost = new() { Orientation = Orientation.Horizontal };
    readonly Border _sepBar;   // "全部"与"+"之间的灰白细竖杠（批次十二，原文任务11）
    readonly Border _plusBtn;  // "全部"右侧的"+"新建分类按钮（批次十二，原文任务11）
    readonly TextBlock _plusLabel;
    readonly Border _moreBtn;
    readonly Path _moreTriangle;               // "更多"磨圆三角形（2026-10-05 需求④，取代"更多 ▾"文字）
    readonly ScaleTransform _moreScale = new(1, 1);  // 出现时的"展开"动画载体（自左缘生长）
    readonly RotateTransform _moreSpin = new(0);     // 展开下拉时三角形翻转 180°
    readonly List<CategoryTabButton> _tabs = new();
    readonly List<CategoryTabButton> _folded = new();
    readonly CategoryModel _all = new() { Id = "all", Name = "", Builtin = true, Type = "all" };
    readonly List<CategoryModel> _cats = new(); // 内置三项+自定义（"全部"不持久化，单独字段）

    BoardModel? _model;
    FrameworkElement? _boardRoot;
    CategoryModel _current;
    bool _dark;
    bool _disabled;

    static readonly string[] BuiltinOrder = { "apps", "folders", "files" }; // 原文 L18 顺序：软件、文件夹、文件

    /// <summary>选中分类变化（含"全部"）；宿主据此切换主网格过滤视图。</summary>
    public event Action<CategoryModel>? SelectionChanged;
    /// <summary>成员/来源变化（加入、移除、文件消失），当前视图若受影响需刷新过滤。</summary>
    public event Action? MembersChanged;

    public CategoryTabBar()
    {
        _current = _all;
        Children.Add(_tabsHost);
        // 灰白细竖杠：长度略长于分类项按钮（24）→ 30
        _sepBar = new Border
        {
            Width = 1,
            Height = 30,
            Margin = new Thickness(TabGap, 0, TabGap, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // "+"按钮：透明灰白圆角小底，点击立即新增分类并进入重命名
        _plusLabel = new TextBlock { Text = "+", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        _plusBtn = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, TabGap, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = _plusLabel,
        };
        _plusBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; AddNewCategory(); };
        _plusBtn.MouseEnter += (_, _) => _plusBtn.Background = PlusBrush(_dark, true);
        _plusBtn.MouseLeave += (_, _) => _plusBtn.Background = PlusBrush(_dark, false);
        // "更多"按钮（2026-10-05 需求④）：磨圆三角形（Fill 与 Stroke 同色 + 圆角连接/端帽 = 顶点磨圆），
        // 不再是"更多 ▾"文字；挂在 _tabsHost **末位** → 天然紧跟"最后一个可见分类项"
        // （被折叠的按钮 Collapsed 不占位），不再固定贴行尾；末位分类项自带 6px 右 Margin 供间距。
        _moreTriangle = new Path
        {
            Data = Geometry.Parse("M1.6,1.4 L9.4,1.4 L5.5,6.6 Z"),
            StrokeThickness = 2.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = _moreSpin,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        _moreBtn = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, TabGap, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = _moreTriangle,
            Visibility = Visibility.Collapsed,
            RenderTransform = _moreScale,
            RenderTransformOrigin = new Point(0, 0.5), // 自左缘生长（观感=从末位分类项旁"长出来"）
        };
        _moreBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleMoreMenu(); };
        SizeChanged += (_, _) => RelayoutTabs();
        Loaded += (_, _) => RelayoutTabs();
        I18n.LanguageChanged += RefreshTexts;
    }

    /// <summary>宿主窗口关闭时解绑静态事件：I18n.LanguageChanged 是静态事件源，
    /// 订阅后不退订会把本组件（含全部按钮与板根引用）连同宿主窗口钉在进程里无法回收。
    /// 由 BoardWindow.Closed 调用（组件自身没有晚于窗口的关闭时机可挂）。</summary>
    public void Detach() => I18n.LanguageChanged -= RefreshTexts;

    /// <summary>宿主装配：数据源与坐标参照（拖入热区命中测试用）。</summary>
    public void Initialize(BoardModel model, FrameworkElement boardRoot)
    {
        _model = model;
        _boardRoot = boardRoot;
        LoadCategories();
        RebuildTabs();
        RefreshTexts(); // "更多"按钮文字与各按钮标签（初建时也必须跑：否则更多按钮无文字不可见）
        // 默认选中"全部"（主板内容），不触发事件（宿主初始即未过滤态）
        _current = _all;
        foreach (var t in _tabs)
            t.SetSelected(ReferenceEquals(t.Cat, _all), ThemeResolver.IsDark());
    }

    // ---- 数据（state.categories；结构见 00 §8 2026-09-25 改版）----

    void LoadCategories()
    {
        _cats.Clear();
        _cats.AddRange(StateStore.Instance.GetCategories());
        // 内置三项缺失则补种（首启/旧数据），按原文顺序 apps、folders、files
        bool dirty = false;
        for (int i = 0; i < BuiltinOrder.Length; i++)
        {
            string id = BuiltinOrder[i];
            if (_cats.Any(c => c.Builtin && c.Id == id)) continue;
            var cat = CategoryModel.CreateBuiltin(id);
            cat.Name = ""; // 内置显示名走 I18n；Name 仅存重命名覆盖
            _cats.Insert(Math.Min(i, _cats.Count(c => c.Builtin)), cat);
            dirty = true;
        }
        if (dirty)
            SaveCategories();
    }

    void SaveCategories()
    {
        try
        {
            StateStore.Instance.SetCategories(_cats);
        }
        catch (Exception ex)
        {
            Logger.Warn($"保存 categories 失败：{ex.Message}");
        }
    }

    /// <summary>板扫描/刷新后由宿主调：文件从桌面消失时自动从所有分类清除（含内置的显式成员）。</summary>
    public void OnSourceChanged()
    {
        if (_model == null || _cats.Count == 0) return;
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in _model.All)
            live.Add(it.Name);
        bool dirty = false;
        bool currentAffected = false;
        foreach (var cat in _cats)
        {
            int before = cat.Items.Count;
            cat.Items.RemoveAll(n => !live.Contains(n));
            if (cat.Items.Count != before)
            {
                dirty = true;
                if (ReferenceEquals(cat, _current))
                    currentAffected = true;
            }
        }
        if (dirty)
            SaveCategories();
        if (currentAffected)
            MembersChanged?.Invoke();
    }

    /// <summary>拖动图标到分类按钮上松开=加入该分类（复制语义：仅成员登记，同一文件可属多分类）。
    /// 已移除项由宿主先行复活（清单三修订B：拖入可复活）。</summary>
    public void AddToCategory(CategoryModel cat, BoardItem item)
    {
        if (cat.Id == "all") return; // "全部"即主板内容，无成员概念
        if (!cat.Items.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
        {
            cat.Items.Add(item.Name);
            SaveCategories();
            Logger.Info($"分类加入：{item.Name} → {DisplayName(cat)}");
        }
        TabFor(cat)?.Pulse(_dark); // 落定反馈：按钮高亮闪一下
        if (ReferenceEquals(cat, _current))
            MembersChanged?.Invoke();
    }

    /// <summary>板坐标系下命中的分类按钮热区（drop 目标；"全部"不接收拖入=正常网格落点）。</summary>
    public CategoryModel? CategoryAt(Point posInBoard)
    {
        if (_boardRoot == null) return null;
        foreach (var t in _tabs)
        {
            if (t.Visibility != Visibility.Visible || t.Cat.Id == "all") continue;
            var tl = t.TranslatePoint(new Point(0, 0), _boardRoot);
            if (double.IsNaN(tl.X) || double.IsNaN(tl.Y)) continue;
            if (new Rect(tl, new Size(t.ActualWidth, t.ActualHeight)).Contains(posInBoard))
                return t.Cat;
        }
        return null;
    }

    /// <summary>当前分类含该显式成员时返回"从此分类移除"菜单项（宿主追加进图标右键菜单）。</summary>
    public PopupMenuItem? RemoveActionFor(BoardItem item)
    {
        if (_current is not { Id: not "all" } cat) return null;
        if (!cat.Items.Contains(item.Name, StringComparer.OrdinalIgnoreCase)) return null;
        return new PopupMenuItem
        {
            Header = I18n.Tr("cat.remove"),
            OnClick = () => RemoveMember(cat, item.Name),
        };
    }

    /// <summary>从指定分类移出成员（公开：宿主拖拽移出语义用，清单三·任务14）。</summary>
    public void RemoveMember(CategoryModel cat, string name)
    {
        int n = cat.Items.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        if (n == 0) return;
        SaveCategories();
        Logger.Info($"从此分类移除：{name}（{DisplayName(cat)}）");
        if (ReferenceEquals(cat, _current))
            MembersChanged?.Invoke();
    }

    // ---- 顶栏按钮 ----

    CategoryTabButton? TabFor(CategoryModel cat) => _tabs.FirstOrDefault(t => ReferenceEquals(t.Cat, cat));

    string DisplayName(CategoryModel cat)
        => cat.Id == "all" ? I18n.Tr("cat.all")
           : cat.Builtin && (cat.Name.Length == 0 || cat.Name == cat.Id) ? I18n.Tr("cat." + cat.Id)
           : cat.Name;

    bool TabVisible(CategoryModel cat) => cat.Id == "all" || !cat.Builtin || cat.Id switch
    {
        "apps" => SettingsStore.Instance.GetBool("advanced.catShowApps", true),
        "files" => SettingsStore.Instance.GetBool("advanced.catShowFiles", true),
        _ => SettingsStore.Instance.GetBool("advanced.catShowFolders", true),
    };

    void RebuildTabs()
    {
        foreach (var t in _tabs)
            t.Detach();
        _tabs.Clear();
        _tabsHost.Children.Clear();
        _folded.Clear();
        AddTab(_all);
        // "全部"与首个分类按钮之间：竖杠 + "+"（批次十二，原文任务11）；两者不参与折叠/拖入热区
        _tabsHost.Children.Add(_sepBar);
        _tabsHost.Children.Add(_plusBtn);
        // 内置三项按原文顺序（apps、folders、files）渲染，不受 state 持久化顺序影响；自定义缀后
        foreach (var id in BuiltinOrder)
        {
            var cat = _cats.FirstOrDefault(c => c.Builtin && c.Id == id);
            if (cat != null) AddTab(cat);
        }
        foreach (var cat in _cats.Where(c => !c.Builtin))
            AddTab(cat);
        // "更多"恒为末位（Children.Clear 会把它一起清掉，故每次重建都要补挂回末位）
        _tabsHost.Children.Add(_moreBtn);
        ApplyVisibilityAll();
        foreach (var t in _tabs)
            t.SetSelected(ReferenceEquals(t.Cat, _current), _dark);
    }

    void AddTab(CategoryModel cat)
    {
        var tab = new CategoryTabButton(cat);
        tab.MouseLeftButtonUp += (_, e) => { e.Handled = true; Select(tab.Cat); };
        tab.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowTabMenu(tab); };
        tab.RefreshText(DisplayName); // 按钮文字（省略号+hover 全名气泡在 RefreshText 内配套）
        _tabsHost.Children.Add(tab);
        _tabs.Add(tab);
    }

    void RefreshTexts()
    {
        ToolTipService.SetToolTip(_moreBtn, I18n.Tr("cat.more")); // 三角形不写字：全名走悬停气泡
        foreach (var t in _tabs)
            t.RefreshText(DisplayName);
        RelayoutTabs(); // 按钮宽度随文字自适应，语言切换后重算折叠
    }

    void ApplyVisibilityAll()
    {
        foreach (var t in _tabs)
            t.Visibility = TabVisible(t.Cat) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>catShow* 子开关变化（含内置项"移除"找回）：刷新可见性；当前项被隐藏切回"全部"。</summary>
    public void RefreshTabs()
    {
        ApplyVisibilityAll();
        if (!TabVisible(_current))
            Select(_all);
        RelayoutTabs();
    }

    /// <summary>切回"全部"（主板内容）——禁用分类页/当前分类被隐藏时由宿主调。</summary>
    public void SelectAll() => Select(_all);

    /// <summary>从 state 重新加载分类数据并重建按钮行（步骤 05 配置导入热应用用）；
    /// 当前选中项若仍存在则保持，否则回"全部"。</summary>
    public void ReloadCategories()
    {
        string currentId = _current.Id;
        LoadCategories();
        RebuildTabs();
        RefreshTexts();
        CategoryModel? keep = currentId == _all.Id
            ? _all
            : _cats.FirstOrDefault(c => c.Id == currentId) ?? _all;
        Select(keep);
        RelayoutTabs();
    }

    // ---- 编辑状态查询与外部退出（清单三·任务8）----

    /// <summary>是否有按钮正在内联重命名。</summary>
    public bool IsEditing => _tabs.Any(t => t.Editing);

    /// <summary>板坐标系下命中点是否落在**正在编辑**的按钮内（退出编辑判定用）。</summary>
    public bool IsEditHit(Point posInBoard)
    {
        if (_boardRoot == null) return false;
        foreach (var t in _tabs)
        {
            if (!t.Editing) continue;
            var tl = t.TranslatePoint(new Point(0, 0), _boardRoot);
            if (double.IsNaN(tl.X) || double.IsNaN(tl.Y)) return false;
            return new Rect(tl, new Size(t.ActualWidth, t.ActualHeight)).Contains(posInBoard);
        }
        return false;
    }

    /// <summary>退出编辑（点编辑按钮外任意处）：走提交路径（与失焦确认同口径）。</summary>
    public void EndEditing()
    {
        foreach (var t in _tabs)
            if (t.Editing)
                t.EndEdit(true);
    }

    public void Select(CategoryModel cat)
    {
        if (ReferenceEquals(cat, _current))
            return;
        _current = cat;
        foreach (var t in _tabs)
            t.SetSelected(ReferenceEquals(t.Cat, cat), _dark);
        RelayoutTabs(); // 当前项可能在"更多"里 → 更新其选中提示
        SelectionChanged?.Invoke(cat);
    }

    // ---- 溢出折叠（原文 L20）----

    /// <summary>
    /// 折叠重算：按钮总宽超出可用宽度时，超出部分折叠进行尾"更多"按钮；
    /// 板 resize/分类增删/重命名/换语言后实时重算。用实测宽度（Measure），勿估算字符宽。
    /// </summary>
    public void RelayoutTabs()
    {
        if (ActualWidth <= 0) return;
        int prevFolded = _folded.Count; // 日志降噪用：仅在折叠集合规模变化时记录（resize 每帧都会跑到这里）
        // 每次重算都必须从 TabVisible 全量恢复可见性再折叠——否则上一次折叠留下的
        // Collapsed 按钮被排除统计，"总宽没超"误入无溢出分支把"更多"藏掉（实测踩坑）
        var visible = new List<CategoryTabButton>(_tabs.Count);
        foreach (var t in _tabs)
        {
            bool show = TabVisible(t.Cat);
            t.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) visible.Add(t);
        }
        foreach (var t in visible)
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _plusBtn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // 竖杠 + "+" 的固定占宽（恒显示，不折叠）
        double fixedW = _sepBar.Margin.Left + _sepBar.Width + _sepBar.Margin.Right
                      + _plusBtn.DesiredSize.Width + _plusBtn.Margin.Left + _plusBtn.Margin.Right;
        double moreW = MeasureMoreWidth();
        double sum = fixedW;
        for (int i = 0; i < visible.Count; i++)
            sum += visible[i].DesiredSize.Width + (i < visible.Count - 1 ? TabGap : 0);
        if (sum <= ActualWidth)
        {
            // 无溢出：全部显示，收起"更多"
            _folded.Clear();
            _moreBtn.Visibility = Visibility.Collapsed;
            if (prevFolded != 0)
                Logger.Info($"分类顶栏折叠解除：全部 {visible.Count} 项可见（条宽={ActualWidth:F0}）");
            return;
        }

        bool moreWasVisible = _moreBtn.Visibility == Visibility.Visible;
        _moreBtn.Visibility = Visibility.Visible;
        double avail = ActualWidth - moreW - fixedW;
        double acc = 0;
        _folded.Clear();
        foreach (var t in visible)
        {
            double w = t.DesiredSize.Width;
            if (acc > 0 && acc + w > avail) // 首个按钮恒显示（保底可点）
            {
                _folded.Add(t);
                t.Visibility = Visibility.Collapsed;
            }
            else
            {
                acc += w + TabGap;
            }
        }
        UpdateMoreSelected();
        PlayMoreAppear(moreWasVisible);
        if (_folded.Count != prevFolded)
            Logger.Info($"分类顶栏折叠：可见{visible.Count - _folded.Count} 折叠{_folded.Count} 更多={_moreBtn.Visibility}"
                + $" 条宽={ActualWidth:F0} moreW={moreW:F0}");
    }

    /// <summary>"更多"出现时的展开动画（2026-10-05 需求④）：自左缘 0.4→1 生长 + 淡入（EaseOutBack 微过冲）。
    /// 只在"隐藏→可见"那一次播放——RelayoutTabs 在每次 resize/重命名都会跑，每次都重播会闪。</summary>
    void PlayMoreAppear(bool moreWasVisible)
    {
        if (moreWasVisible) return;
        _moreScale.ScaleX = _moreScale.ScaleY = 0.4;
        _moreBtn.Opacity = 0;
        Anim.Run(_moreScale, ScaleTransform.ScaleXProperty, 0.4, 1, AnimTuning.CategoryRowCollapse, EaseStyle.EaseOutBack);
        Anim.Run(_moreScale, ScaleTransform.ScaleYProperty, 0.4, 1, AnimTuning.CategoryRowCollapse, EaseStyle.EaseOutBack);
        Anim.Run(_moreBtn, OpacityProperty, 0, 1, AnimTuning.Micro, EaseStyle.EaseOutCubic);
    }

    /// <summary>"更多"按钮占宽（固定值，2026-10-05 需求④）：左右内距 10 + 三角形（几何 7.8×5.2 ＋
    /// 描边 2.6 ≈ 10.4 宽）+ 右 Margin 6 ≈ 26.4，取 27 留一点余量。三角形不随语言变宽，故不再走
    /// FormattedText 实测（旧实现按"更多 ▾"文字宽度算）。</summary>
    double MeasureMoreWidth() => 27;

    /// <summary>当前选中项被折叠时，"更多"按钮给选中态提示（04 §2.3）。</summary>
    void UpdateMoreSelected()
    {
        bool foldedSelected = _folded.Any(t => ReferenceEquals(t.Cat, _current));
        StyleMore(foldedSelected);
    }

    void StyleMore(bool selected)
    {
        // 选中提示跟随新选中色（透明深黑，修订A·4.2）；三角形 Fill/Stroke 取同一支画刷（磨圆实心）
        Color TextColor(bool dark) => dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1B);
        _moreBtn.Background = selected
            ? new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x00, 0x00))
            : Brushes.Transparent;
        var tri = selected ? new SolidColorBrush(Colors.White) : new SolidColorBrush(TextColor(_dark));
        _moreTriangle.Fill = tri;
        _moreTriangle.Stroke = tri;
    }

    /// <summary>"更多"点击（2026-10-05 修订）：**真开合切换**——已展开、或本次按下已被"点板内其他区域"
    /// 收回时，本次点击一律算收回，绝不再弹一张（旧实现：点外收起发生在**按下**、展开发生在**抬起**，
    /// 同一次点击被拆成"先收后弹"＝用户报的"展开卡收回后会二次展开"）。
    /// 三角形角度由 Show 的 onClosed 单点维护：收口动画结束时转回朝下。</summary>
    void ToggleMoreMenu()
    {
        if (GlassPopup.IsOpenFor(_moreBtn) || GlassPopup.JustClosedByOutsideClick(_moreBtn))
        {
            GlassPopup.CloseAll(); // 已展开 → 收回（角度回落交给 onClosed）
            return;
        }
        if (_folded.Count == 0) return;
        var items = new List<PopupMenuItem>();
        foreach (var t in _folded)
        {
            var cat = t.Cat;
            items.Add(new PopupMenuItem
            {
                Header = DisplayName(cat),
                Checked = ReferenceEquals(cat, _current),
                OnClick = () => Select(cat),
            });
        }
        int gen = ++_moreMenuGen;
        Anim.Run(_moreSpin, RotateTransform.AngleProperty, null, 180, AnimTuning.Micro, EaseStyle.EaseOutCubic);
        GlassPopup.Show(_moreBtn, new Point(0, _moreBtn.ActualHeight + DropdownGap), items, onClosed: () =>
        {
            if (gen != _moreMenuGen) return; // 旧卡的关闭回调迟到（新一轮已接管）：不要转回
            Anim.Run(_moreSpin, RotateTransform.AngleProperty, null, 0, AnimTuning.Micro, EaseStyle.EaseOutCubic);
        });
    }

    /// <summary>展开代次：区分"上一张卡的关闭回调"与"本张卡"，见 ToggleMoreMenu。</summary>
    int _moreMenuGen;

    // ---- 右键菜单（原文 L21：移除、重命名）----

    void ShowTabMenu(CategoryTabButton tab)
    {
        // "全部"右键=完全无反应（清单三·任务8：连置灰菜单与提示都不弹；i18n 键 cat.all.locked 保留）
        if (tab.Cat.Id == "all") return;
        var items = new List<PopupMenuItem>();
        {
            items.Add(new PopupMenuItem { Header = I18n.Tr("cat.tab.remove"), OnClick = () => RemoveTab(tab) });
            items.Add(new PopupMenuItem { Header = I18n.Tr("menu.rename"), OnClick = () => BeginTabRename(tab) });
        }
        GlassPopup.Show(tab, new Point(0, tab.ActualHeight + DropdownGap), items);
    }

    void RemoveTab(CategoryTabButton tab)
    {
        if (tab.Cat.Id == "all") return;
        if (tab.Cat.Builtin)
        {
            // 内置项"移除"=隐藏（补充决策：同步写设置子开关，重开即找回，数据不删）
            string key = tab.Cat.Id switch
            {
                "apps" => "advanced.catShowApps",
                "files" => "advanced.catShowFiles",
                _ => "advanced.catShowFolders",
            };
            SettingsStore.Instance.SetBool(key, false); // SettingsChanged → RefreshTabs
            Logger.Info($"移除内置分类：{DisplayName(tab.Cat)}（catShow 关闭，可经设置找回）");
            return;
        }
        // 自定义分类：真删除（缩小淡出后移除；若为当前项自动切"全部"）
        var cat = tab.Cat;
        tab.IsHitTestVisible = false;
        var scale = new ScaleTransform(1, 1);
        tab.RenderTransformOrigin = new Point(0.5, 0.5);
        tab.RenderTransform = scale;
        Anim.Run(tab, OpacityProperty, null, 0, AnimTuning.CategoryItemDelete, EaseStyle.EaseInOutQuad);
        Anim.Run(scale, ScaleTransform.ScaleXProperty, null, 0, AnimTuning.CategoryItemDelete, EaseStyle.EaseInOutQuad);
        Anim.Run(scale, ScaleTransform.ScaleYProperty, null, 0, AnimTuning.CategoryItemDelete, EaseStyle.EaseInOutQuad,
            onDone: () =>
            {
                _tabsHost.Children.Remove(tab);
                tab.Detach();
                _tabs.Remove(tab);
                _folded.Remove(tab);
                _cats.Remove(cat);
                SaveCategories();
                if (ReferenceEquals(_current, cat))
                    Select(_all);
                RelayoutTabs();
                Logger.Info($"删除自定义分类：{cat.Name}");
            });
    }

    void BeginTabRename(CategoryTabButton tab)
    {
        if (_tabs.Any(t => t.Editing)) return;
        tab.BeginEdit(DisplayName(tab.Cat), ok =>
        {
            if (!ok) return;
            var newName = tab.EditText.Trim();
            if (newName.Length == 0 || newName == DisplayName(tab.Cat)) return;
            tab.Cat.Name = newName; // 内置项=重命名覆盖（类型过滤语义不变）；自定义=直接改
            tab.RefreshText(DisplayName);
            SaveCategories();
            RelayoutTabs();
            Logger.Info($"分类重命名 → {newName}");
        });
    }

    // ---- 新增分类（批次十二，原文任务11）----

    /// <summary>"+"点击：立即新增一个分类项并进入其重命名状态（命名冲突自动追加序号）。</summary>
    void AddNewCategory()
    {
        if (_tabs.Any(t => t.Editing)) return;
        var taken = new HashSet<string>(_cats.Select(DisplayName), StringComparer.OrdinalIgnoreCase);
        string baseName = I18n.Tr("cat.newName");
        string name = baseName;
        int n = 2;
        while (!taken.Add(name))
            name = $"{baseName}{n++}";
        var cat = CategoryModel.CreateCustom(name);
        _cats.Add(cat);
        SaveCategories();
        RebuildTabs();
        RelayoutTabs();
        Logger.Info($"新增分类：{name}");
        if (TabFor(cat) is { } newTab)
        {
            // 出现过渡（修订A·4.2）：淡入 + 自 0.85 微放大（00 §10 过渡铁律）
            newTab.Opacity = 0;
            var appear = new ScaleTransform(0.85, 0.85);
            newTab.RenderTransformOrigin = new Point(0.5, 0.5);
            newTab.RenderTransform = appear;
            Anim.Run(newTab, OpacityProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseOutCubic);
            Anim.Run(appear, ScaleTransform.ScaleXProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseOutCubic);
            Anim.Run(appear, ScaleTransform.ScaleYProperty, null, 1, AnimTuning.Micro, EaseStyle.EaseOutCubic);
            BeginTabRename(newTab); // 立即进入重命名
        }
    }

    // ---- 拖入热区高亮（原文 L22）----

    /// <summary>内部拖动悬停时由宿主持续调用：命中分类按钮高亮（null=清除）。</summary>
    public void SetDragHover(Point? posInBoard)
    {
        CategoryTabButton? hit = null;
        if (posInBoard is { } p && _boardRoot != null)
        {
            foreach (var t in _tabs)
            {
                if (t.Visibility != Visibility.Visible || t.Cat.Id == "all") continue;
                var tl = t.TranslatePoint(new Point(0, 0), _boardRoot);
                if (double.IsNaN(tl.X)) continue;
                if (new Rect(tl, new Size(t.ActualWidth, t.ActualHeight)).Contains(p))
                {
                    hit = t;
                    break;
                }
            }
        }
        foreach (var t in _tabs)
            t.SetDragHover(ReferenceEquals(t, hit), _dark);
    }

    // ---- 禁用（advanced.categoryDisabled，原文 L49）----

    /// <summary>整行隐藏（含"全部"与"更多"）；切换带高度/透明度过渡（00 §10 过渡铁律）。
    /// 宿主负责隐藏前先 Select("全部")。</summary>
    public void SetDisabled(bool disabled)
    {
        if (_disabled == disabled) return;
        _disabled = disabled;
        if (disabled)
        {
            Anim.Run(this, Grid.HeightProperty, ActualHeight, 0, AnimTuning.CategoryRowCollapse, EaseStyle.EaseInOutQuad,
                onDone: () => { if (_disabled) Visibility = Visibility.Collapsed; });
            Anim.Run(this, OpacityProperty, null, 0, AnimTuning.CategoryRowCollapse, EaseStyle.EaseInOutQuad, resetOnDone: false);
        }
        else
        {
            Visibility = Visibility.Visible;
            Anim.Run(this, Grid.HeightProperty, 0, RowHeight, AnimTuning.CategoryRowCollapse, EaseStyle.EaseOutCubic);
            Anim.Run(this, OpacityProperty, null, 1, AnimTuning.CategoryRowCollapse, EaseStyle.EaseInOutQuad, resetOnDone: false);
        }
    }

    // ---- 主题（00 §9；06 步 MaterialEngine 接管时同步）----

    public void ApplyTheme(bool dark)
    {
        _dark = dark;
        foreach (var t in _tabs)
            t.SetSelected(ReferenceEquals(t.Cat, _current), dark);
        StyleMore(_folded.Any(t => ReferenceEquals(t.Cat, _current)));
        // 竖杠与"+"（批次十二，原文任务11）：灰白，跟随主题
        _sepBar.Background = new SolidColorBrush(dark
            ? Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x66, 0x9E, 0x9E, 0x9E));
        _plusBtn.Background = PlusBrush(dark, false);
        _plusLabel.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1B));
    }

    /// <summary>"+"按钮底色：透明灰白圆角（hover 加深一档）。</summary>
    static Brush PlusBrush(bool dark, bool hover)
    {
        byte alpha = hover ? (byte)0x59 : (byte)0x2E;
        return new SolidColorBrush(dark
            ? Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(alpha, 0x9E, 0x9E, 0x9E));
    }
}

/// <summary>顶栏分类按钮：胶囊 Border + 省略号 Label + 拖入高亮层 + 内联重命名 TextBox。</summary>
sealed class CategoryTabButton : Border
{
    public readonly CategoryModel Cat;
    public readonly TextBlock Label;
    public readonly TextBox EditBox;
    readonly TextBlock _editMirror;              // 编辑期可见文本（随 TextChanged 同步，任务8）
    readonly Rectangle _caret;                   // 文字右侧闪烁细竖杠（任务8）
    readonly Border _highlight;
    Action<bool>? _editDone;
    Action<bool>? _endEdit;                      // 编辑收尾委托（EndEditing 公开退出用）
    bool _selected;
    bool _dragHover;
    bool _dark;

    const double CaretHeight = 14;               // 细竖杠高（1px 宽，跟在可见文本之后）

    public CategoryTabButton(CategoryModel cat)
    {
        Cat = cat;
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(12, 3, 12, 3);
        Margin = new Thickness(0, 0, 6, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = Cursors.Hand;
        MaxWidth = 140;
        MinHeight = 24;
        var grid = new Grid();
        _highlight = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Label = new TextBlock
        {
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 编辑期 EditBox 本体全透明仅作输入捕获（任务8）：选区高亮/插入符/文字都不可见，
        // 可见文本与光标由覆盖层（镜像 TextBlock + 闪烁竖杠）呈现
        EditBox = new TextBox
        {
            FontSize = 12,
            Visibility = Visibility.Collapsed,
            MinWidth = 72,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            CaretBrush = Brushes.Transparent,
            SelectionBrush = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _editMirror = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _caret = new Rectangle
        {
            Width = 1,
            Height = CaretHeight,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        var editRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        editRow.Children.Add(_editMirror);
        editRow.Children.Add(_caret);
        grid.Children.Add(_highlight);
        grid.Children.Add(Label);
        grid.Children.Add(EditBox);
        grid.Children.Add(editRow); // 覆盖在 EditBox 之上（命中测试关闭，输入直达 EditBox）
        Child = grid;
        MouseEnter += (_, _) =>
        {
            if (!_selected && !_dragHover)
            {
                AnimateBackground(HoverColor(_dark)); // 悬停：透明度过渡拉高一档（修订A·4.3）
                AnimateScale(HoverScale);             // + 微微放大过渡
            }
        };
        MouseLeave += (_, _) =>
        {
            if (!_selected && !_dragHover)
            {
                AnimateBackground(IdleColor(_dark));
                AnimateScale(1.0);
            }
        };
    }

    public string EditText => EditBox.Text;
    public bool Editing => EditBox.Visibility == Visibility.Visible;

    public void RefreshText(Func<CategoryModel, string> displayName)
    {
        Label.Text = displayName(Cat);
        ToolTipService.SetToolTip(this, Label.Text); // hover 全名气泡（省略号配套）
    }

    public void SetSelected(bool selected, bool dark)
    {
        _selected = selected;
        _dark = dark;
        // 选中=透明深黑（修订A·4.2，替代原蓝色实底）、未选中=透明灰；颜色变化走过渡动画
        AnimateBackground(selected ? SelectedColor(dark) : _dragHover ? DragColor(dark) : IdleColor(dark));
        // 选中文字白色、未选中主题文字色，同样过渡
        AnimateForeground(selected ? Colors.White : TextColor(dark));
    }

    static Color IdleColor(bool dark) => dark
        ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)
        : Color.FromArgb(0x14, 0x9E, 0x9E, 0x9E);

    /// <summary>悬停底色：同色相透明度拉高一档（修订A·4.3）。</summary>
    static Color HoverColor(bool dark) => dark
        ? Color.FromArgb(0x3C, 0xFF, 0xFF, 0xFF)
        : Color.FromArgb(0x2E, 0x9E, 0x9E, 0x9E);

    /// <summary>选中底色：透明深黑（修订A·4.2）。</summary>
    static Color SelectedColor(bool dark) => Color.FromArgb(0x59, 0x00, 0x00, 0x00);

    static Color DragColor(bool dark) => dark
        ? Color.FromArgb(0x3C, 0x4C, 0xC2, 0xFF)
        : Color.FromArgb(0x3C, 0x00, 0x78, 0xD4);

    static Color TextColor(bool dark) => dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1B);

    const double HoverScale = 1.05; // 悬停微放大幅度（修订A·4.3）

    /// <summary>底色过渡（修订A·4.2/4.3）：ColorAnimation 挂在非冻结画刷上；画刷未建立（首次）直接落定。</summary>
    void AnimateBackground(Color to)
    {
        if (Background is SolidColorBrush brush && !brush.IsFrozen)
        {
            var anim = new ColorAnimation(to, Anim.Ms(AnimTuning.CategorySwitch))
            {
                EasingFunction = AnimTuning.Map(EaseStyle.EaseInOutQuad),
            };
            Cship.Ui.Animations.AnimationClock.Apply(anim);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
        else
        {
            Background = new SolidColorBrush(to); // 非冻结实例，供后续过渡
        }
    }

    /// <summary>文字颜色过渡（修订A·4.2）：同底色口径。</summary>
    void AnimateForeground(Color to)
    {
        if (Label.Foreground is SolidColorBrush brush && !brush.IsFrozen)
        {
            var anim = new ColorAnimation(to, Anim.Ms(AnimTuning.CategorySwitch))
            {
                EasingFunction = AnimTuning.Map(EaseStyle.EaseInOutQuad),
            };
            Cship.Ui.Animations.AnimationClock.Apply(anim);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
        else
        {
            Label.Foreground = new SolidColorBrush(to);
        }
    }

    /// <summary>悬停微放大过渡（修订A·4.3）：RenderTransform 挂 ScaleTransform 缓动。</summary>
    void AnimateScale(double to)
    {
        if (RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = st;
        }
        Anim.Run(st, ScaleTransform.ScaleXProperty, null, to, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
        Anim.Run(st, ScaleTransform.ScaleYProperty, null, to, AnimTuning.Micro, EaseStyle.EaseInOutQuad);
    }

    static Brush DragBrush(bool dark) => new SolidColorBrush(DragColor(dark));

    /// <summary>拖动悬停高亮（原文 L22）。</summary>
    public void SetDragHover(bool hover, bool dark)
    {
        if (_dragHover == hover) return;
        _dragHover = hover;
        if (!_selected)
            AnimateBackground(hover ? DragColor(dark) : IdleColor(dark));
    }

    /// <summary>加入分类的落定反馈：高亮闪烁一下（04 §4.2）。</summary>
    public void Pulse(bool dark)
    {
        _highlight.Background = DragBrush(dark);
        _highlight.Opacity = 1;
        Anim.Run(_highlight, OpacityProperty, 1, 0, AnimTuning.CategorySwitch, EaseStyle.EaseInOutQuad,
            onDone: () =>
            {
                _highlight.Background = Brushes.Transparent;
                _highlight.Opacity = 1; // 复位不透明度，供下次闪烁
            });
    }

    /// <summary>
    /// 内联重命名（清单三·任务8 改造）：回车确认 / Esc 取消 / 失焦确认。
    /// 进入前锁定按钮 Width（防 TextBox MinWidth 把按钮撑长），结束清除还原自适应；
    /// 无全选高亮（SelectionBrush 全透明、不 SelectAll）；可见文本=镜像 TextBlock 随
    /// TextChanged 同步，文字右侧贴一条闪烁细竖杠（1↔0 AutoReverse 循环）。
    /// </summary>
    public void BeginEdit(string initial, Action<bool> done)
    {
        if (Editing)
        {
            done(false);
            return;
        }
        // 锁宽：ActualWidth 不足（尚未布局）则用 DesiredSize
        Width = !double.IsNaN(ActualWidth) && ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
        _caret.Fill = new SolidColorBrush(_dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1B));
        _editMirror.Foreground = Label.Foreground; // 镜像文本颜色与标签一致（选中白/主题色，修订A·4.1）
        Label.Visibility = Visibility.Collapsed;
        _editMirror.Text = initial;
        EditBox.Text = initial;
        EditBox.Visibility = Visibility.Visible;
        _caret.Visibility = Visibility.Visible;
        StartCaretBlink();
        _editDone = done;
        bool ended = false;

        void End(bool ok)
        {
            if (ended) return;
            ended = true;
            EditBox.KeyDown -= OnKey;
            EditBox.LostFocus -= OnLost;
            EditBox.TextChanged -= OnTextChanged;
            EditBox.Visibility = Visibility.Collapsed;
            StopCaretBlink();
            _caret.Visibility = Visibility.Collapsed;
            _editMirror.Text = ""; // 清空镜像残留（修订A·4.1：残留黑字叠在选中白字上=黑字白描边假象）
            Label.Visibility = Visibility.Visible;
            Width = double.NaN; // 还原自适应宽（锁宽清除）
            _editDone = null;
            _endEdit = null;
            done(ok);
        }
        _endEdit = End;

        void OnTextChanged(object s, TextChangedEventArgs e)
        {
            _editMirror.Text = EditBox.Text; // 可见文本与输入同步（EditBox 本体透明）
        }
        void OnKey(object s, KeyEventArgs e)
        {
            // 只拦截 Enter/Esc；其余键（含中文输入的 Unicode 字符）必须放行，
            // 否则 KeyDown 标记 Handled 会连带吞掉 TextInput（实测"游戏"打不进去）
            if (e.Key == Key.Enter) { e.Handled = true; End(true); }
            else if (e.Key == Key.Escape) { e.Handled = true; End(false); }
        }
        void OnLost(object s, RoutedEventArgs e) => End(true);

        EditBox.KeyDown += OnKey;
        EditBox.LostFocus += OnLost;
        EditBox.TextChanged += OnTextChanged;
        // 焦点延迟到玻璃菜单关闭收尾之后再落（菜单行点击的同步链路里 Popup 的 HWND
        // 还握着 Win32 焦点，立即 Focus() 会被关闭流程冲掉 → 打不了字，实测踩坑）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (EditBox.Visibility != Visibility.Visible) return; // 期间已被 Esc/失焦收尾
            EditBox.Focus();
            Keyboard.Focus(EditBox);
            EditBox.CaretIndex = EditBox.Text.Length; // 不全选（任务8），光标落尾部
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>编辑期间的闪烁细竖杠：1↔0 AutoReverse 无限循环（AnimTuning.CaretBlink 半程）。</summary>
    void StartCaretBlink()
    {
        var blink = new DoubleAnimation(1, 0, Anim.Ms(AnimTuning.CaretBlink))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _caret.BeginAnimation(OpacityProperty, blink);
    }

    void StopCaretBlink()
    {
        _caret.BeginAnimation(OpacityProperty, null);
        _caret.Opacity = 1;
    }

    /// <summary>按钮行重建/外部点按退出编辑（清单三·任务8）：true=提交（同失焦口径），false=取消。</summary>
    public void EndEdit(bool ok) => _endEdit?.Invoke(ok);

    /// <summary>按钮行重建时解除编辑回调引用。</summary>
    public void Detach() => _editDone = _endEdit = null;
}
