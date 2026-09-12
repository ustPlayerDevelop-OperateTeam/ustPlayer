using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UstPlayer.Diagnostics;
using UstPlayer.Interop;
using UstPlayer.Models;
using UstPlayer.Projects;
using UstPlayer.Settings;
using UstPlayer.Ust;

namespace UstPlayer.Video;

/// <summary>
/// 视频导出 — 从 1.1.x <c>core/video_exporter.py</c> 的 <c>VideoExporter</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 流程：解析 UST → 由当前设置组装渲染配置 → 写 <c>.uprd</c> → 调用渲染器逐帧渲染出无声 MP4
/// →（可选）用 ffmpeg 混入伴奏。任一步失败或取消都清理半成品，不留打不开的 MP4
/// 与指向无效视频的 <c>.uprd</c>。
/// </para>
/// <para>
/// <b>时序与播放器一致</b>（这是 1.1.x 重点对齐过的行为）：以「音频播完」为结束边界，
/// 音符内容结束而音频未播完的区间靠**补一个尾部休止音符</b>让渲染器显示空拍文字，
/// 音频播完后进入结束文字并保留 1 秒。
/// </para>
/// </remarks>
internal sealed class VideoExporter
{
    /// <summary>一拍（四分音符）的 tick 数，与播放器 / 渲染器一致。</summary>
    private const int TicksPerQuarterNote = 480;

    /// <summary>尾部休止音符的段编号（便于在渲染器日志里辨认）。</summary>
    private const string PaddingNoteIndex = "EXPORT_PAD";

    /// <summary>时长探测阶段的超时。</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    private readonly SettingsManager _settings;
    private readonly UstFileReader _parser;
    private readonly UplrProjectIO _projectIo;
    private readonly IVideoMuxer _muxer;

    /// <summary>创建导出器。</summary>
    /// <param name="settings">设置管理器。</param>
    /// <param name="parser">UST 解析器。</param>
    /// <param name="projectIo">工程 IO（用于写 .uprd）。</param>
    /// <param name="muxer">音频混流器；传 <see langword="null"/> 用 ffmpeg 实现。</param>
    internal VideoExporter(
        SettingsManager settings,
        UstFileReader parser,
        UplrProjectIO projectIo,
        IVideoMuxer? muxer = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(projectIo);

        _settings = settings;
        _parser = parser;
        _projectIo = projectIo;
        _muxer = muxer ?? new FfmpegMuxer(settings.ProgramRoot);
    }

