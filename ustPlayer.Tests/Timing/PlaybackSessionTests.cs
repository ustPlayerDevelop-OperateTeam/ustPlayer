using System;
using System.Collections.Generic;

using UstPlayer.Models;
using UstPlayer.Timing;

using Xunit;

namespace UstPlayer.Tests.Timing;

/// <summary>
/// 播放时序状态机的验收测试。
/// </summary>
/// <remarks>
/// 这组用例是 Spike 1 的验收项：**必须复现 1.1.x 踩坑后固化下来的四个行为**——
/// 就绪只播一次、播完锚点只记一次、看门狗超限降级、降级瞬间时间轴不跳变。
/// 判定依据见迁移计划 Phase 2 与 <c>docs/adr-0001-renderer-strategy.md</c>。
/// </remarks>
public class PlaybackSessionTests
{
    private const int TicksPerNote = 480;

    /// <summary>120 BPM 下的每秒 tick 数（1 秒 = 960 tick）。</summary>
    private const double TicksPerSecond = 120.0 * TicksPerNote / 60.0;

    // ===================== 就绪只播一次 =====================

    /// <summary>媒体就绪后开始播放，且重复就绪事件不会再次播放。</summary>
    [Fact]
    public void 就绪后只播放一次()
    {
        var (session, audio, _) = CreateSession(noteCount: 2);

        audio.RaiseReady();
        Assert.Equal(1, audio.PlayCount);

        audio.RaiseReady();
        Assert.Equal(1, audio.PlayCount);

        session.Advance();
    }

    /// <summary>已播完后再收到就绪事件不得重播（1.1.x 的严重 bug 之一）。</summary>
    [Fact]
    public void 播完后不再重播()
    {
        var (session, audio, _) = CreateSession(noteCount: 1);

        session.StartOrResume();
        audio.RaiseReady();
        Assert.Equal(1, audio.PlayCount);

        audio.DurationSeconds = 1.0;
        audio.RaiseEnded();

        // 部分后端播完后会把状态回落为「已加载」并再次触发就绪
        audio.RaiseReady();
        Assert.Equal(1, audio.PlayCount);

        session.Advance();
    }

    /// <summary>已进入结束态后不再重播。</summary>
    [Fact]
    public void 进入结束态后不再重播()
    {
        var (session, audio, clock) = CreateSession(noteCount: 1);

        session.StartOrResume();
        audio.DurationSeconds = 0.5;
        audio.RaiseReady();
        audio.PositionOverride = 0.5;
        audio.RaiseEnded();

        clock.Advance(1.0);
        var state = session.Advance();
        Assert.Equal(PlaybackEndStep.End, state.Step);
        Assert.True(state.IsPlayerFinished);

        audio.RaiseReady();
        Assert.Equal(1, audio.PlayCount);
    }

    // ===================== 时间轴锚定 =====================

    /// <summary>
    /// 调用方忘记 <see cref="PlaybackSession.StartOrResume"/> 时，绝不能把「开机以来的秒数」
    /// 当成播放位置——真实时钟以系统启动为零点，那样第一帧就会被判定为已播完。
    /// </summary>
    /// <remarks>
    /// <b>回归测试</b>：这个 bug 曾被假时钟掩盖——<see cref="FakeClock"/> 默认从 0 开始，
    /// 恰好等价于「已正确锚定」，于是所有用默认假时钟的测试都是绿的，
    /// 而真实程序里播放会瞬间结束。因此这里显式用非零起点。
    /// </remarks>
    [Fact]
    public void 未显式锚定时也不把开机时间当播放位置()
    {
        var clock = new FakeClock(3600.0);
        var options = CreateOptions(noteCount: 4);
        var session = new PlaybackSession(options, clock, audio: null);

        // 刻意不调用 StartOrResume

        clock.Advance(1.0 / 60.0);
        var state = session.Advance();

        Assert.Equal(PlaybackEndStep.Continue, state.Step);
        Assert.False(state.IsPlayerFinished);
        Assert.True(state.ElapsedSeconds < 1.0, $"播放位置不该是 {state.ElapsedSeconds} 秒");
    }

