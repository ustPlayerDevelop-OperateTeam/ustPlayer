using System;

namespace UstPlayer.Platform;

/// <summary>
/// 单调墙钟 — 时序逻辑唯一的真实时间来源。
/// </summary>
/// <remarks>
/// <para>
/// 1.1.x 的 <c>player.py</c> 直接调用 <c>time.time()</c>，导致「降级重锚定」「播完补帧」
/// 这些依赖真实时间的分支只能在有音频设备的集成环境里验证。抽出本接口后，
/// 时序状态机可以在单元测试中用可编程假时钟精确驱动（见
/// <c>UstPlayer.Tests/Timing</c>）。
/// </para>
/// <para>
/// 实现要求：返回**单调递增**的秒数，且不受系统时间调整影响。
/// </para>
/// </remarks>
internal interface IClock
{
    /// <summary>当前时间（秒，单调递增）。</summary>
    /// <returns>单调时间戳。</returns>
    double NowSeconds { get; }
}

/// <summary>
/// 基于 <see cref="Environment.TickCount64"/> 的默认时钟实现。
/// </summary>
/// <remarks>
/// 用 <c>TickCount64</c> 而非 <c>DateTime.Now</c>：前者是单调时钟，
/// 不受用户调整系统时间或 NTP 校时影响（后者会让时间轴跳变）。
/// </remarks>
internal sealed class SystemClock : IClock
{
    /// <summary>进程启动到现在的秒数（进程内单调递增）。</summary>
    public double NowSeconds => Environment.TickCount64 / 1000.0;
}