    /// <summary>
    /// 把当前工程渲染为 MP4，并写入对应的 <c>.uprd</c> 工程文件。
    /// </summary>
    /// <param name="outputPath">输出 MP4 路径。</param>
    /// <param name="width">画面宽。</param>
    /// <param name="height">画面高。</param>
    /// <param name="fps">帧率。</param>
    /// <param name="muxAudio">是否把伴奏混入视频。</param>
    /// <param name="progressCallback">进度回调（千分比 0..1000）。</param>
    /// <param name="cancelCheck">取消检查；返回 <see langword="true"/> 时提前终止。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入的 <c>.uprd</c> 路径。</returns>
    /// <exception cref="FileNotFoundException">UST 文件缺失。</exception>
    /// <exception cref="RendererException">渲染器缺失或渲染失败。</exception>
    /// <exception cref="OperationCanceledException">被取消。</exception>
    internal async Task<string> RenderAsync(
        string outputPath,
        int width,
        int height,
        int fps,
        bool muxAudio,
        Action<int>? progressCallback = null,
        Func<bool>? cancelCheck = null,
        CancellationToken cancellationToken = default)
    {
        var mp4Path = EnsureMp4Extension(outputPath);
        ValidateVideoParameters(width, height, fps);

        var coreUst = ParseUst();

        // 以「音频播完」为结束边界（无音频则按音符内容总长）
        var endSeconds = await ResolveEndSecondsAsync(cancelCheck, cancellationToken).ConfigureAwait(false);
        var renderUst = PadTrailingRest(coreUst, endSeconds);
        var totalFrames = FramesFor(endSeconds, fps);

        var uprdPath = UprdPathFor(mp4Path);

        try
        {
            _projectIo.ExportUprd(uprdPath, width, height, fps);
            AppLogger.Info($"已写入 .uprd 工程：{uprdPath}");

            DriveRenderer(renderUst, mp4Path, width, height, fps, totalFrames,
                progressCallback, cancelCheck, cancellationToken);

            if (muxAudio && !string.IsNullOrWhiteSpace(MusicPath))
            {
                await _muxer.MuxAsync(mp4Path, MusicPath, cancelCheck, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 任一步失败 / 取消都清理半成品，避免残留打不开的 MP4 与指向无效视频的 .uprd
            AppLogger.Warning($"导出中止，清理半成品文件：{mp4Path}");
            TryDelete(mp4Path);
            TryDelete(mp4Path + ".mux.tmp.mp4");
            TryDelete(uprdPath);
            throw;
        }

        return uprdPath;
    }

    /// <summary>混流器是否可用（UI 可据此提示用户）。</summary>
    internal bool CanMuxAudio => _muxer.IsAvailable;

    /// <summary>伴奏文件路径。</summary>
    private string MusicPath => _settings.Project.MusicPath.Trim();

    // ===================== 数据准备 =====================

    private UstInfo ParseUst()
    {
        var ustPath = _settings.File.UstPath.Trim();

        if (string.IsNullOrEmpty(ustPath) || !File.Exists(ustPath))
        {
            throw new FileNotFoundException($"UST 文件不存在或未选择：{(ustPath.Length == 0 ? "（空）" : ustPath)}");
        }

        return _parser.Parse(ustPath, _settings.File.Encoding);
    }

    /// <summary>确保输出路径以 <c>.mp4</c> 结尾。</summary>
    /// <param name="outputPath">用户给的路径。</param>
    /// <returns>规范化后的路径。</returns>
    internal static string EnsureMp4Extension(string outputPath)
    {
        var path = (outputPath ?? string.Empty).Trim();
        return path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? path : path + ".mp4";
    }

    /// <summary>视频参数必须为正整数（否则后续可能除零）。</summary>
    /// <param name="width">宽。</param>
    /// <param name="height">高。</param>
    /// <param name="fps">帧率。</param>
    internal static void ValidateVideoParameters(int width, int height, int fps)
    {
        if (width <= 0 || height <= 0 || fps <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"视频参数必须为正数：width={width}, height={height}, fps={fps}");
        }
    }

    /// <summary><c>.uprd</c> 路径：与视频同名的同目录文件。</summary>
    /// <param name="outputPath">视频路径。</param>
    /// <returns>uprd 路径。</returns>
    internal static string UprdPathFor(string outputPath) =>
        Path.ChangeExtension(outputPath, ".uprd");

    /// <summary>音符内容的时长（秒）：每音符长度下限为 1 tick（与渲染器一致）。</summary>
    /// <param name="ust">UST 数据。</param>
    /// <returns>秒数。</returns>
    internal static double ContentSeconds(UstInfo ust)
    {
        ArgumentNullException.ThrowIfNull(ust);

        var ticksPerSecond = ust.Tempo * TicksPerQuarterNote / 60.0;
        if (ticksPerSecond <= 0)
        {
            return 0.0;
        }

        long totalTicks = 0;
        foreach (var note in ust.Notes)
        {
            totalTicks += Math.Max(Math.Max(note.Length, 0), 1);
        }

        return totalTicks / ticksPerSecond;
    }

    /// <summary>
    /// 在音符末尾补一个休止音符，覆盖 [内容结束, 音频结束] 的空拍区间。
    /// </summary>
    /// <param name="ust">原始 UST。</param>
    /// <param name="endSeconds">结束边界（秒）。</param>
    /// <returns>补过尾部休止的 UST（无需补时返回原对象）。</returns>
    /// <remarks>
    /// 播放器在「内容结束但音频未播完」区间显示空拍文字；导出要复现同一画面，
    /// 就得让渲染器在这段时间里也有内容可显示——补一个 <c>lyric=R</c> 的尾部音符即可。
    /// </remarks>
    internal static UstInfo PadTrailingRest(UstInfo ust, double endSeconds)
    {
        ArgumentNullException.ThrowIfNull(ust);

        var contentSeconds = ContentSeconds(ust);
        if (endSeconds <= contentSeconds)
        {
            return ust;
        }

        var ticksPerSecond = ust.Tempo * TicksPerQuarterNote / 60.0;
        if (ticksPerSecond <= 0)
        {
            return ust;
        }

        var trailingTicks = (int)Math.Round(
            (endSeconds - contentSeconds) * ticksPerSecond, MidpointRounding.AwayFromZero);

        if (trailingTicks <= 0)
        {
            return ust;
        }

        var notes = new List<NoteInfo>(ust.Notes.Count + 1);
        notes.AddRange(ust.Notes);
        notes.Add(new NoteInfo
        {
            Index = PaddingNoteIndex,
            Length = trailingTicks,
            Lyric = "R",
            NoteNumber = 60,
        });

        return new UstInfo
        {
            Version = ust.Version,
            Tempo = ust.Tempo,
            Tracks = ust.Tracks,
            Notes = notes,
        };
    }

    /// <summary>总帧数 = 结束边界 × 帧率 + 1 秒结束画面。</summary>
    /// <param name="endSeconds">结束边界（秒）。</param>
    /// <param name="fps">帧率。</param>
    /// <returns>帧数。</returns>
    internal static int FramesFor(double endSeconds, int fps)
    {
        var baseFrames = (int)Math.Ceiling(endSeconds * fps);
        return Math.Max(baseFrames + fps, 1);
    }

    /// <summary>读取 LRC 文本（多编码回退）；不存在返回 <see langword="null"/>。</summary>
    /// <returns>LRC 文本或 <see langword="null"/>。</returns>
    private string? ReadLrcText()
    {
        var lrcPath = _settings.Player.LrcPath.Trim();

        if (string.IsNullOrEmpty(lrcPath) || !File.Exists(lrcPath))
        {
            return null;
        }

        return ReadTextWithEncodingFallback(lrcPath);
    }

    /// <summary>读取伴奏音频时长（秒）；无伴奏 / 无 ffprobe / 失败时返回 0。</summary>
    /// <param name="cancelCheck">取消检查。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>时长（秒）。</returns>
    private async Task<double> AudioDurationSecondsAsync(
        Func<bool>? cancelCheck,
        CancellationToken cancellationToken)
    {
        var musicPath = MusicPath;
        if (string.IsNullOrEmpty(musicPath) || !File.Exists(musicPath))
        {
            return 0.0;
        }

        var ffprobe = ExternalToolLocator.Find("ffprobe", _settings.ProgramRoot);
        if (ffprobe is null)
        {
            return 0.0;
        }

        string[] arguments =
        [
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            musicPath,
        ];

        try
        {
            var result = await CancellableProcess.RunAsync(
                ffprobe, arguments, cancelCheck, ProbeTimeout, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                AppLogger.Warning($"ffprobe 读取音频时长失败（退出码 {result.ExitCode}）");
                return 0.0;
            }

            var text = result.StandardOutput.Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : 0.0;
        }
        catch (OperationCanceledException)
        {
            // 用户取消必须向上传播，不能被吞成「时长未知」
            throw;
        }
        catch (TimeoutException)
        {
            AppLogger.Warning("ffprobe 读取音频时长超时");
            return 0.0;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"ffprobe 读取音频时长失败：{exception.Message}");
            return 0.0;
        }
    }

