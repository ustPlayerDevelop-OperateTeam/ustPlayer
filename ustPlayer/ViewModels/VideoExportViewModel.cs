using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.Settings;
using UstPlayer.Video;

namespace UstPlayer.ViewModels;

/// <summary>分辨率候选 — 固定预设，或「自定义」（尺寸由用户输入）。</summary>
/// <remarks>
/// 显示文案里的尺寸数字（<c>1920 × 1080</c>）各语言一致，因此**不进译文表**——
/// 1.1.x 对它们调用 <c>tr()</c> 也只是原样返回；只有「自定义」这一项需要译文。
/// </remarks>
internal sealed class ResolutionPreset
{
    /// <summary>创建候选。</summary>
    /// <param name="width">宽（像素）。</param>
    /// <param name="height">高（像素）。</param>
    /// <param name="label">显示文案。</param>
    /// <param name="isCustom">是否为「自定义」项。</param>
    internal ResolutionPreset(int width, int height, string label, bool isCustom = false)
    {
        Width = width;
        Height = height;
        Label = label;
        IsCustom = isCustom;
    }

    /// <summary>宽（像素）。</summary>
    internal int Width { get; }

    /// <summary>高（像素）。</summary>
    internal int Height { get; }

    /// <summary>显示文案。</summary>
    internal string Label { get; }

    /// <summary>是否为「自定义」项（选中它时显示宽 / 高输入框）。</summary>
    internal bool IsCustom { get; }
}

/// <summary>帧率候选。</summary>
/// <remarks>「24 fps」这类文案各语言一致，不查译文表（1.1.x 同样直接拼字符串）。</remarks>
internal sealed class FpsPreset
{
    /// <summary>创建候选。</summary>
    /// <param name="fps">帧率。</param>
    internal FpsPreset(int fps)
    {
        Fps = fps;
        Label = $"{fps} fps";
    }

    /// <summary>帧率。</summary>
    internal int Fps { get; }

    /// <summary>显示文案。</summary>
    internal string Label { get; }
}

/// <summary>导出状态（决定状态文案与界面可用性）。</summary>
internal enum VideoExportStatus
{
    /// <summary>尚未开始。</summary>
    Idle,

    /// <summary>正在渲染。</summary>
    Rendering,

    /// <summary>已请求取消，等待渲染循环退出。</summary>
    Cancelling,

    /// <summary>已完成。</summary>
    Done,

    /// <summary>已失败。</summary>
    Failed,

    /// <summary>已取消。</summary>
    Cancelled,
}

/// <summary>导出结果的类别。</summary>
internal enum VideoExportOutcomeKind
{
    /// <summary>成功。</summary>
    Success,

    /// <summary>失败。</summary>
    Failed,

    /// <summary>用户取消（不是失败）。</summary>
    Cancelled,
}

/// <summary>导出结果。</summary>
/// <param name="Kind">类别。</param>
/// <param name="UprdPath">成功时写入的 <c>.uprd</c> 路径。</param>
/// <param name="ErrorMessage">失败时的错误信息。</param>
internal readonly record struct VideoExportOutcome(
    VideoExportOutcomeKind Kind,
    string UprdPath,
    string ErrorMessage)
{
    /// <summary>成功结果。</summary>
    /// <param name="uprdPath">写入的 <c>.uprd</c> 路径。</param>
    /// <returns>结果。</returns>
    internal static VideoExportOutcome Success(string uprdPath) =>
        new(VideoExportOutcomeKind.Success, uprdPath, string.Empty);

    /// <summary>取消结果。</summary>
    /// <returns>结果。</returns>
    internal static VideoExportOutcome Cancelled() =>
        new(VideoExportOutcomeKind.Cancelled, string.Empty, string.Empty);

    /// <summary>失败结果。</summary>
    /// <param name="message">错误信息。</param>
    /// <returns>结果。</returns>
    internal static VideoExportOutcome Failed(string message) =>
        new(VideoExportOutcomeKind.Failed, string.Empty, message);
}

/// <summary>
/// 「导出视频」对话框的状态与流程 — 从 1.1.x <c>ui/video_export_dialog.py</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 这里只放**可单测的逻辑与导出流程**：分辨率预设映射、自定义尺寸夹取、默认输出路径、
/// 进度（千分比 0..1000）与状态。窗口、文件选择框与提示条留在 View
/// （<c>Views/VideoExportWindow.axaml</c>）。
/// </para>
/// <para>
/// 与 1.1.x 的差异：1.1.x 要自建 <c>QThread</c> + worker 才能不卡界面；2.0 的
/// <see cref="VideoExporter.RenderAsync"/> 本身就是 <c>async</c>，这里只需
/// <see cref="Task.Run{TResult}(Func{TResult})"/> 把它挪出 UI 线程，**不阻塞** UI 线程。
/// </para>
/// <para>
/// <b>取消必须真的生效</b>：<see cref="RequestCancel"/> 触发取消令牌，渲染循环每帧检查它，
/// 导出器自己负责清理半成品（MP4 与 <c>.uprd</c>）。
/// </para>
/// </remarks>
internal sealed class VideoExportViewModel : ViewModelBase
{
    /// <summary>自定义宽下限（与 1.1.x 的 SpinBox 范围一致）。</summary>
    internal const int MinimumWidth = 320;

