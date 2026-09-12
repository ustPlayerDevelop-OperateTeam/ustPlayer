using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UstPlayer.Models;
using UstPlayer.Projects;
using UstPlayer.Settings;
using UstPlayer.Ust;
using UstPlayer.Video;

using Xunit;

namespace UstPlayer.Tests.Video;

/// <summary>
/// 视频导出的纯逻辑与命令构造测试。
/// </summary>
/// <remarks>
/// 真实渲染（需要渲染器 DLL 且耗时）与真实混流（需要 ffmpeg）不在本类覆盖；
/// 这里守住的是**容易写错且不报错**的部分：路径规范化、参数校验、结束边界计算、
/// 尾部休止符补帧、以及 ffmpeg 参数是否写对。
/// </remarks>
public class VideoExporterTests : IDisposable
{
    private const int TicksPerQuarterNote = 480;

    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public VideoExporterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"video-{Guid.NewGuid():N}");
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

    // ===================== 路径与参数 =====================

    /// <summary>非 <c>.mp4</c> 输出路径应自动补扩展名；已是 <c>.mp4</c> 则原样保留。</summary>
    [Theory]
    [InlineData("out", "out.mp4")]
    [InlineData("out.mp4", "out.mp4")]
    [InlineData("out.MP4", "out.MP4")]
    [InlineData(@"D:\videos\a.b", @"D:\videos\a.b.mp4")]
    [InlineData("  out  ", "out.mp4")]
    public void 输出路径应补_mp4_扩展名(string input, string expected) =>
        Assert.Equal(expected, VideoExporter.EnsureMp4Extension(input));

    /// <summary><c>.uprd</c> 与视频同名同目录。</summary>
    [Theory]
    [InlineData(@"D:\videos\a.mp4", @"D:\videos\a.uprd")]
    [InlineData("a.mp4", "a.uprd")]
    public void uprd_路径与视频同名(string video, string expected) =>
        Assert.Equal(expected, VideoExporter.UprdPathFor(video));

    /// <summary>非正数参数必须被拒绝（否则后续可能除零）。</summary>
    [Theory]
    [InlineData(0, 1080, 60)]
    [InlineData(1920, 0, 60)]
    [InlineData(1920, 1080, 0)]
    [InlineData(-1, 1080, 60)]
    [InlineData(1920, -1, 60)]
    [InlineData(1920, 1080, -30)]
    public void 非法视频参数被拒绝(int width, int height, int fps) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VideoExporter.ValidateVideoParameters(width, height, fps));

    /// <summary>合法参数通过。</summary>
    [Fact]
    public void 合法视频参数通过() =>
        VideoExporter.ValidateVideoParameters(1920, 1080, 60);

    // ===================== 结束边界 =====================

    /// <summary>内容时长 = 总 tick / (BPM × 480 / 60)。</summary>
    [Fact]
    public void 内容时长计算()
    {
        // 120 BPM：1 秒 = 960 tick；2 个 480 tick 音符 = 1 秒
        var ust = BuildUst(120, (480, "a"), (480, "b"));

        Assert.Equal(1.0, VideoExporter.ContentSeconds(ust), precision: 6);
    }

    /// <summary>零长度音符按 1 tick 计入（与渲染器的 timing.rs 一致）。</summary>
    [Fact]
    public void 零长度音符按一_tick_计算()
    {
        var ust = BuildUst(120, (0, "a"), (480, "b"));

        // 1 + 480 = 481 tick
        Assert.Equal(481 / 960.0, VideoExporter.ContentSeconds(ust), precision: 6);
    }

    /// <summary>速度非法时不除零，返回 0。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void 非法速度下内容时长为零(double tempo)
    {
        var ust = BuildUst(tempo, (480, "a"));

        Assert.Equal(0.0, VideoExporter.ContentSeconds(ust));
    }

    // ===================== 尾部休止符 =====================

    /// <summary>结束边界不超过内容长度时不补帧（返回原对象）。</summary>
    [Fact]
    public void 无需补帧时返回原对象()
    {
        var ust = BuildUst(120, (480, "a"));

        Assert.Same(ust, VideoExporter.PadTrailingRest(ust, endSeconds: 0.5));
        Assert.Same(ust, VideoExporter.PadTrailingRest(ust, endSeconds: 0.2));
    }

    /// <summary>音频更长时补一个休止音符覆盖空拍区间。</summary>
    [Fact]
    public void 音频更长时补尾部休止音符()
    {
        // 内容 0.5 秒（480 tick @120BPM），结束边界 2.5 秒 → 补 2 秒 = 1920 tick
        var ust = BuildUst(120, (480, "a"));

        var padded = VideoExporter.PadTrailingRest(ust, endSeconds: 2.5);

        Assert.Equal(2, padded.Notes.Count);

        var rest = padded.Notes[1];
        Assert.Equal("R", rest.Lyric);
        Assert.Equal("EXPORT_PAD", rest.Index);
        Assert.Equal(1920, rest.Length);

        // 原对象不被修改
        Assert.Single(ust.Notes);

        // 补帧后内容时长应等于结束边界
        Assert.Equal(2.5, VideoExporter.ContentSeconds(padded), precision: 6);
    }

    /// <summary>补帧保留版本、速度与轨道数。</summary>
    [Fact]
    public void 补帧保留其余字段()
    {
        var ust = BuildUst(90, (480, "a"));
        ust.Version = "UST Version1.2";
        ust.Tracks = 3;

        var padded = VideoExporter.PadTrailingRest(ust, endSeconds: 2.0);

        Assert.Equal("UST Version1.2", padded.Version);
        Assert.Equal(90.0, padded.Tempo);
        Assert.Equal(3, padded.Tracks);
    }

    // ===================== 帧数 =====================

    /// <summary>总帧 = 结束边界 × 帧率 + 1 秒结束画面。</summary>
    [Theory]
    [InlineData(10.0, 60, 660)]
    [InlineData(0.5, 30, 45)]
    [InlineData(0.0, 60, 60)]
    public void 总帧数计算(double endSeconds, int fps, int expected) =>
        Assert.Equal(expected, VideoExporter.FramesFor(endSeconds, fps));

    /// <summary>帧数至少为 1。</summary>
    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.1, 1)]
    public void 帧数下限为一(double endSeconds, int fps)
    {
        var frames = VideoExporter.FramesFor(endSeconds, fps);

        Assert.True(frames >= 1, $"帧数应至少为 1，实得 {frames}");
    }

    // ===================== ffmpeg 参数 =====================

    /// <summary>混流参数必须写对：视频轨道复制、音频转 AAC、显式选流、faststart。</summary>
    [Fact]
    public void 混流参数应正确()
    {
        var arguments = FfmpegMuxer.BuildArguments("video.mp4", "audio.wav", "video.mp4.mux.tmp.mp4");

        Assert.Equal("-y", arguments[0]);
        Assert.Equal("-i", arguments[1]);
        Assert.Equal("video.mp4", arguments[2]);
        Assert.Equal("-i", arguments[3]);
        Assert.Equal("audio.wav", arguments[4]);

        // 视频轨道直接复制（不重编码，快且无损）
        Assert.Contains("-c:v", arguments);
        Assert.Equal("copy", arguments[Array.IndexOf(arguments, "-c:v") + 1]);

        // 音频转 AAC
        Assert.Contains("-c:a", arguments);
        Assert.Equal("aac", arguments[Array.IndexOf(arguments, "-c:a") + 1]);

        // 显式选流：第 0 个输入的视频、第 1 个输入的音频
        Assert.Equal("0:v:0", arguments[Array.IndexOf(arguments, "-map") + 1]);
        Assert.Equal("1:a:0", arguments[Array.IndexOf(arguments, "-map", Array.IndexOf(arguments, "-map") + 1) + 1]);

        // 便于网页播放
        Assert.Contains("+faststart", arguments);

        // 输出为最后一个参数
        Assert.Equal("video.mp4.mux.tmp.mp4", arguments[^1]);
    }

    // ===================== 导出失败清理 =====================

    /// <summary>UST 缺失时抛出 <see cref="FileNotFoundException"/>，且不产生半成品文件。</summary>
    [Fact]
    public async Task UST_缺失时抛出且不产生半成品()
    {
        var (exporter, settings, _) = CreateExporter();
        var output = Path.Combine(_tempDirectory, "output.mp4");

        settings.File.UstPath = string.Empty;

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => exporter.RenderAsync(output, 1920, 1080, 60, muxAudio: false));

        Assert.False(File.Exists(output));
        Assert.False(File.Exists(VideoExporter.UprdPathFor(output)));
    }

    /// <summary>
    /// 导出中途失败时必须清理半成品：不留打不开的 MP4，也不留指向无效视频的 <c>.uprd</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用**必定抛异常的混流器**制造「渲染之后」的失败：只有走到这一步，
    /// <c>.uprd</c> 才已经被真正写出来，清理逻辑才谈得上被验证。
    /// </para>
    /// <para>
    /// 缺渲染器 / 缺 ffmpeg 的环境会更早失败——同样必须清理，
    /// 因此「抛异常 + 两件产物都消失」这个断言在任何环境下都成立，
    /// 不需要像此前那样「成功失败都算通过」（那条写法在缺 ffmpeg 的机器上永远是绿的，
    /// 实际上什么都没验证；成功路径现由 <see cref="VideoExportIntegrationTests"/> 覆盖）。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task 导出中途失败时清理半成品()
    {
        var (exporter, settings, root) = CreateExporter();
        var output = Path.Combine(root, "output.mp4");

        settings.File.UstPath = WriteUst("song.ust");

        // 让混流分支真正被进入：路径非空即可（内容无关紧要，混流器是抛异常的假件）
        var musicPath = Path.Combine(_tempDirectory, "dummy.mp3");
        File.WriteAllBytes(musicPath, [0x00]);
        settings.Project.MusicPath = musicPath;

        await Assert.ThrowsAnyAsync<Exception>(
            () => exporter.RenderAsync(output, 640, 360, 30, muxAudio: true));

        Assert.False(File.Exists(output), "失败的导出不应留下 MP4");
        Assert.False(
            File.Exists(VideoExporter.UprdPathFor(output)),
            "失败的导出不应留下指向无效视频的 .uprd");
    }

    // ===================== 辅助 =====================

    private (VideoExporter Exporter, SettingsManager Settings, string Root) CreateExporter()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settings = new SettingsManager(Path.Combine(root, "Settings.json"));
        var projectIo = new UplrProjectIO(settings, cacheBaseOverride: Path.Combine(root, "cache"));
        var exporter = new VideoExporter(settings, new UstFileReader(), projectIo, new ThrowingMuxer());

        return (exporter, settings, root);
    }

    private string WriteUst(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(
            path,
            "[#VERSION]\nUST Version1.2\n[#SETTING]\nTempo=120\n[#0000]\nLength=480\nLyric=a\nNoteNum=60\n");

        return path;
    }

    private static UstInfo BuildUst(double tempo, params (int Length, string Lyric)[] notes)
    {
        var ust = new UstInfo { Version = "UST Version1.2", Tempo = tempo, Tracks = 1 };

        foreach (var (length, lyric) in notes)
        {
            ust.Notes.Add(new NoteInfo
            {
                Index = $"{ust.Notes.Count:0000}",
                Length = length,
                Lyric = lyric,
                NoteNumber = 60,
            });
        }

        return ust;
    }

    /// <summary>测试用混流器：必定抛异常，用于制造「渲染之后」的失败以验证清理。</summary>
    private sealed class ThrowingMuxer : IVideoMuxer
    {
        public bool IsAvailable => true;

        public Task MuxAsync(string videoPath, string audioPath, Func<bool>? cancelCheck,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("测试用混流器：故意失败");
    }
}