    // ===================== 播完锚点 =====================

    /// <summary>重复的「播放到结尾」不得改写结束时刻（否则时间轴回跳）。</summary>
    [Fact]
    public void 重复的播完事件只记一次锚点()
    {
        var (session, audio, clock) = CreateSession(noteCount: 1);

        session.StartOrResume();
        audio.DurationSeconds = 0.5;
        audio.RaiseReady();

        // 时长已知，播完锚点取时长（0.5）
        audio.RaiseEnded();

        // 第一次播完锚点在 now=0；推进 3 秒后若被重复事件改写，位置会退回约 0.5
        clock.Advance(3.0);
        audio.RaiseEnded();

        var state = session.Advance();

        // 位置应为 时长(0.5) + 距播完的墙钟增量(3.0)，而非被重置
        Assert.True(
            state.ElapsedSeconds >= 3.4,
            $"重复的播完事件改写了结束时刻，位置被回退为 {state.ElapsedSeconds:F2}");
    }

    /// <summary>时长未知时退回以当前位置作为播完锚点。</summary>
    [Fact]
    public void 时长未知时以当前位置为锚点()
    {
        var (session, audio, clock) = CreateSession(noteCount: 1);

        session.StartOrResume();
        audio.RaiseReady();

        audio.DurationSeconds = 0.0; // 未知
        audio.PositionOverride = 2.5;
        audio.RaiseEnded();

        clock.Advance(1.0);
        var state = session.Advance();

        Assert.Equal(3.5, state.ElapsedSeconds, precision: 3);
    }

    // ===================== 看门狗 =====================

    /// <summary>播放已发出但后端始终没开始播 → 宽限一次后降级为纯可视化。</summary>
    /// <remarks>
    /// 「已加载 + 未播放」有两种来源，本测试覆盖的是**失败**那一种：
    /// 会话已经发出过播放（收到过 Ready），后端却一直没有真正开始。
    /// 另一种是「Ready 事件发生在会话订阅之前」，那时 <c>_playIssued</c> 还是 false，
    /// 会话应当**补播**而不是降级（见 <c>PlayerFrameCompositorTests.订阅前已就绪的音频应被补播</c>）。
    /// </remarks>
    [Fact]
    public void 播放发出后仍未播放时降级()
    {
        var (session, audio, _) = CreateSession(noteCount: 4);

        // 先让会话发出播放（收到 Ready）
        audio.RaiseReady();

        // 后端却始终没有真正开始播
        audio.IsLoaded = true;
        audio.IsPlayingOverride = false;

        var scheduled = new List<TimeSpan>();

        // 第一次：给宽限（Play() 是异步的，刚发出时 IsPlaying 仍为 false）
        session.CheckAudioReady(scheduled.Add);
        Assert.True(session.IsAudioHealthy, "刚发出播放时不该立刻降级");
        Assert.Single(scheduled);

        // 第二次：宽限用完仍未播放 → 降级
        session.CheckAudioReady(scheduled.Add);
        Assert.False(session.IsAudioHealthy, "宽限用完后仍未播放应当降级");
    }

    /// <summary>媒体无效 → 立即降级。</summary>
    [Fact]
    public void 媒体无效时降级()
    {
        var (session, audio, _) = CreateSession(noteCount: 4);

        audio.IsInvalid = true;

        var scheduled = new List<TimeSpan>();
        session.CheckAudioReady(scheduled.Add);

        // 降级分支不应再调度重试
        Assert.Empty(scheduled);

        Assert.False(session.IsAudioHealthy);
    }

