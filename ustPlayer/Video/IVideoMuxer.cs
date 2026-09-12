using System;
using System.Collections.Generic;
using System.IO;

using UstPlayer.Diagnostics;

namespace UstPlayer.Video;

/// <summary>
/// 外部工具（ffmpeg / ffprobe）的定位 — 从 1.1.x <c>video_exporter.py</c> 的
/// <c>_find_tool</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 查找顺序：<c>&lt;程序目录&gt;/ffmpeg/</c> 子目录 → 程序根目录 → 系统 PATH。
/// 打包时内置版本放在 <c>ffmpeg/</c> 子目录，用户因此不必自行安装 FFmpeg。
/// </para>
/// <para>
/// 平台文件名差异：Windows 是 <c>ffmpeg.exe</c>，macOS / Linux 是 <c>ffmpeg</c>。
/// 两套都尝试，避免为平台写条件编译。
/// </para>
/// <para>
/// <b>返回绝对路径</b>：Windows 上会按当前目录搜索可执行文件，
/// 相对路径 / 裸名有被同名文件劫持的风险（1.1.x 已就此加固）。
/// </para>
/// </remarks>
internal static class ExternalToolLocator
{
    /// <summary>内置工具的子目录名（与打包布局一致）。</summary>
    internal const string BundledDirectoryName = "ffmpeg";

    /// <summary>
    /// 查找外部工具。
    /// </summary>
    /// <param name="name">工具名（<c>ffmpeg</c> / <c>ffprobe</c>）。</param>
    /// <param name="programRoot">程序根目录。</param>
    /// <returns>可执行文件的绝对路径；未找到返回 <see langword="null"/>。</returns>
    internal static string? Find(string name, string programRoot)
    {
        foreach (var candidate in CandidatePaths(name, programRoot))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return FindOnPath(name);
    }

    /// <summary>按优先级枚举内置候选路径。</summary>
    /// <param name="name">工具名。</param>
    /// <param name="programRoot">程序根目录。</param>
    /// <returns>候选路径。</returns>
    private static IEnumerable<string> CandidatePaths(string name, string programRoot)
    {
        foreach (var fileName in PlatformFileNames(name))
        {
            yield return Path.Combine(programRoot, BundledDirectoryName, fileName);
            yield return Path.Combine(programRoot, fileName);
        }
    }

    /// <summary>工具名在各平台的候选文件名。</summary>
    /// <param name="name">工具名。</param>
    /// <returns>候选文件名。</returns>
    private static IEnumerable<string> PlatformFileNames(string name)
    {
        // 先试平台惯用写法，再试另一种：同一份产物目录里两种都可能出现
        if (OperatingSystem.IsWindows())
        {
            yield return $"{name}.exe";
            yield return name;
        }
        else
        {
            yield return name;
            yield return $"{name}.exe";
        }
    }

    /// <summary>在 PATH 中查找。</summary>
    /// <param name="name">工具名。</param>
    /// <returns>绝对路径；未找到返回 <see langword="null"/>。</returns>
    private static string? FindOnPath(string name)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var fileName in PlatformFileNames(name))
            {
                string candidate;

                try
                {
                    candidate = Path.Combine(directory.Trim(), fileName);
                }
                catch (ArgumentException)
                {
                    // PATH 里有非法字符的条目：跳过
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }
}

/// <summary>
/// 伴奏混流 — 把无声 MP4 与音频合成为一个带声视频。
/// </summary>
/// <remarks>
/// 抽成接口是为跨平台留的余地：桌面端用 <see cref="FfmpegMuxer"/>（外部 ffmpeg 进程），
/// 而**移动端没有子进程可用**，Android 头接入时需换用平台编码器实现
/// （或把混流标记为不可用）。解码职责不在本接口内。
/// </remarks>
internal interface IVideoMuxer
{
    /// <summary>混流器是否可用（例如 ffmpeg 是否存在）。</summary>
    /// <returns>可用返回 <see langword="true"/>。</returns>
    bool IsAvailable { get; }

