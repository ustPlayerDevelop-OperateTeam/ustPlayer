using System;

using UstPlayer.Platform;

namespace UstPlayer.Tests.Timing;

/// <summary>可编程时钟：测试中显式推进时间，替代 1.1.x 里不可控的 <c>time.time()</c>。</summary>
/// <remarks>
/// <b>起点可以为非零</b>，这一点很重要：真实时钟 <see cref="UstPlayer.Platform.SystemClock"/>
/// 从系统启动算起（非零），而从 0 开始的假时钟恰好等同于「已正确锚定」，
/// 会把「忘了锚定时间轴」这类 bug 掩盖掉。涉及锚定的测试应传入非零起点。
/// </remarks>
internal sealed class FakeClock : IClock
{
    /// <summary>创建时钟。</summary>
    /// <param name="startSeconds">起始时间（秒）；模拟真实时钟时应传非零值。</param>
    public FakeClock(double startSeconds = 0.0)
    {
        NowSeconds = startSeconds;
    }

    /// <summary>当前时间（秒）。</summary>
    public double NowSeconds { get; private set; }

    /// <summary>推进时间。</summary>
    /// <param name="seconds">推进的秒数。</param>
    public void Advance(double seconds) => NowSeconds += seconds;
}

/// <summary>
/// 可编程音频后端：模拟真实后端「播放位置随时间前进」的行为。
/// </summary>
/// <remarks>
/// <para>
/// 这套假件正是 <see cref="IAudioBackend"/> 抽出来的目的——让「位置随时间前进」
/// 「播完回落为已加载」「看门狗超限降级」这类分支可精确复现，而不依赖真实音频设备
/// （对应 1.1.x <c>tests/test_audio_backend.py</c> 的思路）。
/// </para>
/// <para>
/// 位置模型：<see cref="Play"/> 被调用后，位置 = 时钟已推进的秒数，且不超过
/// <see cref="DurationSeconds"/>；到达时长即视为已到结尾。需要精确控制位置时用
/// <see cref="PositionOverride"/>。
/// </para>
/// </remarks>
internal sealed class FakeAudioBackend : IAudioBackend
{
    private readonly IClock _clock;
    private double _playStartSeconds;
    private bool _playing;

    /// <summary>创建假后端。</summary>
    /// <param name="clock">与播放会话共用的时钟。</param>
    public FakeAudioBackend(IClock clock)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public event EventHandler? Ready;

    /// <inheritdoc />
    public event EventHandler? Ended;

    /// <inheritdoc />
    public event EventHandler<string>? Failed;

    /// <summary>已调用 <see cref="Play"/> 的次数。</summary>
    public int PlayCount { get; private set; }

    /// <summary>已调用 <see cref="Stop"/> 的次数。</summary>
    public int StopCount { get; private set; }

    /// <summary>媒体时长（秒）。</summary>
    public double DurationSeconds { get; set; }

    /// <summary>显式覆盖播放位置（秒）；为 <see langword="null"/> 时按时钟推算。</summary>
    public double? PositionOverride { get; set; }

    /// <summary>显式覆盖「是否正在播放」；为 <see langword="null"/> 时按是否调用过 <see cref="Play"/> 判断。</summary>
    public bool? IsPlayingOverride { get; set; }

    /// <summary>显式覆盖「是否已到结尾」；为 <see langword="null"/> 时按事件与时长推算。</summary>
    public bool? IsFinishedOverride { get; set; }

    /// <summary>模拟的「已加载」状态。</summary>
    public bool IsLoaded { get; set; } = true;

    /// <summary>模拟的「仍在加载」状态。</summary>
    public bool IsLoading { get; set; }

    /// <summary>模拟的「媒体无效」状态。</summary>
    public bool IsInvalid { get; set; }

    /// <summary>是否已收到「播放到结尾」。</summary>
    public bool HasEnded { get; private set; }

    /// <summary>是否已释放。</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public double PositionSeconds
    {
        get
        {
            if (PositionOverride is { } explicitPosition)
            {
                return explicitPosition;
            }

            if (!_playing)
            {
                return 0.0;
            }

            var elapsed = Math.Max(0.0, _clock.NowSeconds - _playStartSeconds);
            return DurationSeconds > 0 ? Math.Min(elapsed, DurationSeconds) : elapsed;
        }
    }

    /// <inheritdoc />
    public bool IsPlaying => IsPlayingOverride ?? _playing;

    /// <inheritdoc />
    /// <remarks>
    /// 与 1.1.x 的 QtAudioBackend 一致：只看「是否已到结尾」这一状态本身，
    /// 与时长是否已知无关（时长未知时 <c>QMediaPlayer.mediaStatus</c> 也不会变成 EndOfMedia）。
    /// </remarks>
    public bool IsFinished => IsFinishedOverride ?? HasEnded;

    /// <inheritdoc />
    public void Load(string musicPath)
    {
        // 只标记为「加载中」：真实后端此时尚不可播放，位置与时长都未知。
        // 路径本身不被状态机消费，故不保存。
        _ = musicPath;
        IsLoading = true;
    }

    /// <inheritdoc />
    public void Play()
    {
        PlayCount++;
        _playing = true;
        _playStartSeconds = _clock.NowSeconds;
    }

    /// <inheritdoc />
    public void Stop()
    {
        StopCount++;
        _playing = false;
    }

    /// <inheritdoc />
    public void Dispose() => Disposed = true;

    /// <summary>模拟媒体加载完成。</summary>
    public void RaiseReady() => Ready?.Invoke(this, EventArgs.Empty);

    /// <summary>模拟播放到结尾。</summary>
    public void RaiseEnded()
    {
        HasEnded = true;
        Ended?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>模拟出错。</summary>
    public void RaiseFailed(string message) => Failed?.Invoke(this, message);
}
