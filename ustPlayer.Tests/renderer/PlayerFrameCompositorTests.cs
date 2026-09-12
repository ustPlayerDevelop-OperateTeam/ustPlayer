using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Avalonia.Headless.XUnit;

using UstPlayer.Interop;
using UstPlayer.Models;
using UstPlayer.Platform;
using UstPlayer.Settings;
using UstPlayer.Tests.Timing;
using UstPlayer.Timing;
using UstPlayer.Views;

using Xunit;
using Xunit.Abstractions;

namespace UstPlayer.Tests.Renderer;

/// <summary>
/// 播放器帧合成器的测试（需要渲染器原生库）。
/// </summary>
/// <remarks>
/// 验证 Phase 4 的核心链路：时序 → 渲染器出帧 → 位图。Spike 0b 已确认**耗时**可行，
/// 本类确认**正确性与生命周期**：尺寸决策、降级路径能跑、帧循环可推进到结束、
/// 位图复用、释放后不可再用。
/// </remarks>
[Collection(NativeRendererCollection.Name)]
public class PlayerFrameCompositorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ITestOutputHelper _output;

    /// <summary>建立临时目录。</summary>
    /// <param name="output">测试输出。</param>
    public PlayerFrameCompositorTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"compositor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败无关紧要
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>原生渲染器缺失时给出可操作提示。</summary>
    private static void RequireNativeRenderer()
    {
        Assert.True(
            UplRenderLoader.IsAvailable(),
            "未找到渲染器原生库。请运行 build/sync-native-assets.ps1，"
            + "或用 UPLRENDER_RELEASE_DIR 指定 uPlRender 的 target/release 目录后重试。");
    }

    // ===================== 渲染尺寸决策 =====================

    /// <summary>窗口尺寸在上限内时按窗口尺寸渲染（字号才能与小窗口匹配）。</summary>
    [Theory]
    [InlineData(800, 600, 800, 600)]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(1280, 720, 1280, 720)]
    public void 窗口在上限内时按窗口尺寸渲染(int viewWidth, int viewHeight, int expectedWidth, int expectedHeight)
    {
        var (width, height) = PlayerFrameCompositor.ResolveRenderSize(viewWidth, viewHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    /// <summary>超过上限时 clamp 到 1920×1080（4K 实时渲染达不到 60fps，见 ADR 0001）。</summary>
    [Theory]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(2560, 1440, 1920, 1080)]
    [InlineData(3840, 1080, 1920, 1080)]
    [InlineData(1920, 2160, 1920, 1080)]
    public void 超过上限时_clamp(int viewWidth, int viewHeight, int expectedWidth, int expectedHeight)
    {
        var (width, height) = PlayerFrameCompositor.ResolveRenderSize(viewWidth, viewHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    /// <summary>非法（零 / 负）尺寸至少取 1，避免创建零尺寸位图。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-100, -100)]
    [InlineData(0, 720)]
    public void 非法尺寸至少取一(int viewWidth, int viewHeight)
    {
        var (width, height) = PlayerFrameCompositor.ResolveRenderSize(viewWidth, viewHeight);

        Assert.True(width >= 1);
        Assert.True(height >= 1);
    }

    // ===================== 端到端帧循环 =====================

    /// <summary>无音频（降级路径）时也能逐帧渲染到结束，且位图被真实写入。</summary>
    [AvaloniaFact]
    public void 无音频时能逐帧渲染到结束()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();
        var parameters = BuildParameters(noteCount: 4);

        // audio: null → 纯墙钟计时（无伴奏或音频不可用时的路径）
        using var compositor = CreateCompositor(parameters, clock, audio: null, 640, 360);

        Assert.Equal(640, compositor.RenderWidth);
        Assert.Equal(360, compositor.RenderHeight);

        ComposedFrame last = default;

        // 内容 4 个 480tick 音符 @120BPM = 2 秒；渲染 60 帧（约 1 秒）后应仍在进行中
        for (var i = 0; i < 60; i++)
        {
            clock.Advance(1.0 / 60.0);
            last = compositor.Advance(640, 360);
        }

        Assert.Equal(PlaybackEndStep.Continue, last.State.Step);
        Assert.False(last.State.IsPlayerFinished);

        // 再推进到超过内容长度 → 应进入结束态
        clock.Advance(2.0);
        var finished = compositor.Advance(640, 360);

        Assert.Equal(PlaybackEndStep.End, finished.State.Step);
        Assert.True(finished.State.IsPlayerFinished);
    }

    /// <summary>每帧复用同一个位图实例（避免每帧分配）。</summary>
    [AvaloniaFact]
    public void 每帧复用同一位图实例()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();
        using var compositor = CreateCompositor(BuildParameters(2), clock, audio: null, 320, 180);

        var first = compositor.Advance(320, 180);
        clock.Advance(0.1);
        var second = compositor.Advance(320, 180);

        Assert.Same(first.Bitmap, second.Bitmap);
    }

    /// <summary>
    /// 真实时钟（非零起点）下第一帧不能判定播放已结束。
    /// </summary>
    /// <remarks>
    /// <b>回归测试</b>：真实时钟从系统启动算起（<c>Environment.TickCount64</c>，非零），
    /// 而假时钟默认从 0 开始——从 0 开始恰好等同于「时间轴已正确锚定」，
    /// 因此漏掉 <see cref="PlayerFrameCompositor.Start"/> / 锚定逻辑时，
    /// 用默认假时钟的测试会**全绿**，而真实程序第一帧就显示结束文字、播放瞬间结束。
    /// 这里刻意用非零起点复现真实条件。
    /// </remarks>
    [AvaloniaFact]
    public void 时钟非零起点时第一帧不应结束()
    {
        RequireNativeRenderer();

        // 内容 4 个 480tick 音符 @120BPM = 2 秒；时钟起点模拟「已开机若干小时」
        var clock = new FakeClock(3600.0);

        using var compositor = CreateCompositor(BuildParameters(4), clock, audio: null, 640, 360);

        clock.Advance(1.0 / 60.0);
        var frame = compositor.Advance(640, 360);

        Assert.Equal(PlaybackEndStep.Continue, frame.State.Step);
        Assert.False(frame.State.IsPlayerFinished);
        Assert.True(frame.State.ElapsedSeconds < 1.0, $"经过一帧后播放位置不该是 {frame.State.ElapsedSeconds} 秒");
    }

    /// <summary>创建时不传尺寸也能工作：首次推进才分配（全屏窗口 Show 之前拿不到客户区尺寸）。</summary>
    [AvaloniaFact]
    public void 尺寸后到时首次推进才分配()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();

        // 不传尺寸 → 创建后既无位图也无渲染尺寸
        using var compositor = CreateCompositor(BuildParameters(2), clock, audio: null, null, null);

        Assert.Null(compositor.Bitmap);
        Assert.Equal(0, compositor.RenderWidth);
        Assert.Equal(0, compositor.RenderHeight);

        var frame = compositor.Advance(400, 225);

        Assert.NotNull(compositor.Bitmap);
        Assert.Equal(400, compositor.RenderWidth);
        Assert.Equal(225, compositor.RenderHeight);
        Assert.Same(compositor.Bitmap, frame.Bitmap);
    }

    /// <summary>窗口尺寸变化时重建位图（字号按画布尺寸算，尺寸不同必须重新配置渲染器）。</summary>
    [AvaloniaFact]
    public void 尺寸变化时重建位图()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();
        using var compositor = CreateCompositor(BuildParameters(2), clock, audio: null, 320, 180);

        var small = compositor.Advance(320, 180);

        clock.Advance(0.1);
        var large = compositor.Advance(800, 450);

        Assert.NotSame(small.Bitmap, large.Bitmap);
        Assert.Equal(800, compositor.RenderWidth);
        Assert.Equal(450, compositor.RenderHeight);

        // 尺寸不变时继续复用（不能每帧重建）
        clock.Advance(0.1);
        var again = compositor.Advance(800, 450);

        Assert.Same(large.Bitmap, again.Bitmap);
    }

    /// <summary>超过上限的窗口尺寸只按上限分配位图（4K 实时渲染达不到 60fps，见 ADR 0001）。</summary>
    [AvaloniaFact]
    public void 超过上限时按上限分配位图()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();

        // 传 4K 尺寸也应收敛到 1920×1080
        using var compositor = CreateCompositor(BuildParameters(2), clock, audio: null, 3840, 2160);

        Assert.Equal(PlayerFrameCompositor.MaxRenderWidth, compositor.RenderWidth);
        Assert.Equal(PlayerFrameCompositor.MaxRenderHeight, compositor.RenderHeight);
    }

    /// <summary>歌词行会被传给渲染器（不抛异常且不改变流程）。</summary>
    [AvaloniaFact]
    public void 带歌词时能正常渲染()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();
        var lrcLines = LrcParser.Parse("[00:00.50]第一句\n[00:01.50]第二句");

        using var compositor = CreateCompositor(
            BuildParameters(4), clock, audio: null, 480, 270, lrcLines);

        clock.Advance(0.6);
        var frame = compositor.Advance(480, 270);

        Assert.Equal(PlaybackEndStep.Continue, frame.State.Step);
    }

    /// <summary>音频健康时会话按音频位置驱动（这里用可编程假后端验证接线正确）。</summary>
    [AvaloniaFact]
    public void 音频健康时按音频位置驱动()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();
        var audio = new FakeAudioBackend(clock) { DurationSeconds = 100.0 };

        using var compositor = CreateCompositor(BuildParameters(100), clock, audio, 320, 180);

        audio.RaiseReady();
        clock.Advance(1.0);
        var frame = compositor.Advance(320, 180);

        Assert.True(frame.State.ElapsedSeconds >= 1.0);
        Assert.Equal(1, audio.PlayCount);
    }

    /// <summary>释放后再次推进应抛 <see cref="ObjectDisposedException"/>，而不是访问已释放的句柄。</summary>
    [AvaloniaFact]
    public void 释放后不可再推进()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();
        var compositor = CreateCompositor(BuildParameters(2), clock, audio: null, 160, 90);

        compositor.Advance(160, 90);
        compositor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => compositor.Advance(160, 90));
    }

    /// <summary>重复释放是安全的。</summary>
    [AvaloniaFact]
    public void 重复释放安全()
    {
        RequireNativeRenderer();

        var clock = new FakeClock();
        var compositor = CreateCompositor(BuildParameters(2), clock, audio: null, 160, 90);

        compositor.Dispose();
        compositor.Dispose();
    }

    // ===================== 音频看门狗接线（回归） =====================

    /// <summary>
    /// 音频看门狗必须经合成器被驱动：后端「已加载但没开始播」时要把它降级掉，时间轴才能走。
    /// </summary>
    /// <remarks>
    /// <b>回归测试</b>：<see cref="PlaybackSession.CheckAudioReady"/> 曾经**只在测试里被调用**，
    /// 生产代码（合成器 / 播放窗口）漏了驱动，后果是音频一次都没播过、
    /// 时间轴停在「等音频就绪」的判断上——而所有单元测试全绿。
    /// 因此这里刻意走 <see cref="PlayerFrameCompositor.CheckAudioReady"/> 这条路，
    /// 而不是直接调会话：漏接线的缺陷只有这样才挡得住。
    /// </remarks>
    [AvaloniaFact]
    public void 看门狗经合成器驱动_已加载未播放时降级()
    {
        RequireNativeRenderer();

        // 非零起点：与真实时钟一致，避免「恰好已锚定」掩盖问题
        var clock = new FakeClock(3600.0);
        var audio = new FakeAudioBackend(clock)
        {
            DurationSeconds = 10.0,
            IsLoaded = true,
            IsPlayingOverride = false,
        };

        using var compositor = CreateCompositor(BuildParameters(4), clock, audio, 320, 180);

        var scheduler = new RecordingScheduler();

        // 先推一帧：生产里窗口是「Start() → 帧定时器 → 看门狗定时器」的顺序，
        // 时间轴必须先锚定，降级点才有意义（本测试刻意照搬这个顺序）
        compositor.Advance(320, 180);

        // 第一次检查就应判定「已加载但没在播」→ 降级
        var retry = compositor.CheckAudioReady(scheduler);

        var afterCheck = compositor.Advance(320, 180);

        Assert.False(retry, "已加载却未播放应当直接降级，不该再等下一次检查");
        Assert.Empty(scheduler.Requested);

        // 降级后时间轴按墙钟走：这就是「看得见的画面」，
        // 未降级时它会永远停在起点
        clock.Advance(0.5);
        var frame = compositor.Advance(320, 180);

        Assert.True(
            frame.State.ElapsedSeconds >= 0.5,
            $"降级后时间轴应按墙钟前进，实际 {frame.State.ElapsedSeconds} 秒"
            + $"（检查后一帧 {afterCheck.State.ElapsedSeconds} 秒，"
            + $"播放位置 {audio.PositionSeconds} 秒，PlayCount={audio.PlayCount}）");
    }

    /// <summary>仍在加载时要预约下一次检查，且间隔取会话配置（宿主据此设定时器）。</summary>
    [AvaloniaFact]
    public void 看门狗仍在加载时预约下一次检查()
    {
        RequireNativeRenderer();

        var clock = new FakeClock(3600.0);
        var audio = new FakeAudioBackend(clock)
        {
            DurationSeconds = 10.0,
            IsLoaded = false,
            IsLoading = true,
        };

        using var compositor = CreateCompositor(BuildParameters(4), clock, audio, 320, 180);

        var scheduler = new RecordingScheduler();
        var retry = compositor.CheckAudioReady(scheduler);

        Assert.True(retry, "仍在加载时应预约下一次检查");
        Assert.Single(scheduler.Requested);
        Assert.Equal(compositor.WatchdogInterval, scheduler.Requested[0]);
        Assert.True(compositor.WatchdogInterval > TimeSpan.Zero, "看门狗间隔必须是正值");
    }

    /// <summary>没有音频会话时驱动看门狗是安全的空操作（纯墙钟计时的路径）。</summary>
    [AvaloniaFact]
    public void 无音频时驱动看门狗安全()
    {
        RequireNativeRenderer();

        var clock = new FakeClock(3600.0);
        using var compositor = CreateCompositor(BuildParameters(2), clock, audio: null, 160, 90);

        var scheduler = new RecordingScheduler();

        Assert.False(compositor.CheckAudioReady(scheduler));
        Assert.Empty(scheduler.Requested);
    }

    /// <summary>
    /// 后端在会话订阅之前就已就绪时，看门狗必须补上「开始播放」，而不是降级掉它。
    /// </summary>
    /// <remarks>
    /// <b>回归测试（真实 bug）</b>：后端由 <c>AudioBackendFactory</c> 创建时即开始解析，
    /// 小伴奏几十毫秒就绪；而会话要等渲染器初始化完（几百毫秒）才订阅 <c>Ready</c>——
    /// 事件早已发过，会话永远收不到。后果是**音频一次都不播**：
    /// 后端「已加载 + 时长正确」但「正在播放 = false」，于是时间轴既不前进也不降级，
    /// 播放器看起来像没做出来。
    ///
    /// 这里刻意**不**触发 <see cref="FakeAudioBackend.RaiseReady"/>，只把后端置成已加载，
    /// 复现的正是「就绪事件发生在订阅之前」这一时序。
    /// </remarks>
    [AvaloniaFact]
    public void 订阅前已就绪的音频应被补播()
    {
        RequireNativeRenderer();

        var clock = new FakeClock(3600.0);

        // IsLoaded = true 表示「后端已经就绪」，但会话从未收到过 Ready 事件
        var audio = new FakeAudioBackend(clock)
        {
            DurationSeconds = 10.0,
            IsLoaded = true,
        };

        using var compositor = CreateCompositor(BuildParameters(4), clock, audio, 320, 180);

        compositor.Advance(320, 180);

        var scheduler = new RecordingScheduler();
        var retry = compositor.CheckAudioReady(scheduler);

        Assert.Equal(1, audio.PlayCount);
        Assert.False(retry, "已就绪的音频补播之后不该再等下一次检查");
        Assert.True(
            audio.IsPlaying,
            "补播之后后端应处于播放状态（否则时间轴仍不会前进）");

        // 时间轴应由音频位置驱动
        clock.Advance(1.0);
        var frame = compositor.Advance(320, 180);

        Assert.True(
            frame.State.ElapsedSeconds >= 1.0,
            $"音频在驱动时时间轴应按音频位置前进，实际 {frame.State.ElapsedSeconds} 秒");
    }

    /// <summary>记录被预约的延迟（不真的等待），用于断言调度接线。</summary>
    private sealed class RecordingScheduler : IPlaybackScheduler
    {
        /// <summary>每次预约的延迟。</summary>
        public List<TimeSpan> Requested { get; } = [];

        /// <inheritdoc />
        public void ScheduleOnce(TimeSpan delay, Action callback)
        {
            Requested.Add(delay);

            // 刻意不执行回调：回调会再次调用 CheckAudioReady 形成递归，
            // 这里只验证「有没有预约、间隔是多少」
        }
    }

    // ===================== 辅助 =====================

    private PlayerFrameCompositor CreateCompositor(
        PlayerLaunchParams parameters,
        FakeClock clock,
        IAudioBackend? audio,
        int? viewWidth,
        int? viewHeight,
        IReadOnlyList<LrcLine>? lrcLines = null)
    {
        var settings = new SettingsManager(Path.Combine(_tempDirectory, $"Settings-{Guid.NewGuid():N}.json"));

        return PlayerFrameCompositor.Create(
            parameters,
            settings,
            lrcLines ?? [],
            clock,
            audio,
            viewWidth,
            viewHeight);
    }

    private static PlayerLaunchParams BuildParameters(int noteCount)
    {
        var ust = new UstInfo { Version = "UST Version1.2", Tempo = 120.0, Tracks = 1 };

        for (var i = 0; i < noteCount; i++)
        {
            ust.Notes.Add(new NoteInfo
            {
                Index = $"{i:0000}",
                Length = 480,
                Lyric = i % 8 == 7 ? "R" : "あ",
                NoteNumber = 60 + (i % 12),
                PitchBend = i % 8 == 7 ? [] : [0, 60, 120, 60, -60],
            });
        }

        return new PlayerLaunchParams
        {
            Ust = ust,
            Show = new ShowConfig { CurveShow = true },
            Project = new ProjectInfo { SongName = "合成器测试", SongAuthor = "作者", UstAuthor = "调音师" },
            Style = new PlayerStyle { AppVersion = AppInfo.Version },
        };
    }
}
