namespace UstPlayer.Settings;

/// <summary>
/// <c>Settings.json</c> 的分组名常量 — 与 1.1.x 的段名逐字一致。
/// </summary>
/// <remarks>
/// <para>
/// <b>这些字符串是存储契约</b>：改任何一个都会让 1.1.x 与 2.0 互相读不到对方的设置。
/// 因此集中在此处定义，并由 <c>SettingsStoreTests</c> 断言实际写出的分组名。
/// </para>
/// <para>
/// 命名说明：段名保留 1.1.x 的 <c>xxxSettings</c> 形式（它同时是旧版 <c>Settings.ini</c>
/// 的段名），键名则一律 snake_case。
/// </para>
/// </remarks>
internal static class SettingsSections
{
    /// <summary>项目信息。</summary>
    internal const string Project = "ProjectSettings";

    /// <summary>文件与编码。</summary>
    internal const string File = "FileSettings";

    /// <summary>显示开关与字体。</summary>
    internal const string Display = "DisplaySettings";

    /// <summary>颜色。</summary>
    internal const string Color = "ColorSettings";

    /// <summary>播放器样式枚举与自定义文字。</summary>
    internal const string Player = "PlayerSettings";

    /// <summary>LRC 歌词路径（与播放器设置分开，沿用 1.1.x）。</summary>
    internal const string Lyric = "LyricSettings";

    /// <summary>语言偏好（不写入 .uplr）。</summary>
    internal const string Language = "LanguageSettings";

    /// <summary>主题（不写入 .uplr）。</summary>
    internal const string Theme = "ThemeSettings";

    /// <summary>上次打开的目录等路径记忆。</summary>
    internal const string Path = "PathSettings";
}
