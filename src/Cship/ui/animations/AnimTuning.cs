using System;
using System.Windows.Media.Animation;

namespace Cship.Ui.Animations;

/// <summary>项目级缓动枚举（03 §2 映射表的"项目缓动名"）。WPF 实现唯一映射在 <see cref="AnimTuning.Map"/>。</summary>
public enum EaseStyle
{
    /// <summary>无 EasingFunction。</summary>
    Linear,
    /// <summary>QuadraticEase EaseInOut（微交互标准）。</summary>
    EaseInOutQuad,
    /// <summary>QuadraticEase EaseIn。</summary>
    EaseInQuad,
    /// <summary>QuadraticEase EaseOut。</summary>
    EaseOutQuad,
    /// <summary>CubicEase EaseOut（入场标准）。</summary>
    EaseOutCubic,
    /// <summary>CubicEase EaseIn（退场标准）。</summary>
    EaseInCubic,
    /// <summary>BackEase EaseOut：峰值过冲恰 1.08（03 §2"过冲 ≤1.08"；振幅换算见 EaseOutBack 注释）。</summary>
    EaseOutBack,
}

/// <summary>
/// 动画时长/缓动/关键帧参数的**唯一来源**（步骤 03）：全部 UI 动画只准引用本类，
/// 禁止散落魔法数字（验收：rg "Duration\(new TimeSpan|FromSeconds\(" src\ui 应 0 命中）。
/// 时长单位一律 double 毫秒（经 <see cref="Anim.Ms"/> 转 TimeSpan）。
/// </summary>
public static class AnimTuning
{
    // ===== 00 §7 动画时长表（步骤 03 固化为常量）=====

    /// <summary>微交互基准：气泡淡入、hover 小过渡、腾位高亮等。</summary>
    public const double Micro = 150;

    /// <summary>图标 hover 上跳。</summary>
    public const double HoverJump = 200;

    /// <summary>入场（收纳板弹出等）。</summary>
    public const double Enter = 350;

    /// <summary>退场（收纳板收起等）。</summary>
    public const double Exit = 250;

    /// <summary>主题切换交叉淡化（ThemeFade 快照淡出）。</summary>
    public const double ThemeFade = 300;

    /// <summary>菜单/气泡出现（scale 0.95→1 + 淡入）。</summary>
    public const double MenuPop = 120;

    /// <summary>菜单关闭淡出+微缩 0.96（00 §7 收纳板表·批次五口径）。</summary>
    public const double MenuClose = 130;

    // 注：00 §7 时长表原"悬浮窗消散 120ms"已被用户两次修订取代（STEP_LOG 步骤01 决策13：
    // 分离+淡出同 250ms，结束瞬间恰好全透明）——不设 DockVanish 常量，消散一律用 DockDissolve。

    // ===== 悬浮窗（步骤 01 修订版编排）=====

    /// <summary>悬停三板错位 6→10 的时长。</summary>
    public const double DockHover = 250;

    /// <summary>消散/复现/程序出入场的分离与淡出时长（同 duration 同起点，决策 13）。</summary>
    public const double DockDissolve = 250;

    // ===== 图标微动效（任务 4 集中化）=====

    /// <summary>hover 上跳位移（00 §7 图标表：6px）。</summary>
    public const double IconLiftPx = 6.0;

    /// <summary>hoverReveal 渐显（SetReveal）。</summary>
    public const double IconReveal = 250;

    /// <summary>删除/移除的消失动画（缩小+淡出）。</summary>
    public const double DeleteFade = 200;

    /// <summary>起拖原图标淡出 / 松手恢复淡入。</summary>
    public const double GhostFade = 150;

    /// <summary>无效位置回弹 / 拖影淡出消失。</summary>
    public const double GhostReturn = 200;

    /// <summary>齿轮 hover 慢转。</summary>
    public const double GearSpin = 400;

    /// <summary>关闭红点 hover 放大过渡（批次二：1.35×/160ms）。</summary>
    public const double ChromeHover = 160;

    // ===== 收纳板开合预设（任务 3 表）=====

    /// <summary>slideEdge 滑出（依锚边从屏幕边滑入）。</summary>
    public const double SlideEdgeOpen = 300;

    /// <summary>popBounce 两段缩放总时长（0→1.06→1）。</summary>
    public const double PopBounce = 400;

    /// <summary>popBounce 峰值过冲。</summary>
    public const double PopBouncePeak = 1.06;

    /// <summary>fadeSoft 上移淡入。</summary>
    public const double SoftFade = 300;

    /// <summary>fadeSoft 的上移距离（px）。</summary>
    public const double SoftFadeRise = 8.0;

    /// <summary>spitOut 第一段：在悬浮窗处压缩至 SpitScaleMin。</summary>
    public const double SpitPress = Micro;

    /// <summary>spitOut 第二段：向锚边方向弹开。</summary>
    public const double SpitPop = Enter;

    /// <summary>spitOut 压缩态缩放。</summary>
    public const double SpitScaleMin = 0.4;

