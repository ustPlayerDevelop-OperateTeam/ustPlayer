using System;
using System.Collections.Generic;
using System.IO;

namespace UstPlayer.Projects;

/// <summary>
/// 导出时要打进工程包的资源清单（设置属性名 → 包内文件名）。
/// </summary>
/// <remarks>
/// 与 1.1.x <c>_collect_members</c> 一致：只收集**存在且非空**的三类资源
/// （UST / LRC / 伴奏），并按「原文件名 → <c>_2</c> → <c>_3</c>」去重——
/// 例如工程同时引用两个都叫 <c>a.ust</c> 的文件时，第二个会落成 <c>a_2.ust</c>。
/// 去重按**大小写不敏感**比较（ZIP 内成员名在 Windows 上不区分大小写）。
/// </remarks>
internal sealed class ResourceMembers
{
    /// <summary>设置属性名 → 包内文件名。</summary>
    private readonly Dictionary<string, string> _members = new(StringComparer.Ordinal);

    /// <summary>已占用的包内文件名（小写）。</summary>
    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);

    /// <summary>收集到的资源数量。</summary>
    internal int Count => _members.Count;

    /// <summary>已收集的资源（设置属性名 → 包内文件名）。</summary>
    internal IReadOnlyDictionary<string, string> Items => _members;

    /// <summary>
    /// 收集一个资源。路径为空或文件不存在时忽略（与 1.1.x 一致：缺失的资源不写入包）。
    /// </summary>
    /// <param name="settingName">设置属性名（<c>ust_path</c> / <c>lrc_path</c> / <c>music_path</c>）。</param>
    /// <param name="localPath">本机文件路径。</param>
    /// <returns>确实收集则返回 <see langword="true"/>。</returns>
    internal bool Add(string settingName, string? localPath)
    {
        var path = (localPath ?? string.Empty).Trim();

        if (path.Length == 0 || !File.Exists(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        var uniqueName = MakeUnique(fileName);

        _members[settingName] = uniqueName;
        _usedNames.Add(uniqueName.ToLowerInvariant());
        return true;
    }

    /// <summary>取包内文件名；未收集时返回 <see langword="null"/>（对应 Python 的 <c>or None</c>）。</summary>
    /// <param name="settingName">设置属性名。</param>
    /// <returns>包内文件名或 <see langword="null"/>。</returns>
    internal string? NameOrNull(string settingName) =>
        _members.TryGetValue(settingName, out var name) ? name : null;

    /// <summary>取包内文件名；未收集时返回空串。</summary>
    /// <param name="settingName">设置属性名。</param>
    /// <returns>包内文件名或空串。</returns>
    internal string NameOrEmpty(string settingName) =>
        _members.TryGetValue(settingName, out var name) ? name : string.Empty;

    private string MakeUnique(string fileName)
    {
        if (!_usedNames.Contains(fileName.ToLowerInvariant()))
        {
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; ; index++)
        {
            var candidate = $"{stem}_{index}{extension}";
            if (!_usedNames.Contains(candidate.ToLowerInvariant()))
            {
                return candidate;
            }
        }
    }
}
