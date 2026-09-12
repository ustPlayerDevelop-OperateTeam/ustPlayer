using System;

using Avalonia.Threading;

using UstPlayer.Timing;

namespace UstPlayer.Views;

/// <summary>
/// 用 <see cref="DispatcherTimer"/> 实现的播放调度器。
/// </summary>
/// <remarks>
/// <para>
/// 播放窗口的音频看门狗要「过一会儿再检查一次」，而窗口里的定时器只有一个
/// （<c>_audioWatchdogTimer</c>）——本类把它适配成
/// <see cref="IPlaybackScheduler"/>，于是时序逻辑不必知道 UI 框架的存在。
/// </para>
/// <para>
/// 只在 UI 线程上使用（<see cref="DispatcherTimer"/> 本身也只在 UI 线程上工作）。
/// 取消由窗口负责：关闭时停表，之后不会再触发。
/// </para>
/// </remarks>
/// <param name="timer">要驱动的定时器（调用方负责启停与释放）。</param>
internal sealed class DispatcherPlaybackScheduler(DispatcherTimer timer) : IPlaybackScheduler
{
    private readonly DispatcherTimer _timer =
        timer ?? throw new ArgumentNullException(nameof(timer));

    /// <inheritdoc />
    public void ScheduleOnce(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        _timer.Interval = delay;
        _timer.Start();
    }
}