    /// <summary>spitOut 吸回第一段（反向收缩）。</summary>
    public const double SpitSuck = Exit;

    /// <summary>spitOut 吸回末端：缩至 0.2 淡出的短尾。</summary>
    public const double SpitSuckTail = Micro;

    /// <summary>spitOut 吸回末端缩放。</summary>
    public const double SpitScaleEnd = 0.2;

    /// <summary>expand 锚定方向滑入淡入的起点不透明度（与 02 步已验收观感一致）。</summary>
    public const double FadeOpenFrom = 0.25;

    /// <summary>屏幕外滑入/滑出的出屏余量（px，保证完全出屏）。</summary>
    public const double SlideOutMarginPx = 12;

    // ===== 分类页（步骤 04 · 2026-09-25 改版：顶部按钮行方案）=====

    /// <summary>分类切换（点不同分类按钮）内容交叉淡化（04 §5.3）。</summary>
    public const double CategorySwitch = 150;

    /// <summary>分类按钮行禁用/启用的高度+透明度过渡（04 §6.1；00 §10 过渡铁律）。</summary>
    public const double CategoryRowCollapse = 200;

    /// <summary>自定义分类删除：缩小淡出（与图标 DeleteFade 同节奏）。</summary>
    public const double CategoryItemDelete = DeleteFade;

    // ===== 清单二（2026-09-25 · 用户 16 项任务拆批 2/3）=====

    /// <summary>调整尺寸期间整窗模糊的出现/消失过渡（任务10，替代旧四边渐变条）。</summary>
    public const double ResizeBlur = 180;

    /// <summary>首开加载点单次闪烁半程（0.12→1.0，AutoReverse 往返）（任务13；清单二修订 A：400→240 提速）。</summary>
    public const double LoadingPulse = 240;

    /// <summary>加载点列内相邻圆点的相位错开（BeginTime）（任务13；修订 A：140→80 提速）。</summary>
    public const double LoadingPulsePhase = 80;

    /// <summary>首载就绪后加载点"散开+淡出"的时长（任务13）。</summary>
    public const double LoadingScatter = 200;

    // ===== 清单三（2026-09-26 · 用户 16 项任务拆批 3/3）=====

    /// <summary>刷新重建的逐项果冻入场（任务12：Squash 0.7→1.08→1 + 淡入）。</summary>
    public const double JellyAppear = 260;

    /// <summary>刷新重建逐项渲染间隔（任务12；交互节奏值非动画时长，入表仅为集中管理）。</summary>
    public const double JellyRebuildStepMs = 20;

    /// <summary>分类重命名闪烁竖杠的半程（任务8：1↔0 AutoReverse 往返）。</summary>
    public const double CaretBlink = 500;

    // ===== 设置窗批次（2026-10-01 · 用户 10 项 bug 修复）=====

    /// <summary>设置窗出现（径向渐显：中心→四周透明度渐降）（2026-10-04 用户定档：400→750）。</summary>
    public const double SettingsWindowIn = 750;

    /// <summary>设置窗消失（径向收拢：四周→中心，开窗逆放）（2026-10-04 用户定档：750→300；
    /// 缓动 EaseOutQuad 保留——遮罩死区修复见决策164，否则 300ms 可见段只剩 ~120ms）。</summary>
    public const double SettingsWindowOut = 300;

    /// <summary>设置窗右列分页切换（淡出→换页→滑入）单段时长。</summary>
    public const double SettingsPageSwitch = 160;

    /// <summary>设置窗左列导航选中/悬停过渡。</summary>
    public const double SettingsNav = 150;

    /// <summary>开关滑块行程（2026-10-01 开关重绘；2026-10-03 去果冻放缓：缓动改 EaseInOutQuad、180→280）。</summary>
    public const double ToggleSlide = 280;

    /// <summary>折叠列表展开卡下滑出现（2026-10-01 改覆盖式展开）。</summary>
    public const double FoldingPop = 140;

    /// <summary>档位滑块（Step≥1）视觉滑块/填充在档位间的过渡（2026-10-04；连续拖动逐帧接续，
    /// 值回写不受动画影响）。</summary>
    public const double SliderStepSnap = 130;

    // ===== 打开动画（2026-10-05 清单05任务5：两点并存）=====
    // ① 图标短缩弹一下（PlayOpenPop）：点击即时反馈，1→谷值→1，单次 220ms；
    // ② 指示灯位三点循环（PlayOpening）：相位错开往复闪烁，直到该项 IsRunning 转亮
    //    （=程序/文件/文件夹打开完成）才向中心汇聚淡出、合并为常亮指示灯；打开失败走 AbortOpening。
    // 两者动画对象互不重叠（Squash 作用于图标宿主，三点作用于指示灯条），天然并行。

    /// <summary>打开弹一下的单次总时长（scale 1→谷值→1 关键帧）。</summary>
    public const double OpenPopMs = 220;

    /// <summary>打开弹一下向中心缩小的谷值（spec：~0.8）。</summary>
    public const double OpenPopScaleMin = 0.8;

