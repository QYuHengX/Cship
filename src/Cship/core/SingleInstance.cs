using System;
using System.Threading;
namespace Cship.Core;

/// <summary>
/// 单实例（步骤 2.2）：Mutex 防重复运行；第二实例经命名 EventWaitHandle 发 activate
/// 信号后退出，首实例收到信号让悬浮窗播放一次呼吸动画。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = "Cship_SingleInstance_Mutex";
    public const string ActivateEventName = "Cship_Activate_Event";

    Mutex? _mutex;
    EventWaitHandle? _activateEvent;
    RegisteredWaitHandle? _waitHandle;

    /// <summary>首实例收到 activate（线程池线程回调，消费方需自行调度到 UI 线程）。</summary>
    public event Action? ActivateRequested;

    public bool IsFirstInstance { get; private set; }

    /// <summary>尝试成为首实例；成功后开始监听 activate 信号。</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        GC.KeepAlive(_mutex); // 防早回收导致误判多实例（步骤·技术指引）
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        IsFirstInstance = true;

        // 同名事件若因上次异常退出残留，直接复用（AutoReset，一次信号一次呼吸）
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _waitHandle = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => ActivateRequested?.Invoke(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    /// <summary>第二实例调用：唤醒首实例后，第二实例自行退出。</summary>
    public static void SignalExisting()
    {
        // 首实例建锁与建事件之间存在毫秒级窗口，小幅重试兜底
        for (int i = 0; i < 10; i++)
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle))
            {
                using (handle)
                    handle.Set();
                return;
            }
            Thread.Sleep(50);
        }
    }

    /// <summary>主动放弃单实例身份（重启场景：先放锁再拉起新进程）。</summary>
    public void Release()
    {
        _waitHandle?.Unregister(null);
        _waitHandle = null;
        _activateEvent?.Dispose();
        _activateEvent = null;
        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 当前线程不持有（异常路径），忽略
            }
            _mutex.Dispose();
            _mutex = null;
        }
        IsFirstInstance = false;
    }

    public void Dispose() => Release();
}
