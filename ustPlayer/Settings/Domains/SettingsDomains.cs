using System.Collections.Generic;

using UstPlayer.I18n;

namespace UstPlayer.Settings.Domains;

/// <summary>
/// 项目信息设置（<c>[ProjectSettings]</c>）。
/// </summary>
/// <remarks>工程名 / 曲名 / 曲作者 / 调音师 / 伴奏路径。全部为自由文本，无校验。</remarks>
internal sealed class ProjectSettings : SettingsDomain<ProjectSettings>
{
    private string _projectName = string.Empty;
    private string _songName = string.Empty;
    private string _songAuthor = string.Empty;
    private string _ustAuthor = string.Empty;
    private string _musicPath = string.Empty;

    /// <summary>工程名。</summary>
    public string ProjectName
    {
        get => _projectName;
        set => SetField(ref _projectName, value);
    }

    /// <summary>曲名。</summary>
    public string SongName
    {
        get => _songName;
        set => SetField(ref _songName, value);
    }

    /// <summary>曲作者。</summary>
    public string SongAuthor
    {
        get => _songAuthor;
        set => SetField(ref _songAuthor, value);
    }

    /// <summary>调音师。</summary>
    public string UstAuthor
    {
        get => _ustAuthor;
        set => SetField(ref _ustAuthor, value);
    }

    /// <summary>伴奏文件路径。</summary>
    public string MusicPath
    {
        get => _musicPath;
        set => SetField(ref _musicPath, value);
    }

    /// <inheritdoc />
    public override void ReadFrom(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Project);
        if (group is null)
        {
            return;
        }

        ProjectName = ReadString(group, "project_name", ProjectName);
        SongName = ReadString(group, "song_name", SongName);
        SongAuthor = ReadString(group, "song_author", SongAuthor);
        UstAuthor = ReadString(group, "ust_author", UstAuthor);
        MusicPath = ReadString(group, "music_path", MusicPath);
    }

    /// <inheritdoc />
    public override void WriteTo(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Project, createIfMissing: true)!;

        group.SetString("project_name", ProjectName);
        group.SetString("song_name", SongName);
        group.SetString("song_author", SongAuthor);
        group.SetString("ust_author", UstAuthor);
        group.SetString("music_path", MusicPath);
    }
}

/// <summary>
/// 文件与编码设置（<c>[FileSettings]</c>）。
/// </summary>
internal sealed class FileSettings : SettingsDomain<FileSettings>
{
    /// <summary>编码的默认值：日文 UST 通常为 Shift-JIS。</summary>
    internal const string DefaultEncoding = "Shift-JIS";

    private string _ustPath = string.Empty;
    private string _encoding = DefaultEncoding;
    private bool _curveShow;

    /// <summary>UST 文件路径。</summary>
    public string UstPath
    {
        get => _ustPath;
        set => SetField(ref _ustPath, value);
    }

    /// <summary>UST 文件编码。</summary>
    public string Encoding
    {
        get => _encoding;
        set => SetField(ref _encoding, value);
    }

    /// <summary>是否显示音高曲线。</summary>
    public bool CurveShow
    {
        get => _curveShow;
        set => SetField(ref _curveShow, value);
    }

    /// <inheritdoc />
    public override void ReadFrom(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.File);
        if (group is null)
        {
            return;
        }

        UstPath = ReadString(group, "ust_path", UstPath);
        Encoding = ReadString(group, "encoding", Encoding);
        CurveShow = ReadBool(group, "curve_show", CurveShow);
    }

    /// <inheritdoc />
    public override void WriteTo(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.File, createIfMissing: true)!;

        // 兼容性：curve_show 在 1.1.x 中同时写进 [FileSettings]（设置文件）
        // 与 .uplr 的 display 段，两处都必须保留。
        group.SetString("ust_path", UstPath);
        group.SetString("encoding", Encoding);
        group.SetBool("curve_show", CurveShow);
    }
}

