using System;

namespace UstPlayer.Platform;

/// <summary>
/// 空音频后端 — 代表「没有伴奏」或「音频不可用」。
/// </summary>
/// <remarks>
/// <para>
/// 播放器在两种情况下走这条路：用户没配伴奏，或音频后端创建失败。
/// 此时 <see cref="Timing.PlaybackSession"/> 立刻按墙钟计时，
/// 因此它**不需要**任何音频设施就能被完整验证（也是 CI 与无声卡环境下的默认路径）。
/// </para>
/// <para>
/// 它实现 <see cref="IAudioBackend"/> 的语义是「如实汇报一切皆无」：
/// 永不就绪、永不播放、位置恒为 0——而不是「假装就绪再失败」。
/// </para>
/// </remarks>
internal sealed class NullAudioBackend : IAudioBackend
{
    /// <summary>共享实例（无可变状态，可安全复用）。</summary>
    internal static readonly NullAudioBackend Instance = new();

    /// <inheritdoc />
    public event EventHandler? Ready
    {
        // 空后端永不就绪：事件声明出来是为了满足接口，永不触发
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler? Ended
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<string>? Failed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public double PositionSeconds => 0.0;

    /// <inheritdoc />
    public double DurationSeconds => 0.0;

    /// <inheritdoc />
    public bool IsPlaying => false;

    /// <inheritdoc />
    public bool IsLoaded => false;

    /// <inheritdoc />
    public bool IsLoading => false;

    /// <inheritdoc />
    public bool IsInvalid => true;

    /// <inheritdoc />
    public bool IsFinished => false;

    /// <inheritdoc />
    public string DescribeState() => "空后端（无伴奏或音频不可用）";

    /// <inheritdoc />
    public void Load(string musicPath)
    {
        _ = musicPath;
    }

    /// <inheritdoc />
    public void Play()
    {
    }

    /// <inheritdoc />
    public void Stop()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
