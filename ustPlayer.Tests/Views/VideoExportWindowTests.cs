using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Controls;

using Avalonia.Headless.XUnit;

using FluentAvalonia.UI.Controls;

using UstPlayer.I18n;
using UstPlayer.ViewModels;
using UstPlayer.Views;

using Xunit;

namespace UstPlayer.Tests.Views;

/// <summary>
/// 「导出视频」对话框的无头界面测试：验证 XAML 能真实加载、绑定与可见性正确。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="PlayerStylePageTests"/> 同样的约束：窗口只构造、<b>不 Show 也不 Measure</b>——
/// FluentAvalonia 的控件模板用图标字体渲染，Avalonia 的无头字体管理器解析不了该字体，
/// 一旦触发渲染就会抛「Could not create glyphTypeface」。绑定在设置 DataContext 后即生效，
/// 足以覆盖「选自定义 → 出现宽 / 高输入框」这条最容易写错的地方。
/// </para>
/// <para>
/// 本类不需要渲染器原生库或 ffmpeg：它只构造界面，不发起导出。
/// 「导出期间锁定配置控件」由 <c>CanEdit</c> 绑定实现，要真正翻转它必须先跑一次真实渲染，
/// 因此只能由 <c>build/verify-app-launch.ps1</c> 一类真机验证覆盖。
/// </para>
/// </remarks>
public class VideoExportWindowTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public VideoExportWindowTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"videoexportwindow-{Guid.NewGuid():N}");
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

    /// <summary>对话框能加载 XAML，且默认值直接反映到控件上。</summary>
    [AvaloniaFact]
    public void 对话框加载并反映默认值()
    {
        using var services = CreateServices();
        services.Settings.LastExportDirectory = _tempDirectory;
        services.Settings.Project.ProjectName = "我的歌";

        var (window, _) = CreateWindow(services);

        // 默认输出路径 = 最近导出目录 / 项目名
        Assert.Equal(Path.Combine(_tempDirectory, "我的歌"), Box(window, "OutputPathBox").Text);

        // 混入伴奏默认开、帧率默认 60、分辨率默认 1920 × 1080
        Assert.True(Toggle(window, "MuxToggle").IsChecked);
        Assert.Equal(60, Selected<FpsPreset>(window, "FpsCombo").Fps);
        Assert.Equal(1920, Selected<ResolutionPreset>(window, "ResolutionCombo").Width);

        // 非自定义时宽 / 高输入框必须收起，否则用户会以为预设也能改
        Assert.False(Number(window, "CustomWidthBox").IsVisible);
        Assert.False(Number(window, "CustomHeightBox").IsVisible);

        // 进度条是千分比刻度，初始文案为「待开始」
        Assert.Equal(1000.0, Progress(window).Maximum);
        Assert.Equal(0.0, Progress(window).Value);
        Assert.Equal(Translator.Tr("待开始"), StatusText(window).Text);
    }

    /// <summary>选中「自定义」后出现宽 / 高输入框，且上下限就是 1.1.x 的 SpinBox 范围。</summary>
    [AvaloniaFact]
    public void 选中自定义后显示宽高输入框()
    {
        using var services = CreateServices();
        var (window, viewModel) = CreateWindow(services);

        viewModel.SelectedResolution = viewModel.Resolutions[^1];

        var width = Number(window, "CustomWidthBox");
        var height = Number(window, "CustomHeightBox");

        Assert.True(width.IsVisible);
        Assert.True(height.IsVisible);

        Assert.Equal(320.0, width.Minimum);
        Assert.Equal(7680.0, width.Maximum);
        Assert.Equal(240.0, height.Minimum);
        Assert.Equal(4320.0, height.Maximum);
    }

    /// <summary>输出路径在界面与 ViewModel 之间双向同步。</summary>
    [AvaloniaFact]
    public void 输出路径双向绑定()
    {
        using var services = CreateServices();
        var (window, viewModel) = CreateWindow(services);

        Box(window, "OutputPathBox").Text = @"C:\out\song.mp4";

        Assert.Equal(@"C:\out\song.mp4", viewModel.OutputPath);
    }

    /// <summary>文案由 code-behind 用 <see cref="Translator.Tr"/> 赋值（不在 XAML 里写死中文）。</summary>
    [AvaloniaFact]
    public void 文案走译文表()
    {
        using var services = CreateServices();
        var (window, _) = CreateWindow(services);

        Assert.Equal(Translator.Tr("导出视频"), window.Title);

        Assert.Equal(Translator.Tr("输出视频："), Text(window, "OutputLabel").Text);
        Assert.Equal(Translator.Tr("分辨率："), Text(window, "ResolutionLabel").Text);
        Assert.Equal(Translator.Tr("帧率："), Text(window, "FpsLabel").Text);
        Assert.Equal(Translator.Tr("混入伴奏音频："), Text(window, "MuxLabel").Text);

        Assert.Equal(Translator.Tr("浏览"), Button(window, "BrowseButton").Content as string);
        Assert.Equal(Translator.Tr("开始导出"), Button(window, "StartButton").Content as string);
        Assert.Equal(Translator.Tr("取消"), Button(window, "CancelButton").Content as string);

        // 开始导出是默认按钮（回车），取消是取消按钮（Esc → 导出中触发取消，否则关窗）
        Assert.True(Button(window, "StartButton").IsDefault);
        Assert.True(Button(window, "CancelButton").IsCancel);
    }

    // ===================== 辅助 =====================

    /// <summary>构造对话框（不显示）。</summary>
    /// <param name="services">组合根。</param>
    /// <returns>窗口与 ViewModel。</returns>
    private static (VideoExportWindow Window, VideoExportViewModel ViewModel) CreateWindow(
        AppServices services)
    {
        var viewModel = new VideoExportViewModel(services.Settings, services.VideoExporter);

        return (new VideoExportWindow(viewModel, new RecordingNotificationHost()), viewModel);
    }

    /// <summary>取控件。</summary>
    /// <typeparam name="T">控件类型。</typeparam>
    /// <param name="window">对话框。</param>
    /// <param name="name">控件名。</param>
    /// <returns>控件。</returns>
    private static T Control<T>(VideoExportWindow window, string name)
        where T : Control
    {
        var control = window.FindControl<T>(name);

        Assert.NotNull(control);

        return control!;
    }

    private static TextBox Box(VideoExportWindow window, string name) => Control<TextBox>(window, name);

    private static TextBlock Text(VideoExportWindow window, string name) =>
        Control<TextBlock>(window, name);

    private static Button Button(VideoExportWindow window, string name) =>
        Control<Button>(window, name);

    private static ToggleSwitch Toggle(VideoExportWindow window, string name) =>
        Control<ToggleSwitch>(window, name);

    private static NumberBox Number(VideoExportWindow window, string name) =>
        Control<NumberBox>(window, name);

    private static ProgressBar Progress(VideoExportWindow window) =>
        Control<ProgressBar>(window, "ExportProgress");

    private static TextBlock StatusText(VideoExportWindow window) =>
        Control<TextBlock>(window, "StatusLabel");

    /// <summary>取下拉当前选中项。</summary>
    /// <typeparam name="T">候选项类型。</typeparam>
    /// <param name="window">对话框。</param>
    /// <param name="name">控件名。</param>
    /// <returns>选中项。</returns>
    private static T Selected<T>(VideoExportWindow window, string name) =>
        Assert.IsType<T>(Control<ComboBox>(window, name).SelectedItem);

    /// <summary>创建指向临时目录的组合根。</summary>
    /// <returns>组合根（调用方负责释放）。</returns>
    private AppServices CreateServices()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new AppServices(Path.Combine(root, "Settings.json"));
    }

    /// <summary>记录调用而不显示提示的宿主（对话框因此无需真实窗口）。</summary>
    private sealed class RecordingNotificationHost : INotificationHost
    {
        /// <summary>收到的提示。</summary>
        internal List<(NotificationSeverity Severity, string Title, string Message)> Messages { get; } = [];

        /// <inheritdoc />
        public void Notify(NotificationSeverity severity, string title, string message) =>
            Messages.Add((severity, title, message));
    }
}
