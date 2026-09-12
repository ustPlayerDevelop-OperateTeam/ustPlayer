using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UstPlayer.Diagnostics;

namespace UstPlayer.Video;

/// <summary>
/// 可取消的外部进程执行 — 从 1.1.x <c>video_exporter.py</c> 的 <c>_run_cancellable</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不能用 <c>Process.WaitForExit</c> 直等</b>：进入 ffprobe / ffmpeg 阶段后，
/// 混流最长可达一小时；直等意味着用户点了取消也要等子进程自然结束才生效（1.1.x 踩过，
/// 表现为「取消按钮没反应」）。这里改为轮询：每 200ms 检查一次取消与超时。
/// </para>
/// <para>
/// 参数通过 <see cref="ProcessStartInfo.ArgumentList"/> 传递，由 .NET 负责转义
/// （而不是自己拼命令行）——路径含空格或引号时不会拼接出错。
/// </para>
/// </remarks>
internal static class CancellableProcess
{
    /// <summary>取消检查的轮询间隔。
    /// 与 1.1.x 的 <c>_PROC_POLL_INTERVAL</c> 一致：取消请求最迟在此延迟后生效。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>错误信息中保留的 stderr 尾部长度。</summary>
    private const int StderrTailLength = 2000;

    /// <summary>执行结果。</summary>
    /// <param name="ExitCode">退出码。</param>
    /// <param name="StandardOutput">标准输出全文。</param>
    /// <param name="StderrTail">stderr 尾部文本（未捕获时为空）。</param>
    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StderrTail);

    /// <summary>
    /// 运行外部程序，期间持续响应取消与超时。
    /// </summary>
    /// <param name="executable">可执行文件路径（应为绝对路径，避免被当前目录劫持）。</param>
    /// <param name="arguments">参数序列。</param>
    /// <param name="cancelCheck">取消检查；返回 <see langword="true"/> 时终止并抛出。</param>
    /// <param name="timeout">超时。</param>
    /// <param name="cancellationToken">取消令牌（与 <paramref name="cancelCheck"/> 二者取或）。</param>
    /// <returns>退出码与 stderr 尾部。</returns>
    /// <exception cref="TimeoutException">超时。</exception>
    /// <exception cref="OperationCanceledException">被取消。</exception>
    /// <exception cref="FileNotFoundException">无法启动该可执行文件。</exception>
    internal static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        Func<bool>? cancelCheck,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new FileNotFoundException($"无法启动外部程序：{executable}");
        }

        // 异步读干两个管道：不读会填满管道缓冲区导致子进程阻塞（经典死锁）
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var deadline = DateTime.UtcNow + timeout;

        while (!process.WaitForExit(milliseconds: (int)PollInterval.TotalMilliseconds))
        {
            if (cancellationToken.IsCancellationRequested || (cancelCheck?.Invoke() ?? false))
            {
                KillQuietly(process);
                throw new OperationCanceledException("导出已取消");
            }

            if (DateTime.UtcNow >= deadline)
            {
                KillQuietly(process);
                throw new TimeoutException($"{Path.GetFileName(executable)} 处理超时");
            }
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdout, Tail(stderr, StderrTailLength));
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception)
        {
            // 终止失败不掩盖上层的取消 / 超时异常
        }
    }

    private static string Tail(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[^maxLength..];
    }
}
