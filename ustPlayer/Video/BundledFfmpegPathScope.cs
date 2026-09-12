using System;
using System.Collections.Generic;
using System.IO;

using UstPlayer.Diagnostics;

namespace UstPlayer.Video;

/// <summary>
/// 临时把内置 <c>ffmpeg/</c> 目录加入 <c>PATH</c> 的作用域 — 从 1.1.x
/// <c>video_exporter.py</c> 的 <c>_bundled_ffmpeg_on_path</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：uPlRender 渲染器内部**只从 PATH 查找 ffmpeg**（它不认程序目录），
/// 而 2.0 把内置 ffmpeg 放在 <c>&lt;程序目录&gt;/ffmpeg/</c>。
/// 不在调用 <c>up_begin_export</c> 前把该目录加进 PATH，渲染器就找不到编码器
/// （1.1.x 踩过：视频导出报「找不到 ffmpeg」，而内置版本明明就在程序目录里）。
/// </para>
/// <para>
/// 用法：<c>using var scope = BundledFfmpegPathScope.Enter(root);</c> ——
/// 离开作用域自动还原原 PATH，异常路径也不会污染进程环境。
/// </para>
/// </remarks>
internal sealed class BundledFfmpegPathScope : IDisposable
{
    private readonly string? _originalPath;
    private bool _disposed;

    private BundledFfmpegPathScope(string? originalPath)
    {
        _originalPath = originalPath;
    }

    /// <summary>
    /// 进入作用域：把内置 ffmpeg 目录加到 PATH 最前。
    /// </summary>
    /// <param name="programRoot">程序根目录。</param>
    /// <returns>离开时还原 PATH 的作用域。</returns>
    internal static BundledFfmpegPathScope Enter(string programRoot)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var bundledDirectory = Path.Combine(programRoot, ExternalToolLocator.BundledDirectoryName);

        if (!Directory.Exists(bundledDirectory) || IsAlreadyOnPath(originalPath, bundledDirectory))
        {
            // 没有内置目录，或已在 PATH 上：什么都不做，仍然返回可用的作用域
            return new BundledFfmpegPathScope(originalPath: null);
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            bundledDirectory + Path.PathSeparator + (originalPath ?? string.Empty));

        AppLogger.Info($"已将内置 ffmpeg 目录加入 PATH：{bundledDirectory}");

        return new BundledFfmpegPathScope(originalPath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed || _originalPath is null)
        {
            _disposed = true;
            return;
        }

        _disposed = true;

        // originalPath 为 null 表示原本没有 PATH，此时应移除而不是设成空串
        Environment.SetEnvironmentVariable("PATH", _originalPath);
    }

    private static bool IsAlreadyOnPath(string? pathVariable, string directory)
    {
        if (string.IsNullOrEmpty(pathVariable))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var entry in SplitPath(pathVariable))
        {
            if (string.Equals(entry, directory, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitPath(string pathVariable)
    {
        foreach (var entry in pathVariable.Split(Path.PathSeparator))
        {
            var trimmed = entry.Trim().Trim('"');
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }
}