/// <summary>
/// 显示开关与字体设置（<c>[DisplaySettings]</c>）。
/// </summary>
/// <remarks>
/// 默认值刻意与 1.1.x 逐字一致：<c>show_lyric</c> 默认是 <b>关闭</b>
/// （需要用户显式开启 LRC 歌词显示），其余显示项默认开启。
/// </remarks>
internal sealed class DisplaySettings : SettingsDomain<DisplaySettings>
{
    private bool _showBpm = true;
    private bool _showPlayTime = true;
    private bool _showSongName = true;
    private bool _showSongAuthor = true;
    private bool _showUstAuthor = true;
    private bool _fullscreen = true;
    private bool _showLyric;
    private bool _showNoteName = true;
    private bool _showUstLyric = true;
    private bool _showCopyright = true;
    private string _fontNote = string.Empty;
    private string _fontUstLyric = string.Empty;
    private string _fontLrc = string.Empty;
    private string _fontOther = string.Empty;
    private List<string> _customFontPaths = [];

    /// <summary>显示 BPM。</summary>
    public bool ShowBpm
    {
        get => _showBpm;
        set => SetField(ref _showBpm, value);
    }

    /// <summary>显示播放时间。</summary>
    public bool ShowPlayTime
    {
        get => _showPlayTime;
        set => SetField(ref _showPlayTime, value);
    }

    /// <summary>显示曲名。</summary>
    public bool ShowSongName
    {
        get => _showSongName;
        set => SetField(ref _showSongName, value);
    }

    /// <summary>显示曲作者。</summary>
    public bool ShowSongAuthor
    {
        get => _showSongAuthor;
        set => SetField(ref _showSongAuthor, value);
    }

    /// <summary>显示调音师。</summary>
    public bool ShowUstAuthor
    {
        get => _showUstAuthor;
        set => SetField(ref _showUstAuthor, value);
    }

    /// <summary>是否全屏播放。</summary>
    public bool Fullscreen
    {
        get => _fullscreen;
        set => SetField(ref _fullscreen, value);
    }

    /// <summary>显示 LRC 歌词（默认关闭）。</summary>
    public bool ShowLyric
    {
        get => _showLyric;
        set => SetField(ref _showLyric, value);
    }

    /// <summary>显示音名。</summary>
    public bool ShowNoteName
    {
        get => _showNoteName;
        set => SetField(ref _showNoteName, value);
    }

    /// <summary>显示歌字（UST 歌词）。</summary>
    public bool ShowUstLyric
    {
        get => _showUstLyric;
        set => SetField(ref _showUstLyric, value);
    }

    /// <summary>显示底部版权信息。</summary>
    public bool ShowCopyright
    {
        get => _showCopyright;
        set => SetField(ref _showCopyright, value);
    }

    /// <summary>音名字体族（空 = 语言默认）。</summary>
    public string FontNote
    {
        get => _fontNote;
        set => SetField(ref _fontNote, CleanFont(value));
    }

    /// <summary>歌字字体族。</summary>
    public string FontUstLyric
    {
        get => _fontUstLyric;
        set => SetField(ref _fontUstLyric, CleanFont(value));
    }

    /// <summary>LRC 歌词字体族。</summary>
    public string FontLrc
    {
        get => _fontLrc;
        set => SetField(ref _fontLrc, CleanFont(value));
    }

    /// <summary>其他文字（BPM / 时间 / 标题 / 版权）字体族。</summary>
    public string FontOther
    {
        get => _fontOther;
        set => SetField(ref _fontOther, CleanFont(value));
    }

    /// <summary>自定义字体文件路径（随工程往返，播放时恢复注册）。</summary>
    public List<string> CustomFontPaths
    {
        get => _customFontPaths;
        set => SetField(ref _customFontPaths, value is null ? [] : [.. value]);
    }

    /// <inheritdoc />
    public override void ReadFrom(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Display);
        if (group is null)
        {
            return;
        }