    /// <summary>仍在加载 → 调度重试；连续超限后强制降级，避免永远停在 0:00。</summary>
    [Fact]
    public void 仍在加载时重试并在超限后降级()
    {
        var (session, audio, _) = CreateSession(noteCount: 4, audioReadyTimeoutChecks: 3);

        audio.IsLoaded = false;
        audio.IsLoading = true;

        var scheduled = new List<TimeSpan>();

        // 第 1、2 次：调度重试，仍健康
        session.CheckAudioReady(scheduled.Add);
        Assert.True(session.IsAudioHealthy);
        session.CheckAudioReady(scheduled.Add);
        Assert.True(session.IsAudioHealthy);

        // 第 3 次：达到上限，降级
        session.CheckAudioReady(scheduled.Add);
        Assert.False(session.IsAudioHealthy);

        Assert.Equal(2, scheduled.Count);
        Assert.All(scheduled, delay => Assert.Equal(TimeSpan.FromMilliseconds(3000), delay));
    }

    /// <summary>后端停在「已到结尾」但事件未发出 → 补记锚点而非降级（该判断必须在「已加载」之前）。</summary>
    [Fact]
    public void 后端已到结尾但事件未发出时补记锚点且不降级()
    {
        var (session, audio, clock) = CreateSession(noteCount: 1);

        session.StartOrResume();

        // 关键：同时满足「已到结尾」与「已加载」两个互斥状态，检验判定顺序。
        // 若把「已到结尾」判断嵌在「已加载」内层，该分支永远不会执行（1.1.x 的教训）。
        audio.IsFinishedOverride = true;
        audio.IsLoaded = true;
        audio.IsPlayingOverride = false;
        audio.DurationSeconds = 1.0;

        var scheduled = new List<TimeSpan>();
        session.CheckAudioReady(scheduled.Add);

        // 补记锚点分支不应再调度重试
        Assert.Empty(scheduled);

        Assert.True(session.IsAudioHealthy);

        clock.Advance(0.5);
        var state = session.Advance();

        // 已进入结束态（音频播完 + 内容结束），位置 = 时长 + 墙钟增量
        Assert.Equal(PlaybackEndStep.End, state.Step);
        Assert.Equal(1.5, state.ElapsedSeconds, precision: 3);
    }

    /// <summary>已播完时看门狗不得降级（后端回落为「已加载」属正常）。</summary>
    [Fact]
    public void 已播完时看门狗不降级()
    {
        var (session, audio, _) = CreateSession(noteCount: 1);

        session.StartOrResume();
        audio.DurationSeconds = 0.2;
        audio.RaiseReady();
        audio.RaiseEnded();

        // 播完后后端回落为「已加载但未播放」——正是曾误判降级的组合
        audio.IsLoaded = true;
        audio.IsPlayingOverride = false;

        var scheduled = new List<TimeSpan>();
        session.CheckAudioReady(scheduled.Add);

        // 已播完不应调度重试
        Assert.Empty(scheduled);

        Assert.True(session.IsAudioHealthy);
        session.Advance();
    }

    // ===================== 降级重锚定 =====================

    /// <summary>
    /// 降级瞬间以当前位置为新零点：时间轴连续，不跳变。
    /// 1.1.x 的 bug 是降级后从 0:00 重走墙钟，画面会从 0:00 直接跳到约 9 秒处。
    /// </summary>
    [Fact]
    public void 降级瞬间重锚定使时间轴不跳变()
    {
        var (session, audio, clock) = CreateSession(noteCount: 100);

        // 先播到 10 秒（时长足够长，避免被判定为已到结尾）
        session.StartOrResume();
        audio.DurationSeconds = 100.0;
        audio.RaiseReady();
        clock.Advance(10.0);

        var before = session.Advance();
        Assert.Equal(10.0, before.ElapsedSeconds, precision: 3);

        // 出错 → 降级
        audio.RaiseFailed("模拟解码失败");
        Assert.False(session.IsAudioHealthy);

        // 降级瞬间位置应与降级前一致（连续，不跳回 0）
        var atDegrade = session.Advance();
        Assert.Equal(10.0, atDegrade.ElapsedSeconds, precision: 3);

        // 之后按墙钟继续前进
        clock.Advance(2.0);
        var after = session.Advance();
        Assert.Equal(12.0, after.ElapsedSeconds, precision: 3);
    }

