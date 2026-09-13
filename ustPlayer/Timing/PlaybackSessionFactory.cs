using UstPlayer.Models;
using UstPlayer.Platform;

namespace UstPlayer.Timing;

/// <summary>
/// 播放会话工厂 —— 把「播放参数 + 时钟 + 音频后端」组装成 <see cref="PlaybackSession"/>。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：<b>播放器和视频导出都要「按同一套时序播放一遍」</b>，
/// 而「设置里的稳定 key → 时序枚举」的映射（静默显示 / 结束显示 / 音名占位符）
/// 只应有一份实现。曾经这段映射长在 <c>PlayerFrameCompositor</c> 里，
/// 于是它成了「想播放就必须经过渲染器合成器」的隐藏耦合——
/// 播放器画面改用自绘后，会话不再需要渲染器，这段映射也就该独立出来。
/// </para>
/// <para>
/// 本类只做组装，不含时序逻辑（那是 <see cref="PlaybackSession"/> 的职责）。
/// </para>
/// </remarks>
internal static class PlaybackSessionFactory
{
    /// <summary>
    /// 按播放参数创建会话。
    /// </summary>
    /// <param name="parameters">播放参数（含 UST、显示开关与样式）。</param>
    /// <param name="clock">时钟（真实运行用 <see cref="SystemClock"/>，测试用假件）。</param>
    /// <param name="audio">音频后端；为 <see langword="null"/> 时走墙钟计时。</param>
    /// <returns>已就绪的会话（调用方负责释放）。</returns>
    public static PlaybackSession Create(
        PlayerLaunchParams parameters,
        IClock clock,
        IAudioBackend? audio)
    {
        System.ArgumentNullException.ThrowIfNull(parameters);
        System.ArgumentNullException.ThrowIfNull(clock);

        return new PlaybackSession(CreateOptions(parameters), clock, audio);
    }

    /// <summary>
    /// 把播放参数映射为会话选项。
    /// </summary>
    /// <param name="parameters">播放参数。</param>
    /// <returns>会话选项。</returns>
    internal static PlaybackSessionOptions CreateOptions(PlayerLaunchParams parameters)
    {
        System.ArgumentNullException.ThrowIfNull(parameters);

        var text = new PlaybackTextOptions(
            ParseSilentMode(parameters.Style.SilentDisplay),
            parameters.Style.SilentCustomText,
            ParseEndMode(parameters.Style.EndDisplay),
            parameters.Style.EndCustomText,
            ParsePitchMode(parameters.Style.PitchPlaceholder),
            parameters.Style.PitchCustomText);

        return new PlaybackSessionOptions(parameters.Ust.Tempo, parameters.Ust.Notes, text);
    }

    /// <summary>把存储层稳定 key 映射为静默显示方式。</summary>
    /// <param name="key">稳定 key。</param>
    /// <returns>显示方式。</returns>
    internal static SilentDisplayMode ParseSilentMode(string? key) => key switch
    {
        "r" => SilentDisplayMode.Rest,
        "dash" => SilentDisplayMode.Dash,
        "custom" => SilentDisplayMode.Custom,
        _ => SilentDisplayMode.None,
    };

    /// <summary>把存储层稳定 key 映射为结束显示方式。</summary>
    /// <param name="key">稳定 key。</param>
    /// <returns>显示方式。</returns>
    internal static EndDisplayMode ParseEndMode(string? key) => key switch
    {
        "end" => EndDisplayMode.End,
        "dash" => EndDisplayMode.Dash,
        "custom" => EndDisplayMode.Custom,
        _ => EndDisplayMode.None,
    };

    /// <summary>把存储层稳定 key 映射为音名占位符方式。</summary>
    /// <param name="key">稳定 key。</param>
    /// <returns>占位符方式。</returns>
    internal static PitchPlaceholderMode ParsePitchMode(string? key) => key switch
    {
        "dash" => PitchPlaceholderMode.Dash,
        "custom" => PitchPlaceholderMode.Custom,
        _ => PitchPlaceholderMode.None,
    };
}