        ShowBpm = ReadBool(group, "show_bpm", ShowBpm);
        ShowPlayTime = ReadBool(group, "show_play_time", ShowPlayTime);
        ShowSongName = ReadBool(group, "show_song_name", ShowSongName);
        ShowSongAuthor = ReadBool(group, "show_song_author", ShowSongAuthor);
        ShowUstAuthor = ReadBool(group, "show_ust_author", ShowUstAuthor);
        Fullscreen = ReadBool(group, "fullscreen", Fullscreen);
        ShowLyric = ReadBool(group, "show_lyric", ShowLyric);
        ShowNoteName = ReadBool(group, "show_note_name", ShowNoteName);
        ShowUstLyric = ReadBool(group, "show_ust_lyric", ShowUstLyric);
        ShowCopyright = ReadBool(group, "show_copyright", ShowCopyright);

        FontNote = ReadString(group, "font_note", FontNote);
        FontUstLyric = ReadString(group, "font_ust_lyric", FontUstLyric);
        FontLrc = ReadString(group, "font_lrc", FontLrc);
        FontOther = ReadString(group, "font_other", FontOther);

        // custom_font_paths 可能是 null / 非数组：非数组时保持原值（与 1.1.x 的
        // 「只接受 list，否则忽略」一致）
        if (group.TryGetStringList("custom_font_paths", out var paths))
        {
            CustomFontPaths = paths;
        }
    }

    /// <inheritdoc />
    public override void WriteTo(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Display, createIfMissing: true)!;

        group.SetBool("show_bpm", ShowBpm);
        group.SetBool("show_play_time", ShowPlayTime);
        group.SetBool("show_song_name", ShowSongName);
        group.SetBool("show_song_author", ShowSongAuthor);
        group.SetBool("show_ust_author", ShowUstAuthor);
        group.SetBool("fullscreen", Fullscreen);
        group.SetBool("show_lyric", ShowLyric);
        group.SetBool("show_note_name", ShowNoteName);
        group.SetBool("show_ust_lyric", ShowUstLyric);
        group.SetBool("show_copyright", ShowCopyright);

        group.SetString("font_note", FontNote);
        group.SetString("font_ust_lyric", FontUstLyric);
        group.SetString("font_lrc", FontLrc);
        group.SetString("font_other", FontOther);
        group.SetStringList("custom_font_paths", CustomFontPaths);
    }

    private static string CleanFont(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value;
}

/// <summary>
/// 颜色设置（<c>[ColorSettings]</c>）。
/// </summary>
/// <remarks>
/// 颜色在 setter 即校验：非法值当场回退默认，因此**不会**把非法颜色写进
/// <c>Settings.json</c> / <c>.uplr</c> / 渲染配置（对应 1.1.x 的 setter 内校验）。
/// </remarks>
internal sealed class ColorSettings : SettingsDomain<ColorSettings>
{
    /// <summary>背景色默认值。</summary>
    internal const string DefaultBackgroundColor = "#000000";

    /// <summary>音名色默认值。</summary>
    internal const string DefaultNoteColor = "#6c6c6c";

    /// <summary>其余颜色的默认值（白）。</summary>
    internal const string DefaultForegroundColor = "#FFFFFF";

    private string _backgroundColor = DefaultBackgroundColor;
    private string _noteColor = DefaultNoteColor;
    private string _lyricColor = DefaultForegroundColor;
    private string _lyricTextColor = DefaultForegroundColor;
    private string _otherTextColor = DefaultForegroundColor;
    private string _pitchCurveColor = DefaultForegroundColor;

    /// <summary>背景色。</summary>
    public string BackgroundColor
    {
        get => _backgroundColor;
        set => SetField(ref _backgroundColor, SettingsValueConverter.ValidateHexColor(value, DefaultBackgroundColor));
    }

    /// <summary>音名颜色。</summary>
    public string NoteColor
    {
        get => _noteColor;
        set => SetField(ref _noteColor, SettingsValueConverter.ValidateHexColor(value, DefaultNoteColor));
    }