    /// <summary>无音频（后端为 null）时退化为纯墙钟计时。</summary>
    [Fact]
    public void 无音频时使用墙钟计时()
    {
        var clock = new FakeClock();
        var session = new PlaybackSession(CreateOptions(noteCount: 100), clock, audio: null);
        session.StartOrResume();

        clock.Advance(3.0);
        var state = session.Advance();

        Assert.Equal(3.0, state.ElapsedSeconds, precision: 3);
        Assert.False(session.IsAudioHealthy);
    }

    /// <summary>时间轴锚点只建立一次：再次调用不会重置零点（对应窗口最小化 / 恢复）。</summary>
    [Fact]
    public void 再次启动不重置时间轴零点()
    {
        var clock = new FakeClock();
        var session = new PlaybackSession(CreateOptions(noteCount: 100), clock, audio: null);

        session.StartOrResume();
        clock.Advance(5.0);

        session.StartOrResume(); // 模拟窗口再次显示
        var state = session.Advance();

        Assert.Equal(5.0, state.ElapsedSeconds, precision: 3);
    }

    // ===================== 结束边界 =====================

    /// <summary>音符内容结束但音频未播完 → 显示空拍文字（不提前结束、不掐断伴奏）。</summary>
    [Fact]
    public void 内容结束而音频未播完时进入空拍态()
    {
        var (session, audio, clock) = CreateSession(noteCount: 1);

        session.StartOrResume();
        audio.RaiseReady();
        audio.DurationSeconds = 10.0;

        // 内容共 480 tick，120BPM 下为 0.5 秒；推进 1 秒已越过内容
        clock.Advance(1.0);
        var state = session.Advance();

        Assert.Equal(PlaybackEndStep.Silent, state.Step);
        Assert.Equal("R", state.LyricText);
        Assert.False(state.IsPlayerFinished);
        Assert.Equal(0, audio.StopCount);
    }

    /// <summary>音频播完且内容结束 → 显示结束文字、停止音频、标记播放完成。</summary>
    [Fact]
    public void 音频播完且内容结束进入结束态()
    {
        var (session, audio, clock) = CreateSession(noteCount: 1);

        session.StartOrResume();
        audio.DurationSeconds = 0.5;
        audio.RaiseReady();
        audio.PositionOverride = 0.5;
        audio.RaiseEnded();

        clock.Advance(1.0);
        var state = session.Advance();

        Assert.Equal(PlaybackEndStep.End, state.Step);
        Assert.Equal("END", state.LyricText);
        Assert.True(state.IsPlayerFinished);
        Assert.Equal(1, audio.StopCount);
    }

    /// <summary>自定义空拍与结束文字生效。</summary>
    [Fact]
    public void 自定义空拍与结束文字生效()
    {
        var options = CreateOptions(noteCount: 1) with
        {
            Text = new PlaybackTextOptions(
                SilentDisplayMode.Custom, "（空拍）",
                EndDisplayMode.Custom, "（完）",
                PitchPlaceholderMode.None, string.Empty),
        };

        var clock = new FakeClock();
        var audio = new FakeAudioBackend(clock) { DurationSeconds = 10.0 };
        var session = new PlaybackSession(options, clock, audio);
        session.StartOrResume();
        audio.RaiseReady();

        // 内容结束但音频未播完 → 空拍自定义文字
        clock.Advance(1.0);
        Assert.Equal("（空拍）", session.Advance().LyricText);

        // 音频播完 → 结束自定义文字
        audio.RaiseEnded();
        Assert.Equal("（完）", session.Advance().LyricText);
    }

    // ===================== 歌字规则 =====================

    /// <summary>延音符保留上一个有效歌词；延音符仍显示音名。</summary>
    [Fact]
    public void 延音符保留上一个有效歌词()
    {
        var (session, _, clock) = CreateSessionWith(
        [
            Note("R", 60),
            Note("do", 62),
            Note("-", 64),
        ]);

        session.StartOrResume();

        // 第 1 个音符是休止符 → 空拍文字
        Assert.Equal("R", session.Advance().LyricText);

        // 第 2 个音符是 do
        Assert.Equal("do", AdvanceTicks(session, clock, TicksPerNote).LyricText);

        // 第 3 个音符是延音 → 保留 do，并显示音名 E4（MIDI 64）
        var state = AdvanceTicks(session, clock, TicksPerNote);
        Assert.Equal("do", state.LyricText);
        Assert.Equal("E4", state.NoteName);
    }

