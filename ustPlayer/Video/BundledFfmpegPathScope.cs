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

    /// <summary>
    /// 把内置 ffmpeg 目录**永久**加入本进程的 <c>PATH</c>（只加不撤）。
    /// </summary>
    /// <param name="programRoot">程序根目录。</param>
    /// <returns>是否真的加上了（目录不存在或已在 PATH 上时为 <see langword="false"/>）。</returns>
    /// <remarks>
    /// <para>
    /// <b>为什么需要「永久」这一版</b>：<see cref="Enter"/> 是成对进出 PATH 的作用域，
    /// 而渲染器是在 <c>RenderFrame</c> 循环里**按需**启动 ffmpeg 子进程的——
    /// 它读 PATH 的时刻可能在作用域退出之后。于是当两处导出并行时会出现这种交错：
    /// </para>
    /// <list type="number">
    ///   <item>A 进入：PATH = 内置目录;原PATH</item>
    ///   <item>B 进入：看到已在 PATH → 返回空作用域（**不记录原值**）</item>
    ///   <item>B 退出：空作用域，什么都不还原</item>
    ///   <item>A 退出：还原成「原PATH」→ **把内置目录从 PATH 上摘掉了**，
    ///   而 A 的渲染器接下来才去 spawn ffmpeg → 报
    ///   <c>ffmpeg init failed: ffmpeg executable not found in PATH</c></item>
    /// </list>
    /// <para>
    /// 实测表现正是「单独跑必过、全量跑偶发」，且失败的用例每次都可能不同
    /// （谁被摘掉 PATH 谁失败）。进程级环境变量做「进入/退出」这种成对修改，
    /// 在并行场景下本身就是不可靠的。
    /// </para>
    /// <para>
    /// 因此测试程序集在 <c>ModuleInitializer</c> 里调用本方法一次：内置目录从进程启动
    /// 就在 PATH 上，<see cref="Enter"/> 退化成无操作，交错窗口消失。
    /// 生产路径仍用 <see cref="Enter"/>（单次导出、无并发，且不希望长期改写用户的 PATH）。
    /// </para>
    /// </remarks>
    internal static bool EnsureOnPath(string programRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programRoot);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var bundledDirectory = Path.Combine(programRoot, ExternalToolLocator.BundledDirectoryName);

        if (!Directory.Exists(bundledDirectory) || IsAlreadyOnPath(originalPath, bundledDirectory))
        {
            return false;
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            bundledDirectory + Path.PathSeparator + (originalPath ?? string.Empty));

        AppLogger.Info($"内置 ffmpeg 目录已常驻 PATH：{bundledDirectory}");

        return true;
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