    /// <summary>歌字颜色。</summary>
    public string LyricColor
    {
        get => _lyricColor;
        set => SetField(ref _lyricColor, SettingsValueConverter.ValidateHexColor(value, DefaultForegroundColor));
    }

    /// <summary>LRC 歌词颜色。</summary>
    public string LyricTextColor
    {
        get => _lyricTextColor;
        set => SetField(ref _lyricTextColor, SettingsValueConverter.ValidateHexColor(value, DefaultForegroundColor));
    }

    /// <summary>其他文字颜色。</summary>
    public string OtherTextColor
    {
        get => _otherTextColor;
        set => SetField(ref _otherTextColor, SettingsValueConverter.ValidateHexColor(value, DefaultForegroundColor));
    }

    /// <summary>音高曲线颜色。</summary>
    public string PitchCurveColor
    {
        get => _pitchCurveColor;
        set => SetField(ref _pitchCurveColor, SettingsValueConverter.ValidateHexColor(value, DefaultForegroundColor));
    }

    /// <inheritdoc />
    public override void ReadFrom(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Color);
        if (group is null)
        {
            return;
        }

        BackgroundColor = ReadString(group, "bg_color", BackgroundColor);
        NoteColor = ReadString(group, "note_color", NoteColor);
        LyricColor = ReadString(group, "lyric_color", LyricColor);
        LyricTextColor = ReadString(group, "lyric_text_color", LyricTextColor);
        OtherTextColor = ReadString(group, "other_text_color", OtherTextColor);
        PitchCurveColor = ReadString(group, "pitch_curve_color", PitchCurveColor);
    }

    /// <inheritdoc />
    public override void WriteTo(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Color, createIfMissing: true)!;

        group.SetString("bg_color", BackgroundColor);
        group.SetString("note_color", NoteColor);
        group.SetString("lyric_color", LyricColor);
        group.SetString("lyric_text_color", LyricTextColor);
        group.SetString("other_text_color", OtherTextColor);
        group.SetString("pitch_curve_color", PitchCurveColor);
    }
}

/// <summary>
/// 播放器样式设置（<c>[PlayerSettings]</c> 与 <c>[LyricSettings]</c>）。
/// </summary>
/// <remarks>
/// 枚举属性在 setter 里做「合法值优先、其次旧中文映射、最后默认值」的迁移
/// （对应 1.1.x 的 <c>migrate_value</c>），因此旧配置文件里的中文枚举值读进来即被规范化。
/// LRC 路径与播放器设置共用本类，但写回时落到独立的 <c>[LyricSettings]</c> 段——
/// 沿用 1.1.x 的分段方式，不要合并。
/// </remarks>
internal sealed class PlayerSettings : SettingsDomain<PlayerSettings>
{
    private string _lyricPosition = SettingsEnums.DefaultLyricPosition;
    private string _silentDisplay = SettingsEnums.DefaultSilentDisplay;
    private string _silentCustomText = string.Empty;
    private string _endDisplay = SettingsEnums.DefaultEndDisplay;
    private string _endCustomText = string.Empty;
    private string _pitchPlaceholder = SettingsEnums.DefaultPitchPlaceholder;
    private string _pitchCustomText = string.Empty;
    private string _lrcPath = string.Empty;

    /// <summary>歌词位置（<c>top</c> / <c>bottom</c>）。</summary>
    public string LyricPosition
    {
        get => _lyricPosition;
        set => SetField(
            ref _lyricPosition,
            SettingsValueConverter.MigrateEnumValue(
                value, SettingsEnums.LyricPositions, SettingsEnums.LegacyLyricPositions,
                SettingsEnums.DefaultLyricPosition));
    }

