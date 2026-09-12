using System;
using System.IO;

namespace UstPlayer.Projects;

/// <summary>
/// 工程包内路径的安全校验 — 从 1.1.x <c>core/uplr_io.py</c> 的 <c>_safe_join</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 工程文件（<c>.uplr</c> / <c>.uprd</c>）是 ZIP 容器，成员名来自**不可信输入**。
/// 不校验就按名解压会形成 zip slip：成员名 <c>../../x</c> 能把内容写到缓存目录之外。
/// 因此所有成员名与 <c>Info.json</c> 里登记的资源路径都要走这一套校验。
/// </para>
/// <para>
/// 拒绝：空名、绝对路径、盘符前缀、NUL 字符、含 <c>..</c> 组件；
/// 最后再以「解析结果必须落在基目录内」二次确认（防止上面没覆盖到的写法，
/// 例如某些平台把 <c>//</c> 或尾随空格归一化后逃逸）。
/// </para>
/// </remarks>
internal static class ProjectPathSafety
{
    /// <summary>
    /// 校验包内成员名并解析为基目录内的绝对路径。
    /// </summary>
    /// <param name="baseDirectory">解压根目录。</param>
    /// <param name="memberName">ZIP 成员名或 <c>Info.json</c> 中登记的资源名。</param>
    /// <returns>基目录内的绝对路径。</returns>
    /// <exception cref="ProjectFormatException">成员名不安全。</exception>
    internal static string ResolveInside(string baseDirectory, string memberName)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);

        var normalized = (memberName ?? string.Empty).Replace('\\', '/');

        if (normalized.Length == 0 ||
            normalized.StartsWith('/') ||
            normalized.Contains('\0') ||
            HasDrivePrefix(normalized) ||
            HasParentComponent(normalized))
        {
            throw new ProjectFormatException($"工程文件包含不安全路径：{memberName}");
        }

        var baseFull = Path.GetFullPath(baseDirectory);
        var target = Path.GetFullPath(Path.Combine(baseFull, normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsInside(baseFull, target))
        {
            throw new ProjectFormatException($"工程文件包含不安全路径：{memberName}");
        }

        return target;
    }

    /// <summary>成员名是否以 <c>/</c> 结尾（目录条目）。</summary>
    /// <param name="memberName">成员名。</param>
    /// <returns>是目录条目返回 <see langword="true"/>。</returns>
    internal static bool IsDirectoryEntry(string memberName) =>
        (memberName ?? string.Empty).Replace('\\', '/').EndsWith('/');

    /// <summary>
    /// 判断 <paramref name="candidate"/> 是否位于 <paramref name="baseFull"/> 之内。
    /// </summary>
    /// <param name="baseFull">基目录的绝对路径。</param>
    /// <param name="candidate">候选路径的绝对路径。</param>
    /// <returns>位于其内返回 <see langword="true"/>。</returns>
    /// <remarks>
    /// 不用 <see cref="Path.GetRelativePath"/> 的返回值做判断，而是显式比较：
    /// 相对路径以 <c>..</c> 开头或以分隔符开头即越界。不同盘符时
    /// <see cref="Path.GetRelativePath"/> 会返回带盘符的绝对路径，同样判为越界。
    /// </remarks>
    private static bool IsInside(string baseFull, string candidate)
    {
        if (string.Equals(baseFull, candidate, PathComparison))
        {
            return true;
        }

        var relative = Path.GetRelativePath(baseFull, candidate);

        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            return false;
        }

        // 再加一道保险：拼回去必须与候选路径一致
        var roundTrip = Path.GetFullPath(Path.Combine(baseFull, relative));
        return string.Equals(roundTrip, candidate, PathComparison);
    }

    private static bool HasParentComponent(string normalized)
    {
        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDrivePrefix(string normalized) =>
        normalized.Length >= 2 &&
        char.IsAsciiLetter(normalized[0]) &&
        normalized[1] == ':';

    /// <summary>路径比较方式：Windows 不区分大小写，其他平台区分。</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
