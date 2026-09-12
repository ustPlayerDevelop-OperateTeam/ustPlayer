using System;
using System.Collections.Generic;

using UstPlayer.Models;
using UstPlayer.Platform;

namespace UstPlayer.Timing;

/// <summary>空拍 / 结束 / 音名占位符的文字配置。</summary>
/// <param name="SilentMode">空拍显示方式。</param>
/// <param name="SilentCustomText">空拍自定义文字。</param>
/// <param name="EndMode">结束显示方式。</param>
/// <param name="EndCustomText">结束自定义文字。</param>
/// <param name="PitchMode">音名占位符规则。</param>
/// <param name="PitchCustomText">音名自定义占位文字。</param>
internal sealed record PlaybackTextOptions(
    SilentDisplayMode SilentMode,
    string SilentCustomText,
    EndDisplayMode EndMode,
    string EndCustomText,
    PitchPlaceholderMode PitchMode,
    string PitchCustomText);

/// <summary>会话配置。</summary>
/// <param name="Tempo">速度（BPM），必须为有限正数。</param>
/// <param name="Notes">音符序列。</param>
/// <param name="Text">文字选项。</param>
/// <param name="AudioReadyTimeoutChecks">
/// 看门狗连续判定「仍在加载」的最大次数，超过即降级（对应 1.1.x 的 <c>_audio_ready_checks >= 3</c>）。
/// </param>
/// <param name="WatchdogIntervalMs">看门狗重试间隔（对应 1.1.x 的 3000ms）。</param>
/// <param name="EndGraceMs">结束文字停留时长（对应 1.1.x 的 1000ms）。</param>
internal sealed record PlaybackSessionOptions(
    double Tempo,
    IReadOnlyList<NoteInfo> Notes,
    PlaybackTextOptions Text,
    int AudioReadyTimeoutChecks = 3,
    double WatchdogIntervalMs = 3000);

/// <summary>本次推进得到的时序结果。</summary>
/// <param name="ElapsedSeconds">当前播放位置（秒）。</param>
/// <param name="CurrentTick">当前 tick。</param>
/// <param name="Step">收尾步骤。</param>
/// <param name="CurrentNote">当前音符；空拍 / 结束区间为 <see langword="null"/>。</param>
/// <param name="LyricText">当前歌字（含空拍 / 结束文字）。</param>
/// <param name="NoteName">当前音名。</param>
/// <param name="IsPlayerFinished">是否已播完（宿主应停止推进并关闭窗口）。</param>
internal sealed record PlaybackState(
    double ElapsedSeconds,
    double CurrentTick,
    PlaybackEndStep Step,
    NoteInfo? CurrentNote,
    string LyricText,
    string NoteName,
    bool IsPlayerFinished);

/// <summary>收尾步骤。</summary>
internal enum PlaybackEndStep
{
    /// <summary>正常显示音符。</summary>
    Continue,

    /// <summary>音符内容已结束但音频未播完 → 显示空拍 / 静默文字。</summary>
    Silent,

    /// <summary>播放结束 → 显示结束文字。</summary>
    End,
}

/// <summary>
/// 播放时序状态机 — 从 1.1.x <c>core/player.py</c> 的 <c>NoteLyricDisplay</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 只负责**时序与文字状态**：不接触任何 UI、音频框架或渲染器。
/// 音频经 <see cref="IAudioBackend"/> 窄接口，真实时间经 <see cref="IClock"/>，
/// 两者都可在测试中替换，因此 1.1.x 里那些「只在特定音频行为下才复现」的分支
/// 现在都能用假件精确复现。
/// </para>
/// <para>
/// 移植时逐一保留了 1.1.x 踩坑后加上的守卫：
/// </para>
/// <list type="number">
///   <item>「就绪只播一次」：<c>play()</c> 只调用一次，已播完 / 已进入结束态不再重播；</item>
///   <item>「播完锚点只记一次」：重复的 EndOfMedia 不再改写结束时刻，否则时间轴回跳；</item>
///   <item>「已播完优先排除」：后端停在 EndOfMedia 但信号未发出时补记锚点，且该判断必须
///   在「已加载」判断之前（二者状态互斥，嵌套在内层永不执行）；</item>
///   <item>「降级重锚定」：降级瞬间以当前播放位置为新零点，避免时间轴跳变；</item>
///   <item>「看门狗超限降级」：长时间未就绪不再无限重试，避免永久停在 0:00。</item>
/// </list>
/// <para>
/// 线程安全：所有公开操作在同一把锁内进行，因为音频后端的回调可能来自其他线程。
/// </para>
/// </remarks>
internal sealed class PlaybackSession : IDisposable
{
    /// <summary>一拍（四分音符）的 tick 数（与播放器、渲染器、导出三方的约定一致）。</summary>
    public const int TicksPerQuarterNote = 480;