    /// <summary>空拍显示方式。</summary>
    public string SilentDisplay
    {
        get => _silentDisplay;
        set => SetField(
            ref _silentDisplay,
            SettingsValueConverter.MigrateEnumValue(
                value, SettingsEnums.SilentDisplays, SettingsEnums.LegacySilentDisplays,
                SettingsEnums.DefaultSilentDisplay));
    }

    /// <summary>空拍自定义文字。</summary>
    public string SilentCustomText
    {
        get => _silentCustomText;
        set => SetField(ref _silentCustomText, value);
    }

    /// <summary>结束显示方式。</summary>
    public string EndDisplay
    {
        get => _endDisplay;
        set => SetField(
            ref _endDisplay,
            SettingsValueConverter.MigrateEnumValue(
                value, SettingsEnums.EndDisplays, SettingsEnums.LegacyEndDisplays,
                SettingsEnums.DefaultEndDisplay));
    }

    /// <summary>结束自定义文字。</summary>
    public string EndCustomText
    {
        get => _endCustomText;
        set => SetField(ref _endCustomText, value);
    }

    /// <summary>音名占位符规则。</summary>
    public string PitchPlaceholder
    {
        get => _pitchPlaceholder;
        set => SetField(
            ref _pitchPlaceholder,
            SettingsValueConverter.MigrateEnumValue(
                value, SettingsEnums.PitchPlaceholders, SettingsEnums.LegacyPitchPlaceholders,
                SettingsEnums.DefaultPitchPlaceholder));
    }

    /// <summary>音名自定义占位文字。</summary>
    public string PitchCustomText
    {
        get => _pitchCustomText;
        set => SetField(ref _pitchCustomText, value);
    }

    /// <summary>LRC 文件路径（写入 <c>[LyricSettings]</c>）。</summary>
    public string LrcPath
    {
        get => _lrcPath;
        set => SetField(ref _lrcPath, value);
    }

    /// <inheritdoc />
    public override void ReadFrom(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Player);
        if (group is not null)
        {
            LyricPosition = ReadString(group, "lyric_pos", LyricPosition);
            SilentDisplay = ReadString(group, "silent_display", SilentDisplay);
            SilentCustomText = ReadString(group, "silent_custom_text", SilentCustomText);
            EndDisplay = ReadString(group, "end_display", EndDisplay);
            EndCustomText = ReadString(group, "end_custom_text", EndCustomText);
            PitchPlaceholder = ReadString(group, "pitch_placeholder", PitchPlaceholder);
            PitchCustomText = ReadString(group, "pitch_custom_text", PitchCustomText);
        }

        var lyricGroup = config.GetSection(SettingsSections.Lyric);
        if (lyricGroup is not null)
        {
            LrcPath = ReadString(lyricGroup, "lrc_path", LrcPath);
        }
    }

    /// <inheritdoc />
    public override void WriteTo(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Player, createIfMissing: true)!;

        group.SetString("lyric_pos", LyricPosition);
        group.SetString("silent_display", SilentDisplay);
        group.SetString("silent_custom_text", SilentCustomText);
        group.SetString("end_display", EndDisplay);
        group.SetString("end_custom_text", EndCustomText);
        group.SetString("pitch_placeholder", PitchPlaceholder);
        group.SetString("pitch_custom_text", PitchCustomText);

        var lyricGroup = config.GetSection(SettingsSections.Lyric, createIfMissing: true)!;
        lyricGroup.SetString("lrc_path", LrcPath);
    }
}

/// <summary>
/// 语言设置（<c>[LanguageSettings]</c>）。
/// </summary>
/// <remarks>
/// <b>不写入 <c>.uplr</c></b>：界面语言是应用级偏好，不属于工程内容
/// （与 1.1.x 的导出逻辑一致）。
/// </remarks>
internal sealed class LanguageSettings : SettingsDomain<LanguageSettings>
{
    /// <summary>「跟随系统」的存储值。</summary>
    internal const string ModeSystem = "system";

    private string _language = ModeSystem;

