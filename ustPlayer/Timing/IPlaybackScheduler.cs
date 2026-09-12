using System;

namespace UstPlayer.Timing;

/// <summary>
/// 定时回调调度器 — 把「稍后再检查一次」交给宿主实现。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是一次真实事故：<see cref="PlaybackSession.CheckAudioReady"/> 曾经
/// **只在测试里被调用**，生产代码里没有任何人驱动它——于是音频永远不会开始播放，
/// 而所有单元测试都是绿的。这类「逻辑正确但接线缺失」的缺陷靠纯逻辑测试挡不住，
/// 必须由一条**穿过生产接线**的测试来守（见 <c>PlayerFrameCompositor</c> 的音频测试）。
/// </para>
/// <para>
/// 宿主用定时器实现（播放窗口用 <c>DispatcherTimer</c>，测试用确定性假件），
/// 因此这里只规定「延迟多久回调一次」，不规定在哪个线程上。
/// </para>
/// </remarks>
internal interface IPlaybackScheduler
{
    /// <summary>
    /// 在指定延迟后回调一次。
    /// </summary>
    /// <param name="delay">延迟。</param>
    /// <param name="callback">要执行的回调。</param>
    /// <remarks>
    /// 只要求「至多一次」（不需要重复触发）：调用方在回调里若判断还需再等，
    /// 会**再次**调用本方法。取消由宿主的生命周期负责（窗口关闭时停掉定时器）。
    /// </remarks>
    void ScheduleOnce(TimeSpan delay, Action callback);
}
