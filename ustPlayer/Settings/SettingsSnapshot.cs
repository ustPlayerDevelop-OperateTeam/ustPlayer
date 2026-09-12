using System;
using System.Collections.Generic;
using System.Reflection;

using UstPlayer.Settings.Domains;

namespace UstPlayer.Settings;

/// <summary>
/// 「工程导入会触碰的设置属性」的快照，用于导入失败时整体回滚。
/// </summary>
/// <remarks>
/// <para>
/// 工程导入是**事务化**的：读到一半发现资源缺失或路径不安全时，已赋值的属性必须回到原状，
/// 否则退出时的 <c>WriteSettings</c> 会把半成品配置持久化——用户会以为导入失败无副作用，
/// 实际设置已被悄悄改坏（1.1.x 踩过这个坑）。
/// </para>
/// <para>
/// 属性清单集中在此处（对应 1.1.x 的 <c>_IMPORT_TOUCHED</c>），
/// 并由 <c>SettingsSnapshotTests</c> 断言「清单与 <c>ApplyInfoJson</c> 实际写入的属性集合一致」——
/// 漏列一个属性会让它在回滚时残留，而这类遗漏不会有任何报错。
/// </para>
/// </remarks>
internal sealed class SettingsSnapshot
{
    /// <summary>会被导入触碰的属性清单：子域名 → 属性名。</summary>
    internal static readonly IReadOnlyDictionary<Type, string[]> TouchedProperties =
        new Dictionary<Type, string[]>
        {
            [typeof(FileSettings)] = ["Encoding", "UstPath", "CurveShow"],
            [typeof(ProjectSettings)] =
                ["ProjectName", "MusicPath", "SongName", "SongAuthor", "UstAuthor"],
            [typeof(DisplaySettings)] =
            [
                "ShowBpm", "ShowPlayTime", "ShowSongName", "ShowSongAuthor", "ShowUstAuthor",
                "Fullscreen", "ShowLyric", "ShowNoteName", "ShowUstLyric", "ShowCopyright",
                "FontNote", "FontUstLyric", "FontLrc", "FontOther", "CustomFontPaths",
            ],
            [typeof(ColorSettings)] =
            [
                "BackgroundColor", "NoteColor", "LyricColor", "LyricTextColor",
                "OtherTextColor", "PitchCurveColor",
            ],
            [typeof(PlayerSettings)] =
            [
                "LyricPosition", "LrcPath", "SilentDisplay", "SilentCustomText",
                "EndDisplay", "EndCustomText", "PitchPlaceholder", "PitchCustomText",
            ],
        };

    private readonly List<(PropertyInfo Property, object Target, object? Value)> _entries = [];

    /// <summary>
    /// 捕获快照。
    /// </summary>
    /// <param name="settings">设置管理器。</param>
    /// <returns>快照。</returns>
    internal static SettingsSnapshot Capture(SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var snapshot = new SettingsSnapshot();

        foreach (var (domainType, propertyNames) in TouchedProperties)
        {
            var target = ResolveDomain(settings, domainType);
            if (target is null)
            {
                continue;
            }

            foreach (var propertyName in propertyNames)
            {
                var property = domainType.GetProperty(propertyName);
                if (property is null)
                {
                    // 清单里写了不存在的属性：说明清单与实现脱节，不能静默忽略
                    throw new InvalidOperationException(
                        $"设置快照清单与实现不一致：{domainType.Name}.{propertyName} 不存在");
                }

                snapshot._entries.Add((property, target, property.GetValue(target)));
            }
        }

        return snapshot;
    }

    /// <summary>按快照恢复全部属性。</summary>
    internal void Restore()
    {
        foreach (var (property, target, value) in _entries)
        {
            property.SetValue(target, value);
        }
    }

    /// <summary>快照覆盖的属性数量（供测试断言清单完整性）。</summary>
    internal int Count => _entries.Count;

    private static object? ResolveDomain(SettingsManager settings, Type domainType)
    {
        if (domainType == typeof(FileSettings))
        {
            return settings.File;
        }

        if (domainType == typeof(ProjectSettings))
        {
            return settings.Project;
        }

        if (domainType == typeof(DisplaySettings))
        {
            return settings.Display;
        }

        if (domainType == typeof(ColorSettings))
        {
            return settings.Color;
        }

        if (domainType == typeof(PlayerSettings))
        {
            return settings.Player;
        }

        return null;
    }
}