    private readonly Lock _syncRoot = new();
    private readonly IClock _clock;
    private readonly IAudioBackend? _audio;
    private readonly PlaybackSessionOptions _options;
    private readonly List<NoteTickRange> _ranges = [];

    private int _noteIndexHint;
    private bool _disposed;

    // ---------- 时间轴 ----------
    private double _startRealSeconds;
    private bool _startedOnce;
    private double _elapsedSeconds;
    private bool _completed;

    // ---------- 音频状态 ----------
    private bool _audioHealthy;
    private bool _mediaFinished;
    private bool _playIssued;
    private double _mediaDurationSeconds;
    private double _mediaFinishRealSeconds;
    private double _degradedAtSeconds;
    private double _degradedRealSeconds;
    private int _audioReadyChecks;

    // ---------- 文字状态 ----------
    private string _lastValidLyric = string.Empty;
    private string _currentLyric = string.Empty;
    private string _currentNoteName = string.Empty;
    private NoteInfo? _currentNote;

    /// <summary>创建会话。</summary>
    /// <param name="options">会话配置。</param>
    /// <param name="clock">时钟。</param>
    /// <param name="audio">音频后端；为 <see langword="null"/> 表示纯可视化计时。</param>
    internal PlaybackSession(PlaybackSessionOptions options, IClock clock, IAudioBackend? audio)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _options = options;
        _clock = clock;
        _audio = audio;

        // 防御性校验：解析器已保证 tempo 合法，但直接构造的调用方可能绕过；
        // 0 / 负数 / NaN / Inf 都会让时间轴失效（0 会让时间轴永远停在第 0 tick）。
        Tempo = double.IsFinite(options.Tempo) && options.Tempo > 0 ? options.Tempo : 120.0;
        TickPerSecond = Tempo * TicksPerQuarterNote / 60.0;

        BuildTickRanges(options.Notes);