    /// <summary>休止符不显示音名。</summary>
    [Fact]
    public void 休止符不显示音名()
    {
        var (session, _, _) = CreateSessionWith([Note("R", 60)]);
        session.StartOrResume();

        var state = session.Advance();

        Assert.Equal("R", state.LyricText);
        Assert.Empty(state.NoteName);
    }

    /// <summary>普通音符同时给出歌字与音名。</summary>
    [Fact]
    public void 普通音符给出歌字与音名()
    {
        var (session, _, _) = CreateSessionWith([Note("do", 60)]);
        session.StartOrResume();

        var state = session.Advance();

        Assert.Equal("do", state.LyricText);
        Assert.Equal("C4", state.NoteName);
    }

    /// <summary>非法速度回退 120 BPM（否则 0 会让时间轴永远停在第 0 tick）。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void 非法速度回退默认值(double tempo)
    {
        var options = CreateOptions(noteCount: 2) with { Tempo = tempo };
        var session = new PlaybackSession(options, new FakeClock(), audio: null);

        Assert.Equal(120.0, session.Tempo);
    }

    /// <summary>音符长度下限为 1 tick（与渲染器 timing.rs 一致）。</summary>
    [Fact]
    public void 零长度音符按一_tick_计算()
    {
        var (session, _, _) = CreateSessionWith(
        [
            Note("a", 60, length: 0),
            Note("b", 62, length: 480),
        ]);

        Assert.Equal(481, session.TotalTicks);
    }

    // ===================== 辅助 =====================

    private static (PlaybackSession Session, FakeAudioBackend Audio, FakeClock Clock) CreateSession(
        int noteCount,
        int audioReadyTimeoutChecks = 3)
    {
        var clock = new FakeClock();
        var audio = new FakeAudioBackend(clock);

        var options = CreateOptions(noteCount) with
        {
            AudioReadyTimeoutChecks = audioReadyTimeoutChecks,
        };

        return (new PlaybackSession(options, clock, audio), audio, clock);
    }

    private static (PlaybackSession Session, FakeAudioBackend Audio, FakeClock Clock) CreateSessionWith(
        IReadOnlyList<NoteInfo> notes)
    {
        var clock = new FakeClock();
        var audio = new FakeAudioBackend(clock);

        // 歌字 / 结束文字的用例只关心「按 tick 找音符 + 文字规则」，与音频状态机无关。
        // 因此不挂音频（audio: null）→ 会话走墙钟计时，时钟推进多少 tick 就是多少，
        // 避免音频位置覆盖墙钟而让用例的意图变得隐晦。
        var options = new PlaybackSessionOptions(120.0, notes, DefaultText());

        return (new PlaybackSession(options, clock, audio: null), audio, clock);
    }

    private static PlaybackSessionOptions CreateOptions(int noteCount)
    {
        var notes = new List<NoteInfo>(noteCount);
        for (var i = 0; i < noteCount; i++)
        {
            notes.Add(Note("la", 60 + (i % 12)));
        }

        return new PlaybackSessionOptions(120.0, notes, DefaultText());
    }

    private static PlaybackTextOptions DefaultText() =>
        new(SilentDisplayMode.Rest, string.Empty,
            EndDisplayMode.End, string.Empty,
            PitchPlaceholderMode.None, string.Empty);

    private static NoteInfo Note(string lyric, int noteNumber, int length = TicksPerNote) =>
        new()
        {
            Index = "0000",
            Length = length,
            Lyric = lyric,
            NoteNumber = noteNumber,
        };

    /// <summary>用假时钟把会话推进指定的 tick 数后取一次状态。</summary>
    private static PlaybackState AdvanceTicks(PlaybackSession session, FakeClock clock, int ticks)
    {
        clock.Advance(ticks / TicksPerSecond);
        return session.Advance();
    }
}