    /// <summary>
    /// 把音频混入视频（原地替换）。
    /// </summary>
    /// <param name="videoPath">视频路径；成功后会被带声版本覆盖。</param>
    /// <param name="audioPath">音频路径。</param>
    /// <param name="cancelCheck">取消检查。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="InvalidOperationException">混流器不可用或 ffmpeg 失败。</exception>
    System.Threading.Tasks.Task MuxAsync(
        string videoPath,
        string audioPath,
        Func<bool>? cancelCheck,
        System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>
/// 用外部 ffmpeg 混流（视频轨道复制、音频转 AAC）。
/// </summary>
/// <remarks>
/// 失败时把 ffmpeg 的 stderr 尾部附进错误消息——否则用户只看到「退出码 1」，
/// 完全无从排查（1.1.x 已如此处理）。
/// </remarks>
internal sealed class FfmpegMuxer : IVideoMuxer
{
    /// <summary>用户可读的失败前缀，UI 据此识别并映射错误码。</summary>
    internal const string FailurePrefix = "音频混流失败";

    /// <summary>混流阶段超时（与 1.1.x 的 3600 秒一致）。</summary>
    private static readonly TimeSpan MuxTimeout = TimeSpan.FromHours(1);

    private readonly string _programRoot;

    /// <summary>创建混流器。</summary>
    /// <param name="programRoot">程序根目录（用于查找内置 ffmpeg）。</param>
    internal FfmpegMuxer(string programRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programRoot);
        _programRoot = programRoot;
    }

    /// <inheritdoc />
    public bool IsAvailable => ExternalToolLocator.Find("ffmpeg", _programRoot) is not null;

    /// <inheritdoc />
    public async System.Threading.Tasks.Task MuxAsync(
        string videoPath,
        string audioPath,
        Func<bool>? cancelCheck,
        System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);

        if (!File.Exists(audioPath))
        {
            // 与 1.1.x 一致：伴奏缺失只是跳过混流，不视为失败
            AppLogger.Warning($"伴奏文件不存在，跳过混流：{audioPath}");
            return;
        }

        var ffmpeg = ExternalToolLocator.Find("ffmpeg", _programRoot)
            ?? throw new InvalidOperationException($"{FailurePrefix}：未找到 ffmpeg");

        // 先写临时文件再替换：混流失败不会破坏已生成的无声音视频
        var tempPath = videoPath + ".mux.tmp.mp4";

        try
        {
            var result = await CancellableProcess.RunAsync(
                    ffmpeg, BuildArguments(videoPath, audioPath, tempPath), cancelCheck, MuxTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                var detail = string.IsNullOrEmpty(result.StderrTail) ? string.Empty : $"：{result.StderrTail}";
                throw new InvalidOperationException($"{FailurePrefix}：ffmpeg 退出码 {result.ExitCode}{detail}");
            }

            File.Move(tempPath, videoPath, overwrite: true);
            AppLogger.Info($"已混入伴奏音频：{videoPath}");
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException($"{FailurePrefix}：无法启动 ffmpeg", exception);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// 构造 ffmpeg 参数。
    /// </summary>
    /// <param name="videoPath">无声视频路径。</param>
    /// <param name="audioPath">音频路径。</param>
    /// <param name="outputPath">输出路径（临时文件）。</param>
    /// <returns>参数序列。</returns>
    /// <remarks>
    /// 抽成独立方法是为了可测：本机与 CI 未必装有 ffmpeg（不是项目依赖），
    /// 但「参数是否写对」必须能被验证——视频轨道 <c>copy</c>（不重编码，快且无损）、
    /// 音频转 AAC、显式 <c>-map</c> 选流、<c>+faststart</c> 便于网页播放。
    /// </remarks>
    internal static string[] BuildArguments(string videoPath, string audioPath, string outputPath) =>
    [
        "-y",
        "-i", videoPath,
        "-i", audioPath,
        "-c:v", "copy",
        "-c:a", "aac",
        "-map", "0:v:0",
        "-map", "1:a:0",
        "-movflags", "+faststart",
        outputPath,
    ];

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 清理失败不掩盖原异常
        }
    }
}
