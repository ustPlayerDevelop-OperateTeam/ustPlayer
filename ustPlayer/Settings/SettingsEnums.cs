using System.Collections.Generic;

namespace UstPlayer.Settings;

/// <summary>
/// 设置层共用的枚举合法值与「旧中文值 → 英文 key」迁移表。
/// </summary>
/// <remarks>
/// <para>
/// <b>存储层只放稳定英文 key</b>：<c>Settings.json</c> 与 <c>.uplr</c> 中的枚举值一律是
/// <c>top</c> / <c>r</c> / <c>custom</c> / <c>none</c> 这类英文值，显示文案由 UI 层
/// 用 <c>Translator.Tr</c> 翻译。早期版本曾把中文显示文案直接写进存储层，
/// 下面的迁移表就是为兼容那些旧文件而存在的（对应 1.1.x <c>player.py</c> 的
/// <c>_LEGACY_*</c> 字典）。
/// </para>
/// <para>
/// 改动这些表会让旧配置文件的枚举值读不出来，因此它们是**契约的一部分**。
/// </para>
/// </remarks>
internal static class SettingsEnums
{
    /// <summary>歌词位置（对应 <c>PlayerStyle.lyric_pos</c>）。</summary>
    internal static readonly string[] LyricPositions = ["top", "bottom"];

    /// <summary>空拍显示方式（对应 <c>PlayerStyle.silent_display</c>）。</summary>
    internal static readonly string[] SilentDisplays = ["r", "dash", "custom", "none"];

    /// <summary>结束显示方式（对应 <c>PlayerStyle.end_display</c>）。</summary>
    internal static readonly string[] EndDisplays = ["end", "dash", "custom", "none"];

    /// <summary>音名占位符规则（对应 <c>PlayerStyle.pitch_placeholder</c>）。</summary>
    internal static readonly string[] PitchPlaceholders = ["none", "dash", "custom"];

    /// <summary>主题模式。</summary>
    internal static readonly string[] ThemeModes = ["auto", "light", "dark"];

    /// <summary>强调色模式。</summary>
    internal static readonly string[] AccentColorModes = ["auto", "custom"];

    /// <summary>窗口背景效果。</summary>
    internal static readonly string[] WindowEffects = ["none", "mica", "acrylic"];

    /// <summary>歌词位置的旧中文值。</summary>
    internal static readonly Dictionary<string, string> LegacyLyricPositions = new()
    {
        ["上"] = "top",
        ["下"] = "bottom",
    };

    /// <summary>空拍显示的旧中文值。</summary>
    internal static readonly Dictionary<string, string> LegacySilentDisplays = new()
    {
        ["R"] = "r",
        ["-"] = "dash",
        ["自定义文字"] = "custom",
        ["什么都不显示"] = "none",
    };

    /// <summary>结束显示的旧中文值。</summary>
    internal static readonly Dictionary<string, string> LegacyEndDisplays = new()
    {
        ["END"] = "end",
        ["-"] = "dash",
        ["自定义文字"] = "custom",
        ["什么都不显示"] = "none",
    };

    /// <summary>音名占位符的旧中文值。</summary>
    internal static readonly Dictionary<string, string> LegacyPitchPlaceholders = new()
    {
        ["无"] = "none",
        ["-"] = "dash",
        ["自定义文字"] = "custom",
    };

    /// <summary>歌词位置的默认值。</summary>
    internal const string DefaultLyricPosition = "top";

    /// <summary>空拍显示的默认值。</summary>
    internal const string DefaultSilentDisplay = "r";

    /// <summary>结束显示的默认值。</summary>
    internal const string DefaultEndDisplay = "end";

    /// <summary>音名占位符的默认值。</summary>
    internal const string DefaultPitchPlaceholder = "none";

    /// <summary>主题模式默认值（跟随系统）。</summary>
    internal const string DefaultThemeMode = "auto";

    /// <summary>强调色模式默认值（跟随系统）。</summary>
    internal const string DefaultAccentColorMode = "auto";

    /// <summary>默认自定义强调色。</summary>
    internal const string DefaultAccentColor = "#009faa";

    /// <summary>窗口效果默认值（Win11 Mica）。</summary>
    internal const string DefaultWindowEffect = "mica";
}
