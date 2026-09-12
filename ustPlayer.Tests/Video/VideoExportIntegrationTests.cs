using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using UstPlayer.I18n;
using UstPlayer.Interop;
using UstPlayer.Projects;
using UstPlayer.Settings;
using UstPlayer.Ust;
using UstPlayer.Video;
using UstPlayer.ViewModels;

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
[Collection(NativeRendererCollection.Name)]
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

    // ===================== 对话框 ViewModel 的真实导出 =====================
    //
    // 放在本类（而不是 ViewModels/）的原因是跨平台 CI 过滤只按 FullyQualifiedName 排除
    // 本类——这里的用例同样依赖渲染器原生库与 ffmpeg，另起一个类就必须同步 CI 的
    // NATIVE_ONLY_TESTS_FILTER，漏改会让非 Windows 作业明确失败。

    /// <summary>
    /// 「导出视频」对话框的 ViewModel 能真的驱动一次导出：成功状态、.uprd 与目录记忆都对。
    /// </summary>
    /// <returns>任务。</returns>
    /// <remarks>
    /// 覆盖对话框最关键的一段接线：分辨率 / 帧率 / 混音开关与进度回调真的传到了导出器，
    /// 且成功后把 <c>.uprd</c> 所在目录写回设置（1.1.x 亦如此）。
    /// </remarks>
    [Fact]
    public async Task 对话框ViewModel能驱动真实导出()
    {
        RequireNativeRenderer();

        var root = Path.Combine(_tempDirectory, "vm-success");
        Directory.CreateDirectory(root);

        var settings = new SettingsManager(Path.Combine(root, "Settings.json"));
        var projectIo = new UplrProjectIO(settings, cacheBaseOverride: Path.Combine(root, "cache"));

        RequireFfmpeg(settings.ProgramRoot);

        settings.File.UstPath = WriteUst(root);
        settings.Project.ProjectName = "对话框";

        var viewModel = CreateDialogViewModel(settings, projectIo, root, "out.mp4");

        // 记录进度通知：导出期间进度条必须真的推进，而不是一直停在 0
        var observed = new List<double>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VideoExportViewModel.Progress))
            {
                observed.Add(viewModel.Progress);
            }
        };

        var outcome = await viewModel.ExportAsync();

        // 断言里必须带上 ErrorMessage：这条用例曾偶发失败，而原先的
        // Assert.Equal 只报「Success vs Failed」，看不出到底是渲染器、ffmpeg
        // 还是路径的问题，导致长期无法定位
        Assert.True(
            outcome.Kind == VideoExportOutcomeKind.Success,
            $"真实导出应成功，实际为 {outcome.Kind}；错误信息：{outcome.ErrorMessage}");
        Assert.Equal(VideoExporter.UprdPathFor(viewModel.OutputPath), outcome.UprdPath);
        Assert.True(File.Exists(outcome.UprdPath), "应产出 .uprd 工程文件");

        Assert.Equal(VideoExportStatus.Done, viewModel.Status);
        Assert.Equal(TranslatorSample("完成"), viewModel.StatusText);
        Assert.Equal(VideoExportViewModel.ProgressMaximum, viewModel.Progress);
        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.CanEdit);

        // 渲染器按千分比回调（本用例 48 帧 → 24 次），且单调不减、最终到 1000
        Assert.True(
            observed.Count >= 3,
            $"导出期间没有收到进度回调（{observed.Count} 次），进度条会一直停在 0");

        Assert.Equal(observed.OrderBy(value => value), observed);
        Assert.Equal(VideoExportViewModel.ProgressMaximum, observed[^1]);

        // 成功后记住本次导出目录并立即写盘
        Assert.Equal(root, settings.LastExportDirectory);
        Assert.Contains("last_export_dir", File.ReadAllText(settings.SettingsPath));
    }

    /// <summary>
    /// 「取消」必须真的中止渲染：状态变「已取消」，半成品 MP4 与 .uprd 都被清理。
    /// </summary>
    /// <returns>任务。</returns>
    /// <remarks>
    /// 这是对话框里最容易「看起来能用其实没用」的一条：取消按钮只改文案而没把令牌传给
    /// 渲染循环时，界面会显示「已取消」但进程仍在渲染，还会留下打不开的文件。
    /// </remarks>
    [Fact]
    public async Task 对话框取消会中止导出并清理半成品()
    {
        RequireNativeRenderer();

        var root = Path.Combine(_tempDirectory, "vm-cancel");
        Directory.CreateDirectory(root);

        var settings = new SettingsManager(Path.Combine(root, "Settings.json"));
        var projectIo = new UplrProjectIO(settings, cacheBaseOverride: Path.Combine(root, "cache"));

        RequireFfmpeg(settings.ProgramRoot);

        settings.File.UstPath = WriteUst(root);

        var viewModel = CreateDialogViewModel(settings, projectIo, root, "cancelled.mp4");

        // ExportAsync 的同步段已经把状态置为「正在渲染」并建好取消令牌，
        // 因此这里紧接着请求取消，渲染循环的第一帧检查就会抛出
        var export = viewModel.ExportAsync();
        viewModel.RequestCancel();

        Assert.Equal(VideoExportStatus.Cancelling, viewModel.Status);
        Assert.Equal(TranslatorSample("正在取消…"), viewModel.StatusText);

        var outcome = await export;

        Assert.Equal(VideoExportOutcomeKind.Cancelled, outcome.Kind);
        Assert.Equal(VideoExportStatus.Cancelled, viewModel.Status);
        Assert.Equal(TranslatorSample("已取消"), viewModel.StatusText);
        Assert.False(viewModel.IsExporting);

        Assert.False(File.Exists(viewModel.OutputPath), "取消后不应留下半成品 MP4");
        Assert.False(
            File.Exists(VideoExporter.UprdPathFor(viewModel.OutputPath)),
            "取消后不应留下指向无效视频的 .uprd");
    }

    /// <summary>
    /// 按对话框的默认值之外的最小尺寸配置一个 ViewModel（把渲染压到最快）。
    /// </summary>
    /// <param name="settings">设置管理器。</param>
    /// <param name="projectIo">工程 IO。</param>
    /// <param name="root">输出目录。</param>
    /// <param name="fileName">输出文件名。</param>
    /// <returns>ViewModel。</returns>
    private static VideoExportViewModel CreateDialogViewModel(
        SettingsManager settings,
        UplrProjectIO projectIo,
        string root,
        string fileName)
    {
        var viewModel = new VideoExportViewModel(
            settings,
            new VideoExporter(settings, new UstFileReader(), projectIo));

        // 自定义分辨率下限 320 × 240 + 24 fps：帧数最少，渲染最快
        viewModel.SelectedResolution = viewModel.Resolutions[^1];
        viewModel.CustomWidth = VideoExportViewModel.MinimumWidth;
        viewModel.CustomHeight = VideoExportViewModel.MinimumHeight;
        viewModel.SelectedFps = viewModel.FpsChoices[0];
        viewModel.MuxAudio = false;
        viewModel.OutputPath = Path.Combine(root, fileName);

        return viewModel;
    }

    /// <summary>查一条译文（<see cref="Translator"/> 是全局状态，因此不写死中文）。</summary>
    /// <param name="source">中文原文。</param>
    /// <returns>当前语言的译文。</returns>
    private static string TranslatorSample(string source) => Translator.Tr(source);
}