    /// <summary>渲染结束边界（秒）。</summary>
    /// <param name="cancelCheck">取消检查。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>秒数。</returns>
    private async Task<double> ResolveEndSecondsAsync(
        Func<bool>? cancelCheck,
        CancellationToken cancellationToken)
    {
        var coreUst = ParseUst();
        var contentSeconds = ContentSeconds(coreUst);
        var audioSeconds = await AudioDurationSecondsAsync(cancelCheck, cancellationToken).ConfigureAwait(false);

        return audioSeconds > 0 ? Math.Max(contentSeconds, audioSeconds) : contentSeconds;
    }

    // ===================== 渲染驱动 =====================

    /// <summary>驱动渲染器逐帧渲染。</summary>
    private void DriveRenderer(
        UstInfo renderUst,
        string outputPath,
        int width,
        int height,
        int fps,
        int totalFrames,
        Action<int>? progressCallback,
        Func<bool>? cancelCheck,
        CancellationToken cancellationToken)
    {
        var parameters = _settings.BuildLaunchParams(renderUst);
        var config = RenderConfig.Build(parameters, width, height, fps, outputPath);
        var ustJson = RenderConfig.BuildUstJson(renderUst);

        using var renderer = UplRenderContext.Create();

        // 渲染器的编码器只从 PATH 查找 ffmpeg；内置版本位于 <程序目录>/ffmpeg，
        // 必须在 begin_export 前临时加入 PATH。
        using var pathScope = BundledFfmpegPathScope.Enter(_settings.ProgramRoot);

        renderer.SetConfig(config);
        renderer.SetUstText(ustJson);

        if (ReadLrcText() is { } lrcText)
        {
            renderer.SetLrcText(lrcText);
        }

        renderer.SetProgressCallback(progressCallback);

        renderer.BeginExport();
        AppLogger.Info($"开始渲染视频：{outputPath}（fps={fps}，总帧={totalFrames}）");

        for (var index = 0; index < totalFrames; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (cancelCheck?.Invoke() ?? false)
            {
                throw new OperationCanceledException("导出已取消");
            }

            renderer.RenderFrame(index / (double)fps);
        }

        renderer.EndExport();
        AppLogger.Info("视频渲染完成（无声 MP4）");
    }

    // ===================== 辅助 =====================

    private static string ReadTextWithEncodingFallback(string path)
    {
        foreach (var encoding in EncodingsToTry())
        {
            try
            {
                return File.ReadAllText(path, encoding);
            }
            catch (DecoderFallbackException)
            {
                // 换下一个编码
            }
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static List<Encoding> EncodingsToTry()
    {
        var list = new List<Encoding>
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
        };

        foreach (var name in (string[])["gbk", "gb2312", "shift-jis"])
        {
            try
            {
                list.Add(Encoding.GetEncoding(
                    name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
            }
            catch (ArgumentException)
            {
                // 该代码页不可用：跳过
            }
        }

        return list;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"清理半成品文件失败：{path}（{exception.Message}）");
        }
    }
}
