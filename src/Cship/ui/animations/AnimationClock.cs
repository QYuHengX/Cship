using System;
using System.Windows.Media.Animation;
using Cship.Core;

namespace Cship.Ui.Animations;

/// <summary>
/// 动画时钟（00 §6 术语表；步骤 03 任务 1）：进程级动画帧率唯一出口。
/// general.fps（60/90/120/165）经 Timeline.SetDesiredFrameRate 写到每个新建时间轴上，
/// 改设置后**下一次动画**即用新帧率（读键发生在动画构造时，天然即时生效）。
///
/// 记录局限（步骤文件 1.1 要求写进 STEP_LOG）：DesiredFrameRate 只是"期望帧率"，
/// 实际渲染受显示器 vsync 与 WPF 渲染线程双重限制——60Hz 显示器上 165 档表现为
/// "尽可能快"（封顶 vsync），不会凭空快过屏幕刷新。
/// </summary>
public static class AnimationClock
{
    static int _fps;

    /// <summary>当前帧率档（general.fps；非法值回退 60）。</summary>
    public static int Fps => _fps == 0 ? Refresh() : _fps;

    /// <summary>重读 general.fps（每次 Apply 前调用，读键开销可忽略；档位变化记日志留痕）。</summary>
    public static int Refresh()
    {
        int raw = SettingsStore.Instance.GetInt("general.fps", 60);
        int fps = raw is 60 or 90 or 120 or 165 ? raw : 60;
        if (fps != _fps)
        {
            _fps = fps;
            Logger.Info($"动画帧率档 = {_fps} fps（general.fps；实际渲染受显示器 vsync 上限）");
        }
        return _fps;
    }

    /// <summary>把当前帧率档写到时间轴（附加属性设在具体 Animation/Storyboard 上——Timeline 子节点设置无效）。</summary>
    public static void Apply(Timeline timeline)
    {
        Timeline.SetDesiredFrameRate(timeline, Refresh());
    }
}
