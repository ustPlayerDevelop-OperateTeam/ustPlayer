using System;

namespace UstPlayer.Platform;

/// <summary>
/// 伴奏音频后端 — 播放器依赖的唯一音频接口。
/// </summary>
/// <remarks>
/// <para>
/// 沿袭 1.1.x <c>core/audio_backend.py</c> 的设计：把具体音频框架（QtMultimedia /
/// LibVLC / Media Foundation …）的加载、播放与状态机**隔离在实现类内**，
/// 播放器只依赖本窄接口。这样做的直接收益是：
/// 音频状态机能在没有音频设备的环境里用可编程假后端测试
/// （1.1.x 的 <c>tests/test_audio_backend.py</c> 即为此）。
/// </para>
/// <para>
/// 事件语义（与 1.1.x 的 <c>media_ready</c> / <c>media_ended</c> / <c>media_error</c> 一致）：
/// 就绪后播放器才调用 <see cref="Play"/>；收到 <see cref="Ended"/> 或
/// <see cref="Failed"/> 后，播放器按自己的降级规则处理（见 <c>Timing</c> 层）。
/// </para>
/// <para>
/// 后端只负责**如实汇报**媒体状态，不含任何「降级」「看门狗」判断——
/// 那些属于播放时序逻辑。
/// </para>
/// </remarks>
internal interface IAudioBackend : IDisposable
{
    /// <summary>媒体加载完成，可以开始播放。</summary>
    event EventHandler? Ready;

    /// <summary>播放到结尾。</summary>
    event EventHandler? Ended;

    /// <summary>出错，参数为可读错误描述。</summary>
    event EventHandler<string>? Failed;

    /// <summary>开始加载伴奏；之后异步经 <see cref="Ready"/> / <see cref="Ended"/> / <see cref="Failed"/> 汇报。</summary>
    /// <param name="musicPath">伴奏文件路径。</param>
    void Load(string musicPath);

    /// <summary>开始播放（仅当已就绪时有效）。</summary>
    void Play();

    /// <summary>停止播放。</summary>
    void Stop();

    /// <summary>当前播放位置（秒）；未播放返回 0.0。这是播放时间轴的驱动源。</summary>
    /// <returns>播放位置（秒）。</returns>
    double PositionSeconds { get; }

    /// <summary>媒体总时长（秒）；未知返回 0.0。</summary>
    /// <returns>媒体时长（秒）。</returns>
    double DurationSeconds { get; }

    /// <summary>是否正处于播放状态。</summary>
    /// <returns>播放中返回 <see langword="true"/>。</returns>
    bool IsPlaying { get; }

    /// <summary>媒体是否已加载完成。</summary>
    /// <returns>已加载返回 <see langword="true"/>。</returns>
    bool IsLoaded { get; }

    /// <summary>媒体是否仍在加载中。</summary>
    /// <returns>加载中返回 <see langword="true"/>。</returns>
    bool IsLoading { get; }

    /// <summary>媒体是否无效。</summary>
    /// <returns>无效返回 <see langword="true"/>。</returns>
    bool IsInvalid { get; }

    /// <summary>
    /// 媒体是否已播放到结尾。
    /// </summary>
    /// <returns>已到结尾返回 <see langword="true"/>。</returns>
    /// <remarks>
    /// <b>不要单独依赖本属性判断「播完」。</b>1.1.x 踩过的坑：Qt 的 FFmpeg 后端在播放结束后
    /// 会把 <c>mediaStatus</c> 回落为 <c>LoadedMedia</c> 而非停留在 <c>EndOfMedia</c>，
    /// 只看本属性会把「已播完」误判为「已加载但未播放」进而错误降级（短伴奏尤其必现）。
    /// 正确做法是同时参考 <see cref="Ended"/> 事件是否已置位。
    /// </remarks>
    bool IsFinished { get; }

    /// <summary>
    /// 状态快照（供日志与诊断使用）。
    /// </summary>
    /// <returns>可读的状态描述。</returns>
    /// <remarks>
    /// 存在的理由：音频链路出问题时的表现是「画面在动但没有声音」，
    /// 从界面完全看不出是「没配伴奏」「解析还没完成」「解析失败」还是「播放没起来」。
    /// 这几个字段单独看都容易误判，一次性写进日志才能一眼定位。
    /// </remarks>
    string DescribeState();
}