    /// <summary>打开三点单次闪烁半程（0.25↔1.0，AutoReverse 往返）。</summary>
    public const double OpenDotsPulse = 320;

    /// <summary>打开三点相邻相位错开（BeginTime）。</summary>
    public const double OpenDotsPhase = 120;

    /// <summary>三点汇聚合并为指示灯的收尾时长。</summary>
    public const double DotsMerge = 280;

    /// <summary>收纳板拖出屏幕后松手的回弹时长（按回弹距离折算，上限）。</summary>
    public const double BoardSnapBack = 300;

    // ===== 帧插值 / 非动画节奏（集中管理，避免散落）=====

    /// <summary>滚轮平滑滚动：渲染帧对目标偏移的逼近比例（≈120ms 收敛）。</summary>
    public const double ScrollFrameLerp = 0.22;

    /// <summary>滚轮平滑滚动的收敛阈值（offset 差小于此视为到达）。</summary>
    public const double ScrollFrameEpsilon = 0.5;

    /// <summary>指示灯进程轮询间隔——**空闲档**：全场无运行项、也无刚打开的项目时的间隔
    /// （DispatcherTimer，非动画时长；入 AnimTuning 仅为时长集中）。</summary>
    public const double RunningPollMs = 2000;

    /// <summary>指示灯轮询——**活跃档**：有项目处于"运行中"时的间隔（2026-10-05 清单06任务1：
    /// 原先恒 2s，程序关闭后指示灯最长要等近 2s 才熄灭）→ 现在 ~0.7s 内熄灭。</summary>
    public const double RunningPollActiveMs = 700;

    /// <summary>指示灯轮询——**冲刺档**：刚经板打开项目后的间隔（2026-10-05 清单06任务1：
    /// 程序开得快时"三点等到合并"不该拖到下一拍）。</summary>
    public const double RunningPollBurstMs = 300;

    /// <summary>冲刺档维持时长（自"打开项目"起算；过期后按运行态回落活跃/空闲档）。</summary>
    public const double RunningPollBurstHoldMs = 4000;

    // ===== 拖到文件夹图标上（2026-10-05 清单06任务3）=====

    /// <summary>命中区半径系数：以图标**原始中心**为圆心、图标宽度 × 本系数为半径的圆内才算"拖到该文件夹上"
    /// （用户口径 = 图标宽度的 1/3）。</summary>
    public const double FolderDropRadiusRatio = 1.0 / 3.0;

    /// <summary>拖入提示描边与目标图标的间距（描边不贴图标，"要有一定距离"）。</summary>
    public const double FolderRingGapPx = 6;

    /// <summary>拖入提示描边的宽度（"粗边"）。</summary>
    public const double FolderRingStrokePx = 3;

    // ===== 缓动映射（03 §2 表：项目缓动名 → WPF 实现）=====

    static readonly QuadraticEase EaseInOutQuadEase = new() { EasingMode = EasingMode.EaseInOut };
    static readonly QuadraticEase EaseInQuadEase = new() { EasingMode = EasingMode.EaseIn };
    static readonly QuadraticEase EaseOutQuadEase = new() { EasingMode = EasingMode.EaseOut };
    static readonly CubicEase EaseOutCubicEase = new() { EasingMode = EasingMode.EaseOut };
    static readonly CubicEase EaseInCubicEase = new() { EasingMode = EasingMode.EaseIn };

    /// <summary>
    /// BackEase EaseOut：峰值 = 1 + 4a³/(27(a+1)²)（a=Amplitude）。03 §2 要求"过冲 ≤1.08"，
    /// 解得 a=1.5 时峰值恰为 1.08（表内"Amplitude≈0.35"对应峰值仅 1.003，肉眼不可见，
    /// 与 1.08 的约束矛盾——按约束取 1.5，见 STEP_LOG 补充决策）。
    /// </summary>
    static readonly BackEase EaseOutBackEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 1.5 };

    static AnimTuning()
    {
        // 缓动实例全局共享（多动画并发引用），按 Freezable 规则冻结后线程/多主安全
        foreach (var e in new System.Windows.Freezable[] {
            EaseInOutQuadEase, EaseInQuadEase, EaseOutQuadEase, EaseOutCubicEase, EaseInCubicEase, EaseOutBackEase })
            if (e.CanFreeze) e.Freeze();
    }

    /// <summary>缓动枚举 → WPF EasingFunction（Linear 返回 null = 无缓动）。bounce 关键帧手工构造不经此表。</summary>
    public static IEasingFunction? Map(EaseStyle style) => style switch
    {
        EaseStyle.EaseInOutQuad => EaseInOutQuadEase,
        EaseStyle.EaseInQuad => EaseInQuadEase,
        EaseStyle.EaseOutQuad => EaseOutQuadEase,
        EaseStyle.EaseOutCubic => EaseOutCubicEase,
        EaseStyle.EaseInCubic => EaseInCubicEase,
        EaseStyle.EaseOutBack => EaseOutBackEase,
        _ => null, // Linear
    };
}