        if (_audio is not null)
        {
            _audioHealthy = true;
            _audio.Ready += OnAudioReady;
            _audio.Ended += OnAudioEnded;
            _audio.Failed += OnAudioFailed;
        }
    }

    /// <summary>实际采用的速度（BPM）。</summary>
    internal double Tempo { get; }

    /// <summary>每秒对应的 tick 数。</summary>
    internal double TickPerSecond { get; }

    /// <summary>音符总 tick 数。</summary>
    internal double TotalTicks { get; private set; }

    /// <summary>是否仍在使用音频驱动（降级或未配置音频时为 <see langword="false"/>）。</summary>
    internal bool IsAudioHealthy
    {
        get
        {
            lock (_syncRoot)
            {
                return _audioHealthy;
            }
        }
    }

    /// <summary>是否已进入结束态。</summary>
    internal bool IsFinished
    {
        get
        {
            lock (_syncRoot)
            {
                return _completed;
            }
        }
    }

    /// <summary>
    /// 释放会话：退订音频事件并标记为已释放。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须调用</b>。事件订阅是强引用，音频后端会一直持有本会话；
    /// 1.1.x 靠 Qt 的父子对象关系自动回收，这个机制在 .NET 里不存在，
    /// 直接移植会漏掉退订而形成泄漏。
    /// </para>
    /// <para>
    /// <b>不释放音频后端</b>：其所有权归宿主（宿主可能与其他组件共享同一后端）。
    /// 本方法只做退订。
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_audio is not null)
            {
                _audio.Ready -= OnAudioReady;
                _audio.Ended -= OnAudioEnded;
                _audio.Failed -= OnAudioFailed;
            }
        }
    }

    /// <summary>
    /// 建立时间轴锚点。只锚定一次。
    /// </summary>
    /// <remarks>
    /// 1.1.x 的教训：最小化 / 恢复会再次触发窗口显示事件，墙钟零点不能重置，
    /// 否则时间轴会跳变；音频在显示前已降级 / 结束时也要把相应锚点重新锚到真正开始显示的时刻。
    /// </remarks>
    internal void StartOrResume()
    {
        lock (_syncRoot)
        {
            if (_startedOnce)
            {
                return;
            }

            var now = _clock.NowSeconds;
            _startRealSeconds = now;
            _startedOnce = true;

            if (_degradedRealSeconds > 0)
            {
                _degradedRealSeconds = now;
            }

            if (_mediaFinished)
            {
                _mediaFinishRealSeconds = now;
            }
        }
    }

    /// <summary>
    /// 推进一帧，返回当前时序状态。
    /// </summary>
    /// <returns>本帧的时序结果。</returns>
    /// <remarks>
    /// 本方法不吞异常：帧内异常由宿主的帧循环决定如何处理（记日志、跳过本帧等）。
    /// 1.1.x 在此处用 <c>try/except</c> 吞掉异常并每 60 帧记一次日志；
    /// 2.0 把日志职责交回宿主，避免时序层依赖日志设施。
    /// </remarks>
    internal PlaybackState Advance()
    {
        lock (_syncRoot)
        {
            return AdvanceCore();
        }
    }

    /// <summary>
    /// 音频就绪看门狗（对应 1.1.x <c>_check_audio_ready</c>）。
    /// </summary>
    /// <param name="scheduleNext">需要再次检查时的调度回调（宿主用定时器实现）。</param>
    /// <remarks>
    /// 判定顺序刻意与 1.1.x 保持一致：**「已播完」优先排除**，其次补记播完锚点，
    /// 然后才是「已加载但未播放 → 降级」「媒体无效 → 降级」「仍在加载 → 重试 / 超限降级」。
    /// </remarks>
    internal void CheckAudioReady(Action<TimeSpan> scheduleNext)
    {
        ArgumentNullException.ThrowIfNull(scheduleNext);

        lock (_syncRoot)
        {
            if (!_audioHealthy || _audio is null)
            {
                return;
            }

            if (_mediaFinished)
            {
                // 已播完（EndOfMedia 事件已发）：部分后端播完后会把媒体状态回落为「已加载」，
                // 属正常现象，不降级。
                return;
            }

            if (_audio.IsFinished)
            {
                // 后端停留在「已到结尾」但事件未发出：补记播完锚点，不降级。
                // 必须放在「已加载」判断之前——「已加载」与「已到结尾」是互斥状态，
                // 嵌套在内层将永远不会执行（1.1.x 曾因此留下永不生效的分支）。
                HandleMediaEnded();
                return;
            }

            if (_audio.IsLoaded)
            {
                if (!_audio.IsPlaying)
                {
                    Degrade();
                }

                return;
            }

            if (_audio.IsInvalid)
            {
                Degrade();
                return;
            }

            // 仍在加载（或后端缺失导致媒体停在「无媒体」）：等待重试，超限强制降级，
            // 避免「永远加载中」让时间轴停在 0:00 表现为卡死。
            _audioReadyChecks++;
            if (_audioReadyChecks >= _options.AudioReadyTimeoutChecks)
            {
                Degrade();
                return;
            }

            scheduleNext(TimeSpan.FromMilliseconds(_options.WatchdogIntervalMs));
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"PlaybackSession(tempo={Tempo}, ticks={TotalTicks}, audioHealthy={IsAudioHealthy})";

    // ===================== 内部实现 =====================

    private PlaybackState AdvanceCore()
    {
        UpdateElapsed();

        var currentTick = _elapsedSeconds * TickPerSecond;
        var step = ResolveEndStep(currentTick);

        if (step == PlaybackEndStep.End)
        {
            _completed = true;
            _currentLyric = TextRules.EndText(_options.Text.EndMode, _options.Text.EndCustomText);
            _currentNoteName = string.Empty;
            _currentNote = null;

            // 立刻停止音频，避免结束后被重复触发播放
            if (_audioHealthy && _audio is not null)
            {
                _audio.Stop();
            }

            return Snapshot(currentTick, step);
        }

        if (step == PlaybackEndStep.Silent)
        {
            _currentLyric = SilentText();
            _currentNoteName = string.Empty;
            _currentNote = null;

            return Snapshot(currentTick, step);
        }

        var note = FindNoteAt(currentTick);
        if (note is not null)
        {
            ApplyNote(note);
            _currentNote = note;
        }
        else
        {
            _currentNote = null;
        }

        return Snapshot(currentTick, step);
    }

    private PlaybackState Snapshot(double currentTick, PlaybackEndStep step) =>
        new(_elapsedSeconds, currentTick, step, _currentNote, _currentLyric, _currentNoteName, _completed);

    private void UpdateElapsed()
    {
        if (_audioHealthy && _audio is not null)
        {
            if (_mediaFinished)
            {
                // 播完锚点之后按墙钟继续走，保证结束文字停留期间时间仍在推进
                _elapsedSeconds = _mediaDurationSeconds + (_clock.NowSeconds - _mediaFinishRealSeconds);
            }
            else
            {
                _elapsedSeconds = _audio.PositionSeconds;
            }

            return;
        }

        if (_degradedRealSeconds > 0)
        {
            // 音频已降级：从降级瞬间的位置 + 墙钟增量继续，时间轴连续不跳变
            _elapsedSeconds = _degradedAtSeconds + (_clock.NowSeconds - _degradedRealSeconds);
            return;
        }

        _elapsedSeconds = _clock.NowSeconds - _startRealSeconds;
    }

    private PlaybackEndStep ResolveEndStep(double currentTick)
    {
        var contentDone = currentTick >= TotalTicks;

        if (_audioHealthy)
        {
            if (_mediaFinished && contentDone)
            {
                // 音频播完且内容结束 → 显示结束文字
                return PlaybackEndStep.End;
            }

            if (contentDone)
            {
                // 音符内容结束、音频未播完 → 显示空拍文字，继续等待音频
                return PlaybackEndStep.Silent;
            }

            return PlaybackEndStep.Continue;
        }

        return contentDone ? PlaybackEndStep.End : PlaybackEndStep.Continue;
    }

    private NoteInfo? FindNoteAt(double currentTick)
    {
        var ranges = _ranges;
        var count = ranges.Count;
        var hint = _noteIndexHint;

        if (hint < count && ranges[hint].Contains(currentTick))
        {
            return ranges[hint].Note;
        }

        for (var i = hint; i < count; i++)
        {
            if (ranges[i].Contains(currentTick))
            {
                _noteIndexHint = i;
                return ranges[i].Note;
            }
        }

        for (var i = 0; i < hint && i < count; i++)
        {
            if (ranges[i].Contains(currentTick))
            {
                _noteIndexHint = i;
                return ranges[i].Note;
            }
        }

        return null;
    }

    private void ApplyNote(NoteInfo note)
    {
        var lyric = note.Lyric;

        if (TextRules.IsRest(lyric))
        {
            _currentLyric = SilentText();
            _currentNoteName = string.Empty;
            return;
        }

        if (TextRules.IsSustain(lyric))
        {
            _currentLyric = string.IsNullOrEmpty(_lastValidLyric) ? SilentText() : _lastValidLyric;
            _currentNoteName = PitchText(note.NoteNumber);
            return;
        }

        _currentLyric = lyric;
        _lastValidLyric = lyric;
        _currentNoteName = PitchText(note.NoteNumber);
    }

    private string SilentText() =>
        TextRules.SilentText(_options.Text.SilentMode, _options.Text.SilentCustomText);

    private string PitchText(int noteNumber) =>
        TextRules.PitchText(noteNumber, _options.Text.PitchMode, _options.Text.PitchCustomText);

    private void OnAudioReady(object? sender, EventArgs e)
    {
        lock (_syncRoot)
        {
            // 就绪后**只播一次**：已播完或已进入结束态时不再重播
            if (!_audioHealthy || _audio is null || _playIssued || _mediaFinished || _completed)
            {
                return;
            }

            _playIssued = true;
            _audio.Play();
        }
    }

    private void OnAudioEnded(object? sender, EventArgs e)
    {
        lock (_syncRoot)
        {
            HandleMediaEnded();
        }
    }

    private void OnAudioFailed(object? sender, string message)
    {
        lock (_syncRoot)
        {
            try
            {
                _audio?.Stop();
            }
            catch (Exception)
            {
                // 停止出错的音频后端失败不应影响降级
            }

            Degrade();
        }
    }

    private void HandleMediaEnded()
    {
        if (_mediaFinished)
        {
            // 重复的「播放到结尾」会重写结束时刻，导致时间轴回跳
            return;
        }

        _mediaFinished = true;

        var duration = _audio?.DurationSeconds ?? 0.0;
        var position = _audio?.PositionSeconds ?? 0.0;
        _mediaDurationSeconds = duration > 0 ? duration : position;
        _mediaFinishRealSeconds = _clock.NowSeconds;
    }

    private void Degrade()
    {
        _audioHealthy = false;
        _degradedAtSeconds = _elapsedSeconds;
        _degradedRealSeconds = _clock.NowSeconds;
    }

    private void BuildTickRanges(IReadOnlyList<NoteInfo> notes)
    {
        _ranges.Clear();

        var currentTick = 0;
        foreach (var note in notes)
        {
            var length = Math.Max(note.Length, 1);
            _ranges.Add(new NoteTickRange(currentTick, currentTick + length, note));
            currentTick += length;
        }

        TotalTicks = currentTick;
    }

    /// <summary>音符的 tick 区间。</summary>
    /// <param name="Start">起始 tick（含）。</param>
    /// <param name="End">结束 tick（不含）。</param>
    /// <param name="Note">对应音符。</param>
    private readonly record struct NoteTickRange(int Start, int End, NoteInfo Note)
    {
        /// <summary>指定 tick 是否落在本区间内。</summary>
        /// <param name="tick">待判断的 tick。</param>
        /// <returns>落在区间内返回 <see langword="true"/>。</returns>
        internal bool Contains(double tick) => tick >= Start && tick < End;
    }
}
