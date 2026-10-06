using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Cship.Ui.Animations;

/// <summary>
/// 动画一行式辅助（步骤 03 任务 1.2）：内部构造动画 + DesiredFrameRate（fps 即时生效）+
/// Completed 一次性订阅，供各处一行调用。完成回调里默认**移除动画并把基值落定为终值**
/// ——等价于 FillBehavior=Stop + 手动复位（03 §3 要求），同时释放属性持有的动画引用
/// （防 Storyboard.Completed 式的目标引用泄漏）。同一 Transform 的两个属性（X/Y）
/// 各调一次 Run（两个独立 DoubleAnimation，不能共用）。
/// </summary>
public static class Anim
{
    /// <summary>毫秒 → TimeSpan（全项目唯一入口，时长常量来自 AnimTuning）。</summary>
    public static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    /// <summary>
    /// 单属性 DoubleAnimation：from 缺省=**当前有效值接续**（批次十二，原文任务1）——动画进行中
    /// 再次触发时不再整段重播，而是以当前输出值（含进行中动画的实时值）为起点继续滑向新目标
    /// （例：0→10 升到 5 时再触发，从 5 接着升；10→0 降到 4 时再触发上行，从 4 起升）。
    /// 实现要点：先快照 GetValue（含动画值）→ 摘除旧动画 → 基值落定为快照 → 再从快照起播，
    /// 保证 Handoff 之间无跳变。完成时复位基值为 to（resetOnDone=false 保留动画持值）。
    /// </summary>
    public static DoubleAnimation Run(IAnimatable target, DependencyProperty property,
        double? from, double to, double durationMs,
        EaseStyle ease = EaseStyle.EaseInOutQuad,
        Action? onDone = null,
        bool resetOnDone = true)
    {
        double start;
        if (from is { } f)
        {
            start = f;
        }
        else
        {
            var obj = (DependencyObject)target;
            start = (double)obj.GetValue(property);
            target.BeginAnimation(property, null); // 摘旧动画（其 Completed 随之作废，接续语义下无副作用）
            obj.SetValue(property, start);         // 基值落定为快照，防摘除瞬间回弹
        }
        var a = new DoubleAnimation
        {
            To = to,
            From = start,
            Duration = Ms(durationMs),
            EasingFunction = AnimTuning.Map(ease),
        };
        if (resetOnDone || onDone != null)
            a.Completed += (_, _) =>
            {
                if (resetOnDone)
                {
                    target.BeginAnimation(property, null);
                    ((DependencyObject)target).SetValue(property, to); // UIElement 实现的是 IAnimatable，基值复位经 DependencyObject
                }
                onDone?.Invoke();
            };
        AnimationClock.Apply(a);
        target.BeginAnimation(property, a);
        return a;
    }

    /// <summary>
    /// 关键帧动画：keys 为（值，绝对时刻 ms，该段缓动）序列，KeyTime 用绝对时刻表达。
    /// 完成时同样复位基值为末帧值。
    /// </summary>
    public static DoubleAnimationUsingKeyFrames RunKeys(IAnimatable target, DependencyProperty property,
        (double Value, double AtMs, EaseStyle Ease)[] keys,
        Action? onDone = null,
        bool resetOnDone = true)
    {
        var kf = new DoubleAnimationUsingKeyFrames();
        foreach (var (value, atMs, ease) in keys)
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(Ms(atMs)), AnimTuning.Map(ease)));
        if (resetOnDone || onDone != null)
            kf.Completed += (_, _) =>
            {
                if (resetOnDone && keys.Length > 0)
                {
                    target.BeginAnimation(property, null);
                    ((DependencyObject)target).SetValue(property, keys[^1].Value); // 同上：IAnimatable → DependencyObject
                }
                onDone?.Invoke();
            };
        AnimationClock.Apply(kf);
        target.BeginAnimation(property, kf);
        return kf;
    }

    /// <summary>
    /// 并行组合：每个启动委托收到统一的完成信号，全部到齐后回调一次（任务 1.2 多属性并行）。
    /// UI 线程专用（计数无锁）。
    /// </summary>
    public static void Parallel(Action<Action>[] starters, Action? onAllDone)
    {
        if (starters.Length == 0)
        {
            onAllDone?.Invoke();
            return;
        }
        int remaining = starters.Length;
        foreach (var start in starters)
            start(() =>
            {
                if (--remaining == 0)
                    onAllDone?.Invoke();
            });
    }

    /// <summary>串行组合：依序执行各步骤，每步收到"下一步"委托，全部完成后回调（任务 1.2）。</summary>
    public static void Sequence(Action<Action>[] steps, Action? onAllDone = null)
    {
        void RunAt(int i)
        {
            if (i >= steps.Length)
            {
                onAllDone?.Invoke();
                return;
            }
            steps[i](() => RunAt(i + 1));
        }
        RunAt(0);
    }
}

/// <summary>
/// hover 进出动画的"完整播放"编排（批次十二修订 B，用户裁决）：触发后**强行播放完整动画**，
/// 播放进行中的进/离触发一律忽略（不接续、不重播、不折返）。动画播完时核对指针实际位置
/// 把播放期间被丢弃的触发补账——升完发现指针已不在 → 补播回程；降完发现指针已回来 → 再升起；
/// 任何时序都不会半途折返或停在半空。每组进/出动画一个实例（内部含状态，勿共享）。
/// </summary>
public sealed class HoverCycle
{
    bool _busy;
    Action<Action>? _startUp;
    Action<Action>? _startReturn;
    Func<bool>? _hoverNow;

    /// <summary>装配：startUp/startReturn 收到完成回调（须传给该组**末尾**动画的 onDone）；
    /// hoverNow = 指针当前是否在触发区内。</summary>
    public void Configure(Action<Action> startUp, Action<Action> startReturn, Func<bool> hoverNow)
    {
        _startUp = startUp;
        _startReturn = startReturn;
        _hoverNow = hoverNow;
    }

    public void OnEnter() => Trigger(true);
    public void OnLeave() => Trigger(false);

    void Trigger(bool enter)
    {
        if (_busy) return; // 播放中：二次触发忽略（用户裁决）
        _busy = true;
        if (enter) _startUp?.Invoke(OnDone(true));
        else _startReturn?.Invoke(OnDone(false));
    }

    Action OnDone(bool enter) => () =>
    {
        _busy = false;
        bool over = _hoverNow?.Invoke() ?? false;
        if (enter && !over) Trigger(false);     // 升完指针已不在 → 补回程
        else if (!enter && over) Trigger(true); // 降完指针已回来 → 再升起
    };

    /// <summary>外部编排接管属性（消散/呼吸等）时调用，防 _busy 闩死。</summary>
    public void Reset() => _busy = false;
}