    /// <summary>语言代码或 <see cref="ModeSystem"/>。</summary>
    public string Language
    {
        get => _language;
        set => SetField(
            ref _language,
            string.IsNullOrEmpty(value) || (!Translator.IsSupportedLanguage(value) && value != ModeSystem)
                ? ModeSystem
                : value);
    }

    /// <summary>实际生效的语言代码（跟随系统时解析出具体语言）。</summary>
    public string EffectiveLanguage => _language == ModeSystem ? Translator.SystemLocale() : _language;

    /// <inheritdoc />
    public override void ReadFrom(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Language);
        if (group is null)
        {
            return;
        }

        Language = ReadString(group, "language", Language);
    }

    /// <inheritdoc />
    public override void WriteTo(SettingsConfig config)
    {
        config.GetSection(SettingsSections.Language, createIfMissing: true)!
            .SetString("language", Language);
    }
}

/// <summary>
/// 主题设置（<c>[ThemeSettings]</c>）。
/// </summary>
/// <remarks><b>不写入 <c>.uplr</c></b>：主题是应用级偏好，不属于工程内容。</remarks>
internal sealed class ThemeSettings : SettingsDomain<ThemeSettings>
{
    private string _themeMode = SettingsEnums.DefaultThemeMode;
    private string _accentColorMode = SettingsEnums.DefaultAccentColorMode;
    private string _customAccentColor = SettingsEnums.DefaultAccentColor;
    private string _windowEffect = SettingsEnums.DefaultWindowEffect;

    /// <summary>主题模式（<c>auto</c> / <c>light</c> / <c>dark</c>）。</summary>
    public string ThemeMode
    {
        get => _themeMode;
        set => SetField(
            ref _themeMode,
            SettingsValueConverter.MigrateEnumValue(
                value, SettingsEnums.ThemeModes, NoLegacy, SettingsEnums.DefaultThemeMode));
    }

    /// <summary>强调色模式（<c>auto</c> / <c>custom</c>）。</summary>
    public string AccentColorMode
    {
        get => _accentColorMode;
        set => SetField(
            ref _accentColorMode,
            SettingsValueConverter.MigrateEnumValue(
                value, SettingsEnums.AccentColorModes, NoLegacy, SettingsEnums.DefaultAccentColorMode));
    }

    /// <summary>自定义强调色。</summary>
    public string CustomAccentColor
    {
        get => _customAccentColor;
        set => SetField(
            ref _customAccentColor,
            SettingsValueConverter.ValidateHexColor(value, SettingsEnums.DefaultAccentColor));
    }

    /// <summary>窗口背景效果（<c>none</c> / <c>mica</c> / <c>acrylic</c>）。</summary>
    public string WindowEffect
    {
        get => _windowEffect;
        set => SetField(
            ref _windowEffect,
            SettingsValueConverter.MigrateEnumValue(
                value, SettingsEnums.WindowEffects, NoLegacy, SettingsEnums.DefaultWindowEffect));
    }

    /// <summary>这些字段没有历史中文值（1.1.x 起就存英文 key），用空表即可。</summary>
    private static readonly Dictionary<string, string> NoLegacy = [];

    /// <inheritdoc />
    public override void ReadFrom(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Theme);
        if (group is null)
        {
            return;
        }

        ThemeMode = ReadString(group, "theme_mode", ThemeMode);
        AccentColorMode = ReadString(group, "accent_color_mode", AccentColorMode);
        CustomAccentColor = ReadString(group, "custom_accent_color", CustomAccentColor);
        WindowEffect = ReadString(group, "window_effect", WindowEffect);
    }

    /// <inheritdoc />
    public override void WriteTo(SettingsConfig config)
    {
        var group = config.GetSection(SettingsSections.Theme, createIfMissing: true)!;

        group.SetString("theme_mode", ThemeMode);
        group.SetString("accent_color_mode", AccentColorMode);
        group.SetString("custom_accent_color", CustomAccentColor);
        group.SetString("window_effect", WindowEffect);
    }
}
