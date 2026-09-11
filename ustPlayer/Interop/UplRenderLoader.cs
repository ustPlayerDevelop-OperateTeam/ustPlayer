using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UstPlayer.Interop;

/// <summary>
/// 渲染器原生库的目录查找。
/// </summary>
/// <remarks>
/// 沿用 1.1.x 的约定：优先程序根目录，其次程序根目录下的 <c>renderer/</c> 子目录。
/// 2.0 另外补上「程序集所在目录」与「当前工作目录」两个候选，
/// 让单元测试与开发期运行（输出目录里就有 <c>renderer/</c>）无需额外配置。
/// </remarks>
internal static class UplRenderLoader
{
    /// <summary>渲染器子目录名（与打包布局一致）。</summary>
    internal const string RendererDirectoryName = "renderer";

    /// <summary>按优先级返回渲染器原生库的候选目录（已去重、只含存在的目录）。</summary>
    /// <returns>候选目录列表。</returns>
    internal static IReadOnlyList<string> SearchDirectories()
    {
        var candidates = new List<string>(6);

        AddCandidate(candidates, ProgramRoot());
        AddCandidate(candidates, Path.Combine(ProgramRoot(), RendererDirectoryName));

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        AddCandidate(candidates, assemblyDirectory);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            AddCandidate(candidates, Path.Combine(assemblyDirectory, RendererDirectoryName));
        }

        AddCandidate(candidates, Directory.GetCurrentDirectory());
        AddCandidate(candidates, Path.Combine(Directory.GetCurrentDirectory(), RendererDirectoryName));

        return candidates;
    }

    /// <summary>渲染器原生库是否可用（用于在缺失时给出可读提示或跳过测试）。</summary>
    /// <returns>找到则返回 <see langword="true"/>。</returns>
    internal static bool IsAvailable()
    {
        foreach (var directory in SearchDirectories())
        {
            if (File.Exists(Path.Combine(directory, NativeLibraryResolver.PlatformFileName)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>按优先级返回第一个存在的渲染器原生库完整路径。</summary>
    /// <returns>文件路径；未找到返回 <see langword="null"/>。</returns>
    internal static string? FindLibraryPath()
    {
        foreach (var directory in SearchDirectories())
        {
            var candidate = Path.Combine(directory, NativeLibraryResolver.PlatformFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 程序根目录：打包后为可执行文件所在目录，开发期为当前工作目录。
    /// </summary>
    /// <returns>目录路径。</returns>
    private static string ProgramRoot()
    {
        // 单文件发布时 Assembly.Location 为空，此时以进程主模块路径为准
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static void AddCandidate(ICollection<string> candidates, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var normalized = Path.GetFullPath(directory);
        foreach (var existing in candidates)
        {
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        candidates.Add(normalized);
    }
}
