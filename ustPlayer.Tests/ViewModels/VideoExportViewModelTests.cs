using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using UstPlayer.I18n;
using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 「导出视频」对话框 ViewModel 的测试。
/// </summary>
/// <remarks>
/// <para>
/// 只覆盖**纯逻辑**：分辨率预设映射、自定义尺寸夹取、默认输出路径、帧率 / 混音默认值。
/// 真实渲染需要渲染器原生库与 ffmpeg，属于 <c>VideoExportIntegrationTests</c> 的地盘；
/// 这里连 <see cref="VideoExportViewModel.ExportAsync"/> 都不会走到渲染那一步。
/// </para>
/// <para>
/// 与文案相关的断言一律和 <see cref="Translator.Tr"/> 的结果比对，而不是写死中文——
/// <see cref="Translator"/> 是全局状态，别的测试类可能正装载着英文目录
/// （xunit 按类并行），写死中文会变成偶发失败。
/// </para>
/// </remarks>
public class VideoExportViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public VideoExportViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"videoexport-{Guid.NewGuid():N}");
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

    // ===================== 分辨率 =====================

    /// <summary>候选是 1.1.x 的四个预设 + 「自定义」（自定义固定在最后）。</summary>
    [Fact]
    public void 分辨率候选含四个预设与自定义()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        Assert.Equal(5, viewModel.Resolutions.Count);

        Assert.Equal((1920, 1080), (viewModel.Resolutions[0].Width, viewModel.Resolutions[0].Height));
        Assert.Equal((1280, 720), (viewModel.Resolutions[1].Width, viewModel.Resolutions[1].Height));
        Assert.Equal((2560, 1440), (viewModel.Resolutions[2].Width, viewModel.Resolutions[2].Height));
        Assert.Equal((3840, 2160), (viewModel.Resolutions[3].Width, viewModel.Resolutions[3].Height));

        Assert.False(viewModel.Resolutions[0].IsCustom);
        Assert.True(viewModel.Resolutions[4].IsCustom);
        Assert.Equal(Translator.Tr("自定义"), viewModel.Resolutions[4].Label);
    }

    /// <summary>预设项的显示文案是尺寸数字（各语言一致，不进译文表）。</summary>
    [Fact]
    public void 预设文案为尺寸数字()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        Assert.Equal("1920 × 1080", viewModel.Resolutions[0].Label);
        Assert.Equal("3840 × 2160", viewModel.Resolutions[3].Label);
    }

    /// <summary>默认选中 1920 × 1080（1.1.x 的 <c>_set_resolution(1920, 1080)</c>）。</summary>
    [Fact]
    public void 默认分辨率为_1920x1080()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        Assert.False(viewModel.IsCustomResolution);
        Assert.Equal((1920, 1080), viewModel.CurrentResolution);
    }

    /// <summary>选中预设时（宽, 高）就是预设值，与自定义输入框无关。</summary>
    [Theory]
    [InlineData(0, 1920, 1080)]
    [InlineData(1, 1280, 720)]
    [InlineData(2, 2560, 1440)]
    [InlineData(3, 3840, 2160)]
    public void 预设映射到宽高(int index, int width, int height)
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.SelectedResolution = viewModel.Resolutions[index];

        Assert.Equal((width, height), viewModel.CurrentResolution);
    }

    /// <summary>选「自定义」后取的是输入框的值。</summary>
    [Fact]
    public void 自定义分辨率取输入值()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.SelectedResolution = viewModel.Resolutions[4];
        viewModel.CustomWidth = 1000;
        viewModel.CustomHeight = 600;

        Assert.True(viewModel.IsCustomResolution);
        Assert.Equal((1000, 600), viewModel.CurrentResolution);
    }

    /// <summary>自定义尺寸就地夹取（1.1.x 的 SpinBox 范围：宽 320..7680、高 240..4320）。</summary>
    [Theory]
    [InlineData(1.0, 320)]
    [InlineData(319.0, 320)]
    [InlineData(320.0, 320)]
    [InlineData(1920.0, 1920)]
    [InlineData(7680.0, 7680)]
    [InlineData(7681.0, 7680)]
    [InlineData(99999.0, 7680)]
    public void 自定义宽度被夹取(double input, int expected)
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.SelectedResolution = viewModel.Resolutions[^1];
        viewModel.CustomWidth = input;

        Assert.Equal(expected, viewModel.CustomWidth);
        Assert.Equal(expected, viewModel.CurrentResolution.Width);
    }

    /// <summary>高度同理。</summary>
    [Theory]
    [InlineData(1.0, 240)]
    [InlineData(239.0, 240)]
    [InlineData(240.0, 240)]
    [InlineData(1080.0, 1080)]
    [InlineData(4320.0, 4320)]
    [InlineData(4321.0, 4320)]
    public void 自定义高度被夹取(double input, int expected)
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.SelectedResolution = viewModel.Resolutions[^1];
        viewModel.CustomHeight = input;

        Assert.Equal(expected, viewModel.CustomHeight);
        Assert.Equal(expected, viewModel.CurrentResolution.Height);
    }

    /// <summary>非数字输入回退下限（否则 (int)NaN 会变成 0 宽，渲染器直接除零 / 报错）。</summary>
    [Fact]
    public void 非数字输入回退下限()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.SelectedResolution = viewModel.Resolutions[^1];
        viewModel.CustomWidth = double.NaN;
        viewModel.CustomHeight = double.NaN;

        Assert.Equal((VideoExportViewModel.MinimumWidth, VideoExportViewModel.MinimumHeight),
            viewModel.CurrentResolution);
    }

    /// <summary>选预设时把自定义输入框同步成该尺寸（1.1.x 的 <c>_on_res_changed</c>）。</summary>
    [Fact]
    public void 选预设会同步自定义输入框()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.SelectedResolution = viewModel.Resolutions[3];

        Assert.Equal(3840, viewModel.CustomWidth);
        Assert.Equal(2160, viewModel.CustomHeight);

        viewModel.SelectedResolution = viewModel.Resolutions[4];

        Assert.Equal((3840, 2160), viewModel.CurrentResolution);
    }

    // ===================== 帧率 / 混音 =====================

    /// <summary>帧率候选是 24 / 30 / 60，默认 60。</summary>
    [Fact]
    public void 帧率候选与默认值()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        Assert.Equal(new[] { 24, 30, 60 }, viewModel.FpsChoices.Select(choice => choice.Fps));
        Assert.Equal("24 fps", viewModel.FpsChoices[0].Label);
        Assert.Equal("60 fps", viewModel.FpsChoices[2].Label);

        Assert.Equal(VideoExportViewModel.DefaultFps, viewModel.SelectedFps.Fps);
        Assert.Equal(60, viewModel.SelectedFps.Fps);
    }

    /// <summary>混入伴奏默认开启（1.1.x 的 <c>setChecked(True)</c>）。</summary>
    [Fact]
    public void 混入伴奏默认开启()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        Assert.True(viewModel.MuxAudio);
    }

    // ===================== 默认输出路径 =====================

    /// <summary>默认输出路径 = 最近导出目录 / 项目名。</summary>
    [Fact]
    public void 默认输出路径由目录与项目名组成()
    {
        using var services = CreateServices();
        services.Settings.LastExportDirectory = _tempDirectory;
        services.Settings.Project.ProjectName = "我的歌";

        var viewModel = CreateViewModel(services);

        Assert.Equal(Path.Combine(_tempDirectory, "我的歌"), viewModel.OutputPath);
    }

    /// <summary>项目名为空 / 全空白时用「未命名」。</summary>
    /// <param name="projectName">设置里的项目名。</param>
    /// <param name="expectedName">期望的文件名。</param>
    [Theory]
    [InlineData("", "未命名")]
    [InlineData("   ", "未命名")]
    [InlineData("  我的歌  ", "我的歌")]
    public void 默认输出路径用项目名或未命名(string projectName, string expectedName)
    {
        var path = VideoExportViewModel.DefaultOutputPath(_tempDirectory, projectName);

        Assert.Equal(Path.Combine(_tempDirectory, expectedName), path);
    }

    /// <summary>
    /// 「未命名」必须来自译文表（1.1.x 的对话框即 <c>tr("未命名")</c>），
    /// 与「保存工程」的默认文件名同一规则。
    /// </summary>
    [Fact]
    public void 未命名走译文表()
    {
        var path = VideoExportViewModel.DefaultOutputPath(_tempDirectory, "   ");

        Assert.Equal(Path.Combine(_tempDirectory, Translator.Tr("未命名")), path);
    }

    /// <summary>目录为空时只用文件名（不产生前导分隔符）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 目录为空时只用文件名(string? directory)
    {
        Assert.Equal("我的歌", VideoExportViewModel.DefaultOutputPath(directory, "我的歌"));
    }

    // ===================== 状态 =====================

    /// <summary>初始状态是「待开始」，且可以改配置。</summary>
    [Fact]
    public void 初始状态为待开始()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        Assert.Equal(VideoExportStatus.Idle, viewModel.Status);
        Assert.Equal(Translator.Tr("待开始"), viewModel.StatusText);
        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.CanEdit);
        Assert.Equal(0.0, viewModel.Progress);
    }

    /// <summary>未在导出时「取消」不做任何事（对话框会直接关闭，而不是改状态）。</summary>
    [Fact]
    public void 未导出时请求取消无副作用()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.RequestCancel();

        Assert.Equal(VideoExportStatus.Idle, viewModel.Status);
        Assert.Equal(Translator.Tr("待开始"), viewModel.StatusText);
    }

    /// <summary>输出路径为空时拒绝导出（否则会往当前工作目录丢一个 <c>.mp4</c>）。</summary>
    /// <returns>任务。</returns>
    [Fact]
    public async Task 输出路径为空时拒绝导出()
    {
        using var services = CreateServices();
        var viewModel = CreateViewModel(services);

        viewModel.OutputPath = "   ";

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.ExportAsync());

        // 参数校验发生在动渲染器之前，状态必须还停在「待开始」
        Assert.Equal(VideoExportStatus.Idle, viewModel.Status);
    }

    // ===================== 译文表 =====================

    /// <summary>
    /// 成功提示是多行条目（<c>视频已导出：{0}\n已保存工程：{1}</c>），必须能被译文表查到。
    /// </summary>
    /// <remarks>
    /// 这条文案在 <c>.ts</c> 里跨两行；查询键里的换行只能是 <b>LF</b>——
    /// 多写一个 <c>\r</c> 就会静默查不到，英文 / 文言界面里这条提示永远显示中文
    /// （回退不报错，肉眼很难发现）。用部署到程序目录的目录文件核对，顺带守住部署这一环。
    /// </remarks>
    [Fact]
    public void 成功文案可被译文表查到()
    {
        var catalog = TranslationCatalogLoader.LoadForLocale(AppContext.BaseDirectory, "en_US");

        Assert.NotNull(catalog);

        var translated = catalog!.Translate("视频已导出：{0}\n已保存工程：{1}");

        Assert.NotNull(translated);
        Assert.StartsWith("Video exported:", translated, StringComparison.Ordinal);
    }

    // ===================== 辅助 =====================

    /// <summary>创建对话框 ViewModel（复用组合根里已装配的导出器）。</summary>
    /// <param name="services">组合根。</param>
    /// <returns>ViewModel。</returns>
    private static VideoExportViewModel CreateViewModel(AppServices services) =>
        new(services.Settings, services.VideoExporter);

    /// <summary>创建指向临时目录的组合根。</summary>
    /// <returns>组合根（调用方负责释放）。</returns>
    private AppServices CreateServices()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new AppServices(Path.Combine(root, "Settings.json"));
    }
}
