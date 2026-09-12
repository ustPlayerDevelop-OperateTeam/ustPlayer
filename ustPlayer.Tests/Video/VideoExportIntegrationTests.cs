using System;
using System.IO;
using System.Threading.Tasks;

using UstPlayer.Interop;
using UstPlayer.Projects;
using UstPlayer.Settings;
using UstPlayer.Ust;
using UstPlayer.Video;

using Xunit;

namespace UstPlayer.Tests.Video;

/// <summary>
/// 视频导出的**真实端到端**测试（渲染器逐帧渲染 → 写出 MP4 → 写 .uprd）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="VideoExporterTests"/> 的分工：那一类测纯逻辑（路径 / 参数 / 边界 / 帧数），
/// 本类测「真的能导出一个能打开的 MP4」。后者无法用假件替代——
/// 编码是渲染器 DLL 内部做的事，参数写错只会在导出时才炸。
/// </para>
/// <para>
/// 渲染器编码器会从 <c>PATH</c> 查找 <c>ffmpeg</c>（见 <c>VideoExporter.DriveRenderer</c>
/// 与 <c>BundledFfmpegPathScope</c>），因此**无 ffmpeg 的环境下本类会失败**。
/// 这是刻意的：导出是本工具的核心功能，缺依赖应当明确报出来，
/// 而不是像旧的 <c>导出失败时清理半成品</c> 那样「成功失败都算通过」——
/// 那条用例在缺 ffmpeg 的机器上一直是绿的，实际上什么都没验证。
/// </para>
/// </remarks>
public class VideoExportIntegrationTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public VideoExportIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"videox-{Guid.NewGuid():N}");
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

    /// <summary>ffmpeg 缺失时给出可操作提示（渲染器编码 MP4 依赖它）。</summary>
    /// <param name="programRoot">程序根目录（测试输出目录）。</param>
    private static void RequireFfmpeg(string programRoot)
    {
        Assert.True(
            ExternalToolLocator.Find("ffmpeg", programRoot) is not null,
            "未找到 ffmpeg。渲染器 uPlRender 编码 MP4 **本身就依赖 ffmpeg**，且只从 PATH 查找"
            + "（见 Video/BundledFfmpegPathScope.cs）。请运行 build/fetch-ffmpeg.ps1 获取。");
    }

    /// <summary>导出无声 MP4：必须真的产出非空 MP4 与配套 .uprd。</summary>
    [Fact]
    public async Task 能导出无声_MP4_与_uprd()
    {
        RequireNativeRenderer();

        var root = Path.Combine(_tempDirectory, "case");
        Directory.CreateDirectory(root);

        var settings = new SettingsManager(Path.Combine(root, "Settings.json"));
        var projectIo = new UplrProjectIO(settings, cacheBaseOverride: Path.Combine(root, "cache"));

        RequireFfmpeg(settings.ProgramRoot);

        settings.File.UstPath = WriteUst(root);

        var output = Path.Combine(root, "out.mp4");
        var exporter = new VideoExporter(settings, new UstFileReader(), projectIo);

        // 2 个 480tick 音符 @120BPM = 1 秒内容；30fps → 30 帧 + 30 帧结束画面
        var uprdPath = await exporter.RenderAsync(output, 640, 360, 30, muxAudio: false);

        Assert.Equal(VideoExporter.UprdPathFor(output), uprdPath);
        Assert.True(File.Exists(output), "应产出 MP4");

        // 空文件（或只有 ftyp 头）说明编码实际没发生
        var length = new FileInfo(output).Length;
        Assert.True(length > 1024, $"MP4 过小（{length} 字节），编码可能没真正执行");

        Assert.True(File.Exists(uprdPath), "应产出 .uprd 工程文件");

        // MP4 魔数：第 4..8 字节应为 'ftyp'
        var header = new byte[12];
        using (var stream = File.OpenRead(output))
        {
            stream.ReadExactly(header);
        }

        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(header, 4, 4));
    }

    /// <summary>写一个最小但合法的 UST。</summary>
    /// <param name="root">目录。</param>
    /// <returns>UST 路径。</returns>
    private static string WriteUst(string root)
    {
        var path = Path.Combine(root, "song.ust");
        File.WriteAllText(
            path,
            "[#VERSION]\nUST Version1.2\n[#SETTING]\nTempo=120.00\nTracks=1\n"
            + "[#0000]\nLength=480\nLyric=a\nNoteNum=60\n"
            + "[#0001]\nLength=480\nLyric=b\nNoteNum=62\n");

        return path;
    }
}
