using System;
using System.IO;

using UstPlayer.Diagnostics;
using UstPlayer.Models;
using UstPlayer.Settings;
using UstPlayer.Settings.Domains;

namespace UstPlayer.Settings;

/// <summary>
/// 设置门面 — 从 1.1.x <c>core/settings_manager.py</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 只负责**组装**七个设置子域并编排读写，自身不持有任何配置属性。
/// UI 通过 <c>ctx.settings.Display.ShowBpm</c> 这类路径访问。
/// </para>
/// <para>
/// 与 1.1.x 的差异：设置文件路径由本类持有（1.1.x 由 <c>SettingsStore</c> 内部决定），
/// 便于测试注入与将来的便携模式；<c>program_root</c> 改为读 <see cref="ProgramPaths"/>。
/// </para>
/// </remarks>
internal sealed class SettingsManager
{
    private readonly SettingsStore _store;

    /// <summary>创建设置管理器并立即读取设置。</summary>
    /// <param name="settingsPath">设置文件路径；传 <see langword="null"/> 用默认策略解析。</param>
    internal SettingsManager(string? settingsPath = null)
    {
        _store = new SettingsStore(settingsPath);

        Project = new ProjectSettings();
        File = new FileSettings();
        Display = new DisplaySettings();
        Color = new ColorSettings();
        Player = new PlayerSettings();
        Language = new LanguageSettings();
        Theme = new ThemeSettings();

        // 上次打开的目录默认指向桌面（与 1.1.x 一致）
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        LastOpenDirectory = desktop;
        LastExportDirectory = desktop;

        ReadSettings();
    }

    /// <summary>项目信息子域。</summary>
    internal ProjectSettings Project { get; }

    /// <summary>文件与编码子域。</summary>
    internal FileSettings File { get; }

    /// <summary>显示开关与字体子域。</summary>
    internal DisplaySettings Display { get; }

    /// <summary>颜色子域。</summary>
    internal ColorSettings Color { get; }

    /// <summary>播放器样式子域。</summary>
    internal PlayerSettings Player { get; }

    /// <summary>语言子域。</summary>
    internal LanguageSettings Language { get; }

    /// <summary>主题子域。</summary>
    internal ThemeSettings Theme { get; }

    /// <summary>程序根目录（缓存目录与内置资源都相对它解析）。</summary>
    internal string ProgramRoot => ProgramPaths.ProgramRoot;

    /// <summary>当前设置文件路径。</summary>
    internal string SettingsPath => _store.SettingsPath;

    /// <summary>上次打开工程的目录。</summary>
    internal string LastOpenDirectory { get; set; }

    /// <summary>上次导出视频的目录。</summary>
    internal string LastExportDirectory { get; set; }