    /// <summary>自定义宽上限。</summary>
    internal const int MaximumWidth = 7680;

    /// <summary>自定义高下限。</summary>
    internal const int MinimumHeight = 240;

    /// <summary>自定义高上限。</summary>
    internal const int MaximumHeight = 4320;

    /// <summary>进度上限（导出器按千分比上报）。</summary>
    internal const double ProgressMaximum = 1000;

    /// <summary>默认帧率（1.1.x 的 <c>setCurrentIndex(2)</c>）。</summary>
    internal const int DefaultFps = 60;

    /// <summary>「自定义」分辨率项的初始尺寸（与默认预设一致）。</summary>
    private const int CustomInitialWidth = 1920;

    /// <summary>「自定义」分辨率项的初始高度。</summary>
    private const int CustomInitialHeight = 1080;

    private readonly SettingsManager _settings;
    private readonly VideoExporter _exporter;

    private ResolutionPreset _selectedResolution;
    private FpsPreset _selectedFps;
    private string _outputPath;
    private double _customWidth;
    private double _customHeight;
    private bool _muxAudio = true;
    private double _progress;
    private VideoExportStatus _status;

    /// <summary>取消令牌源；仅在导出期间非空。</summary>
    private CancellationTokenSource? _cancellation;

    /// <summary>最近一次上报的千分比（去重用，见 <see cref="Report"/>）。</summary>
    private int _lastReportedProgress = -1;

    /// <summary>创建对话框 ViewModel。</summary>
    /// <param name="settings">设置门面（读项目名与最近导出目录、导出成功后写回）。</param>
    /// <param name="exporter">视频导出器（来自组合根 <c>AppServices.VideoExporter</c>）。</param>
    internal VideoExportViewModel(SettingsManager settings, VideoExporter exporter)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(exporter);

        _settings = settings;
        _exporter = exporter;

        Resolutions =
        [
            new ResolutionPreset(1920, 1080, "1920 × 1080"),
            new ResolutionPreset(1280, 720, "1280 × 720"),
            new ResolutionPreset(2560, 1440, "2560 × 1440"),
            new ResolutionPreset(3840, 2160, "3840 × 2160"),
            new ResolutionPreset(
                CustomInitialWidth,
                CustomInitialHeight,
                Translator.Tr("自定义"),
                isCustom: true),
        ];

        FpsChoices = [new FpsPreset(24), new FpsPreset(30), new FpsPreset(DefaultFps)];

        _selectedResolution = Resolutions[0];
        _selectedFps = FpsChoices[2];
        _customWidth = CustomInitialWidth;
        _customHeight = CustomInitialHeight;

        _outputPath = DefaultOutputPath(settings.LastExportDirectory, settings.Project.ProjectName);
    }

    // ===================== 配置 =====================

    /// <summary>分辨率候选（最后一项是「自定义」）。</summary>
    internal IReadOnlyList<ResolutionPreset> Resolutions { get; }

    /// <summary>帧率候选。</summary>
    internal IReadOnlyList<FpsPreset> FpsChoices { get; }

    /// <summary>输出视频路径（默认 <c>&lt;最近导出目录&gt;/&lt;项目名&gt;</c>）。</summary>
    internal string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value ?? string.Empty);
    }

    /// <summary>
    /// 选中的分辨率项。
    /// </summary>
    /// <remarks>
    /// 选中固定预设时把自定义输入框也同步成该尺寸（1.1.x 的 <c>_on_res_changed</c> 即如此），
    /// 这样从预设切到「自定义」不会看到一个与刚才所选无关的尺寸。
    /// </remarks>
    internal ResolutionPreset SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            // 下拉的 SelectedItem 在候选被替换等情况下可能回写 null：忽略，而不是让界面空掉
            if (value is null || ReferenceEquals(value, _selectedResolution))
            {
                return;
            }

            if (!SetProperty(ref _selectedResolution, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCustomResolution));

            if (!value.IsCustom)
            {
                CustomWidth = value.Width;
                CustomHeight = value.Height;
            }
        }
    }

    /// <summary>是否选中「自定义」（决定宽 / 高输入框是否可见）。</summary>
    internal bool IsCustomResolution => SelectedResolution.IsCustom;

    /// <summary>自定义宽（越界值就地夹取，与 1.1.x 的 SpinBox 一致）。</summary>
    internal double CustomWidth
    {
        get => _customWidth;
        set => SetProperty(ref _customWidth, ClampWidth(value));
    }

    /// <summary>自定义高。</summary>
    internal double CustomHeight
    {
        get => _customHeight;
        set => SetProperty(ref _customHeight, ClampHeight(value));
    }

    /// <summary>选中的帧率项。</summary>
    internal FpsPreset SelectedFps
    {
        get => _selectedFps;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedFps))
            {
                return;
            }

            SetProperty(ref _selectedFps, value);
        }
    }

    /// <summary>是否把伴奏混入视频（默认开，与 1.1.x 一致）。</summary>
    internal bool MuxAudio
    {
        get => _muxAudio;
        set => SetProperty(ref _muxAudio, value);
    }

    /// <summary>实际要渲染的 (宽, 高)：预设取预设值，自定义取输入框（已夹取）。</summary>
    internal (int Width, int Height) CurrentResolution => SelectedResolution.IsCustom
        ? (ClampWidth(CustomWidth), ClampHeight(CustomHeight))
        : (SelectedResolution.Width, SelectedResolution.Height);

    // ===================== 状态 =====================

    /// <summary>进度（千分比 0..1000）。</summary>
    internal double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>导出状态。</summary>
    internal VideoExportStatus Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsExporting));
            OnPropertyChanged(nameof(CanEdit));
        }
    }

    /// <summary>状态文案（「待开始」/「正在渲染…」/「完成」/「失败」/「已取消」/「正在取消…」）。</summary>
    internal string StatusText => Translator.Tr(Status switch
    {
        VideoExportStatus.Rendering => "正在渲染…",
        VideoExportStatus.Cancelling => "正在取消…",
        VideoExportStatus.Done => "完成",
        VideoExportStatus.Failed => "失败",
        VideoExportStatus.Cancelled => "已取消",
        _ => "待开始",
    });

    /// <summary>是否正在导出（导出期间禁止关窗、禁止改设置项）。</summary>
    internal bool IsExporting =>
        Status is VideoExportStatus.Rendering or VideoExportStatus.Cancelling;

    /// <summary>是否允许修改配置（导出期间全部锁住；「取消」不受此限）。</summary>
    internal bool CanEdit => !IsExporting;

    // ===================== 流程 =====================

    /// <summary>
    /// 按当前配置导出视频。
    /// </summary>
    /// <returns>结果（成功 / 失败 / 已取消）。</returns>
    /// <remarks>
    /// <para>
    /// 渲染放在线程池上执行（<see cref="Task.Run{TResult}(Func{Task{TResult}})"/>），
    /// UI 线程只 <c>await</c>，因此窗口全程可响应——<b>不</b>用 <c>.Result</c> / <c>.Wait()</c>。
    /// </para>
    /// <para>
    /// 进度回调由渲染线程调用：用 <see cref="Progress{T}"/>（捕获 UI 线程的同步上下文）
    /// 把它投递回 UI 线程，并**按千分比去重**——逐帧回调会刷爆消息队列。
    /// </para>
    /// </remarks>
    internal async Task<VideoExportOutcome> ExportAsync()
    {
        if (IsExporting)
        {
            throw new InvalidOperationException("导出正在进行中，不能重复启动");
        }

        // 视图已经提示过「请先选择输出视频路径」，这里再兜一层：
        // 空路径会被 EnsureMp4Extension 变成 ".mp4"，等于往当前工作目录里丢文件
        var outputPath = OutputPath.Trim();

        if (outputPath.Length == 0)
        {
            throw new InvalidOperationException("未选择输出视频路径");
        }

        outputPath = VideoExporter.EnsureMp4Extension(outputPath);
        OutputPath = outputPath;

        var (width, height) = CurrentResolution;
        var fps = SelectedFps.Fps;
        var muxAudio = MuxAudio;

        _lastReportedProgress = -1;
        Progress = 0;
        Status = VideoExportStatus.Rendering;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        var reporter = new Progress<double>(value => Progress = value);

        try
        {
            var uprdPath = await Task.Run(
                () => _exporter.RenderAsync(
                    outputPath,
                    width,
                    height,
                    fps,
                    muxAudio,
                    perMille => Report(reporter, perMille),
                    cancelCheck: null,
                    cancellationToken: cancellation.Token)).ConfigureAwait(true);

            Progress = ProgressMaximum;
            Status = VideoExportStatus.Done;
            RememberExportDirectory(uprdPath);

            return VideoExportOutcome.Success(uprdPath);
        }
        catch (OperationCanceledException)
        {
            // 取消不是失败：导出器已自行清理半成品，界面只是回到「已取消」
            AppLogger.Info($"视频导出已取消：{outputPath}");
            Progress = 0;
            Status = VideoExportStatus.Cancelled;

            return VideoExportOutcome.Cancelled();
        }
        catch (Exception exception)
        {
            // 取消是「意图」，不能按异常类型判断：中途取消时原生渲染器 / 编码器抛出的
            // 往往是 RendererException 或 IOException（ffmpeg 被杀掉、MP4 只写了一半），
            // 而不是 OperationCanceledException。若只看异常类型，同一个「用户取消」操作
            // 会因为取消恰好落在哪一步而**时而是 Cancelled、时而是 Failed**——
            // 既不符合语义，也正是本类用例偶发失败的原因（已实测复现并抓到名字）。
            if (cancellation.IsCancellationRequested)
            {
                AppLogger.Info($"视频导出已取消：{outputPath}（底层报错：{exception.Message}）");
                Progress = 0;
                Status = VideoExportStatus.Cancelled;

                return VideoExportOutcome.Cancelled();
            }

            AppLogger.Error($"导出视频失败：{outputPath}", exception);
            Status = VideoExportStatus.Failed;

            return VideoExportOutcome.Failed(exception.Message);
        }
        finally
        {
            _cancellation = null;
        }
    }

    /// <summary>
    /// 请求取消正在进行的导出。
    /// </summary>
    /// <remarks>
    /// 渲染循环每帧检查令牌，因此取消是**协作式**的：状态先变成「正在取消…」，
    /// 真正结束（并清理半成品）后由 <see cref="ExportAsync"/> 收尾。
    /// 不在导出中时本方法不做任何事。
    /// </remarks>
    internal void RequestCancel()
    {
        if (!IsExporting)
        {
            return;
        }

        Status = VideoExportStatus.Cancelling;
        _cancellation?.Cancel();
    }

    // ===================== 纯逻辑 =====================

    /// <summary>
    /// 默认输出路径：<c>&lt;最近导出目录&gt;/&lt;项目名&gt;</c>。
    /// </summary>
    /// <param name="lastExportDirectory">最近导出目录；为空时只用文件名。</param>
    /// <param name="projectName">项目名；为空时用「未命名」。</param>
    /// <returns>默认输出路径。</returns>
    /// <remarks>
    /// 项目名为空时用「未命名」（查译文表），避免出现空文件名——
    /// 与「保存工程」的默认文件名同一规则（<c>BasicPageViewModel.SuggestProjectFileName</c>），
    /// 只是这里要走译文表（1.1.x 的对话框即 <c>tr("未命名")</c>）。
    /// </remarks>
    internal static string DefaultOutputPath(string? lastExportDirectory, string? projectName)
    {
        var name = (projectName ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            name = Translator.Tr("未命名");
        }

        return string.IsNullOrWhiteSpace(lastExportDirectory)
            ? name
            : Path.Combine(lastExportDirectory, name);
    }

    /// <summary>把宽夹取到合法范围（非数字输入回退下限，避免出现 0 宽）。</summary>
    /// <param name="value">输入值。</param>
    /// <returns>夹取后的整数宽。</returns>
    internal static int ClampWidth(double value) =>
        double.IsNaN(value) ? MinimumWidth : (int)Math.Clamp(Math.Round(value), MinimumWidth, MaximumWidth);

    /// <summary>把高夹取到合法范围。</summary>
    /// <param name="value">输入值。</param>
    /// <returns>夹取后的整数高。</returns>
    internal static int ClampHeight(double value) =>
        double.IsNaN(value) ? MinimumHeight : (int)Math.Clamp(Math.Round(value), MinimumHeight, MaximumHeight);

    // ===================== 辅助 =====================

    /// <summary>把进度（千分比）投递到 UI 线程；值未变则跳过。</summary>
    /// <param name="reporter">进度投递器（捕获 UI 线程同步上下文）。</param>
    /// <param name="perMille">导出器上报的千分比。</param>
    private void Report(IProgress<double> reporter, int perMille)
    {
        var clamped = Math.Clamp(perMille, 0, (int)ProgressMaximum);

        if (clamped == _lastReportedProgress)
        {
            return;
        }

        _lastReportedProgress = clamped;
        reporter.Report(clamped);
    }

    /// <summary>记住本次导出目录并立即写盘（1.1.x 导出成功后同样写设置）。</summary>
    /// <param name="uprdPath">写入的 <c>.uprd</c> 路径。</param>
    private void RememberExportDirectory(string uprdPath)
    {
        var directory = Path.GetDirectoryName(uprdPath);

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        _settings.LastExportDirectory = directory;
        _settings.WriteSettings();
    }
}