    /// <summary>
    /// 读取设置并恢复全部配置。
    /// </summary>
    /// <remarks>
    /// 读取失败（文件损坏等）不抛异常：回退默认值并记录日志——坏掉的配置文件
    /// 不该让程序无法启动（与 1.1.x 一致）。
    /// </remarks>
    internal void ReadSettings()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        try
        {
            var config = _store.Load();

            if (config.IsEmpty)
            {
                LastOpenDirectory = desktop;
                LastExportDirectory = desktop;
                return;
            }

            var pathGroup = config.GetSection(SettingsSections.Path);
            if (pathGroup is not null)
            {
                var open = pathGroup.GetNode("last_open_dir") is { } openNode &&
                           openNode.GetValueKind() == System.Text.Json.JsonValueKind.String
                    ? openNode.GetValue<string>()
                    : desktop;

                var export = pathGroup.GetNode("last_export_dir") is { } exportNode &&
                             exportNode.GetValueKind() == System.Text.Json.JsonValueKind.String
                    ? exportNode.GetValue<string>()
                    : desktop;

                // 目录已不存在（U 盘拔掉、目录改名）时回退桌面
                LastOpenDirectory = Directory.Exists(open) ? open : desktop;
                LastExportDirectory = Directory.Exists(export) ? export : desktop;
            }

            Project.ReadFrom(config);
            File.ReadFrom(config);
            Display.ReadFrom(config);
            Color.ReadFrom(config);
            Player.ReadFrom(config);
            Language.ReadFrom(config);
            Theme.ReadFrom(config);
        }
        catch (Exception exception)
        {
            LastOpenDirectory = desktop;
            LastExportDirectory = desktop;
            AppLogger.Error("读取配置文件失败，使用默认值", exception);
        }
    }

    /// <summary>把全部设置写回配置文件（退出时保存，以便重启恢复）。</summary>
    internal void WriteSettings()
    {
        try
        {
            var config = new SettingsConfig(new System.Text.Json.Nodes.JsonObject());

            var pathGroup = config.GetSection(SettingsSections.Path, createIfMissing: true)!;
            pathGroup.SetString("last_open_dir", LastOpenDirectory);
            pathGroup.SetString("last_export_dir", LastExportDirectory);

            Project.WriteTo(config);
            File.WriteTo(config);
            Display.WriteTo(config);
            Color.WriteTo(config);
            Player.WriteTo(config);
            Language.WriteTo(config);
            Theme.WriteTo(config);

            _store.Save(config);
        }
        catch (Exception exception)
        {
            AppLogger.Error("写入配置文件失败", exception);
        }
    }

    /// <summary>
    /// 组装播放器的启动参数。
    /// </summary>
    /// <param name="ustInfo">UST 解析结果。</param>
    /// <returns>播放器启动参数。</returns>
    /// <remarks>
    /// 注意 <c>curve_show</c> 取自 <see cref="FileSettings"/>（1.1.x 即如此），
    /// 而非显示子域。
    /// </remarks>
    internal PlayerLaunchParams BuildLaunchParams(UstInfo ustInfo)
    {
        ArgumentNullException.ThrowIfNull(ustInfo);

        return new PlayerLaunchParams
        {
            Ust = ustInfo,
            Show = new ShowConfig
            {
                Bpm = Display.ShowBpm,
                PlayTime = Display.ShowPlayTime,
                SongName = Display.ShowSongName,
                SongAuthor = Display.ShowSongAuthor,
                UstAuthor = Display.ShowUstAuthor,
                Lyric = Display.ShowLyric,
                CurveShow = File.CurveShow,
                NoteName = Display.ShowNoteName,
                UstLyric = Display.ShowUstLyric,
                Copyright = Display.ShowCopyright,
                FontNote = Display.FontNote,
                FontUstLyric = Display.FontUstLyric,
                FontLrc = Display.FontLrc,
                FontOther = Display.FontOther,
                CustomFontPaths = [.. Display.CustomFontPaths],
            },
            Project = new ProjectInfo
            {
                ProjectName = Project.ProjectName,
                SongName = Project.SongName,
                SongAuthor = Project.SongAuthor,
                UstAuthor = Project.UstAuthor,
            },
            Style = new PlayerStyle
            {
                BackgroundColor = Color.BackgroundColor,
                NoteColor = Color.NoteColor,
                LyricColor = Color.LyricColor,
                LyricTextColor = Color.LyricTextColor,
                OtherTextColor = Color.OtherTextColor,
                LyricPosition = Player.LyricPosition,
                Fullscreen = Display.Fullscreen,
                LrcPath = Player.LrcPath,
                MusicPath = Project.MusicPath,
                SilentDisplay = Player.SilentDisplay,
                SilentCustomText = Player.SilentCustomText,
                EndDisplay = Player.EndDisplay,
                EndCustomText = Player.EndCustomText,
                PitchPlaceholder = Player.PitchPlaceholder,
                PitchCustomText = Player.PitchCustomText,
                PitchCurveColor = Color.PitchCurveColor,
                AppVersion = AppInfo.Version,
            },
        };
    }

    /// <summary>
    /// 快照「会被工程导入触碰」的全部设置属性，供失败回滚。
    /// </summary>
    /// <returns>快照。</returns>
    internal SettingsSnapshot CreateSnapshot() => SettingsSnapshot.Capture(this);

    /// <summary>按快照恢复设置（setter 会触发通知，UI 因此同步回旧值）。</summary>
    /// <param name="snapshot">快照。</param>
    internal void RestoreSnapshot(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Restore();
    }
}
