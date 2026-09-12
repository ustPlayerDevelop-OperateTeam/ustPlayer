using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

using UstPlayer.Settings;
using UstPlayer.Settings.Domains;

namespace UstPlayer.Projects;

/// <summary>
/// <c>.uplr</c> 的 <c>Info.json</c> 与设置子域之间的映射 — 从 1.1.x <c>core/uplr_io.py</c> 的
/// <c>_settings_to_info_json</c> / <c>_apply_info_json</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是「格式双向兼容」硬承诺的核心</b>。四类易错点必须逐字复刻：
/// </para>
/// <list type="number">
///   <item><b>布尔是整数</b> <c>0/1</c>（<c>int(x)</c>），不是 <c>true/false</c>，
///   也不是设置文件里的字符串 <c>"1"/"0"</c>——同一个概念三处写法不同。</item>
///   <item><b>空字符串转 <c>null</c></b>（Python 的 <c>or None</c> 语义）；
///   <c>custom_font_paths</c> 空列表也转 <c>null</c>。</item>
///   <item><b>段结构在两个格式里不同</b>：<c>.uplr</c> 的 <c>display</c> 含 <c>curve_show</c>；
///   <c>.uprd</c> 的 <c>display</c> 不含它（放在 <c>else</c>），且另含渲染器不消费的
///   <c>show_phoneme</c> / <c>show_midinote</c> / <c>show_waveform</c>（恒 <c>0</c>）。</item>
///   <item><b>读取端一切宽松</b>：字段缺失、类型不对、枚举越界都回退默认，绝不抛异常——
///   判空与类型判断留给这一层，而不是让上层到处 try/catch。</item>
/// </list>
/// </remarks>
internal static class UplrInfoJson
{
    /// <summary>Info.json 的固定成员名。</summary>
    internal const string InfoFileName = "Info.json";

    // ===================== 写：设置 → Info.json =====================

    /// <summary>
    /// 把设置组装为 <c>.uplr</c> 的 <c>Info.json</c>。
    /// </summary>
    /// <param name="settings">设置管理器。</param>
    /// <param name="members">已收集的资源：设置属性名 → 包内文件名。</param>
    /// <returns>Info.json 根对象。</returns>
    internal static JsonObject BuildUplrInfo(SettingsManager settings, ResourceMembers members)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(members);

        var display = settings.Display;
        var file = settings.File;
        var color = settings.Color;
        var player = settings.Player;
        var project = settings.Project;

        return new JsonObject
        {
            ["encoding"] = file.Encoding,
            ["basic"] = new JsonObject
            {
                ["project_name"] = OrNull(project.ProjectName),
                ["ust_path"] = members.NameOrNull("ust_path"),
                ["music_path"] = members.NameOrNull("music_path"),
                ["song_name"] = OrNull(project.SongName),
                ["song_author"] = OrNull(project.SongAuthor),
                ["ust_author"] = OrNull(project.UstAuthor),
            },
            ["display"] = new JsonObject
            {
                ["show_bpm"] = ToFlag(display.ShowBpm),
                ["show_play_time"] = ToFlag(display.ShowPlayTime),
                ["show_song_name"] = ToFlag(display.ShowSongName),
                ["show_song_author"] = ToFlag(display.ShowSongAuthor),
                ["show_ust_author"] = ToFlag(display.ShowUstAuthor),
                ["fullscreen"] = ToFlag(display.Fullscreen),
                ["show_lyric"] = ToFlag(display.ShowLyric),
                // .uplr 的 curve_show 在 display 段
                ["curve_show"] = ToFlag(file.CurveShow),
                ["show_note_name"] = ToFlag(display.ShowNoteName),
                ["show_ust_lyric"] = ToFlag(display.ShowUstLyric),
                ["show_copyright"] = ToFlag(display.ShowCopyright),
                ["font_note"] = OrNull(display.FontNote),
                ["font_ust_lyric"] = OrNull(display.FontUstLyric),
                ["font_lrc"] = OrNull(display.FontLrc),
                ["font_other"] = OrNull(display.FontOther),
                ["custom_font_paths"] = ListOrNull(display.CustomFontPaths),
            },
            ["color"] = new JsonObject
            {
                ["bg_color"] = color.BackgroundColor,
                ["note_color"] = color.NoteColor,
                ["lyric_color"] = color.LyricColor,
                ["lyric_text_color"] = color.LyricTextColor,
                ["other_text_color"] = color.OtherTextColor,
                ["pitch_curve_color"] = color.PitchCurveColor,
            },
            ["else"] = new JsonObject
            {
                ["lyric_pos"] = player.LyricPosition,
                ["lrc_path"] = members.NameOrNull("lrc_path"),
                ["silent_display"] = player.SilentDisplay,
                ["silent_custom_text"] = OrNull(player.SilentCustomText),
                ["end_display"] = player.EndDisplay,
                ["end_custom_text"] = OrNull(player.EndCustomText),
                ["pitch_placeholder"] = player.PitchPlaceholder,
                ["pitch_custom_text"] = OrNull(player.PitchCustomText),
            },
        };
    }

    /// <summary>
    /// 把设置组装为 <c>.uprd</c> 的 <c>Info.json</c>（在 <c>.uplr</c> 结构上叠加视频段）。
    /// </summary>
    /// <param name="settings">设置管理器。</param>
    /// <param name="members">已收集的资源。</param>
    /// <param name="video">视频段（宽 / 高 / 帧率）。</param>
    /// <returns>Info.json 根对象。</returns>
    /// <remarks>
    /// 与 <c>.uplr</c> 的差异（必须保持，否则渲染器取不到 <c>width/height/fps</c>）：
    /// <c>display</c> 不含 <c>curve_show</c>（改放 <c>else</c>），
    /// 额外含恒为 <c>0</c> 的 <c>show_phoneme</c> / <c>show_midinote</c> / <c>show_waveform</c>，
    /// 以及新增的 <c>video</c> 段。
    /// </remarks>
    internal static JsonObject BuildUprdInfo(
        SettingsManager settings,
        ResourceMembers members,
        (int Width, int Height, int Fps) video)
    {
        var info = BuildUplrInfo(settings, members);

        var display = (JsonObject)info["display"]!;
        display.Remove("curve_show");
        display["show_phoneme"] = 0;
        display["show_midinote"] = 0;
        display["show_waveform"] = 0;

        var elseSection = (JsonObject)info["else"]!;
        elseSection["curve_show"] = ToFlag(settings.File.CurveShow);

        info["video"] = new JsonObject
        {
            ["width"] = video.Width,
            ["height"] = video.Height,
            ["fps"] = video.Fps,
        };

        return info;
    }

    /// <summary>
    /// 把 <c>.uprd</c> 的 <c>Info.json</c> 归一化为 <c>.uplr</c> 兼容结构。
    /// </summary>
    /// <param name="info">源信息对象。</param>
    /// <returns>归一化后的信息对象。</returns>
    /// <remarks>
    /// <c>.uprd</c> 与 <c>.uplr</c> 的出入（对应 1.1.x <c>normalize_uprd_info</c>）：
    /// 移除渲染器无对应字段的波形 / 音名 / 音素开关；<c>curve_show</c> 从 <c>else</c> 回迁到
    /// <c>display</c>；补默认 <c>pitch_curve_color</c>；枚举旧中文值迁移为英文 key。
    /// </remarks>
    internal static JsonObject NormalizeUprdInfo(JsonObject info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var display = AsObject(info["display"]);
        var elseSection = AsObject(info["else"]);

        foreach (var key in (string[])["show_phoneme", "show_midinote", "show_waveform"])
        {
            display.Remove(key);
        }

        // .uprd 把 curve_show 放在 else；归一化时回迁到 display（.uplr 的位置）
        if (elseSection.TryGetPropertyValue("curve_show", out var curveShow))
        {
            // 先取出再移除：JsonNode 一个实例只能有一个父节点，
            // 直接从 else 里读出来的节点仍挂在 else 上，赋给 display 会抛
            // 「The node already has a parent」。深拷贝后再移除，语义最清楚。
            display["curve_show"] = curveShow?.DeepClone();
            elseSection.Remove("curve_show");
        }

        MigrateLegacyEnum(elseSection, "lyric_pos");
        MigrateLegacyEnum(elseSection, "silent_display");
        MigrateLegacyEnum(elseSection, "end_display");
        MigrateLegacyEnum(elseSection, "pitch_placeholder");

        var color = AsObject(info["color"]);
        if (!color.ContainsKey("pitch_curve_color"))
        {
            color["pitch_curve_color"] = "#FFFFFF";
        }

        var result = new JsonObject
        {
            ["encoding"] = ReadString(info, "encoding") ?? "Shift-JIS",
            ["basic"] = AsObject(info["basic"]),
            ["display"] = display,
            ["color"] = color,
            ["else"] = elseSection,
            ["video"] = AsObject(info["video"]),
        };

        return result;
    }

    // ===================== 读：Info.json → 设置 =====================

    /// <summary>
    /// 把 <c>Info.json</c> 应用到设置。
    /// </summary>
    /// <param name="settings">设置管理器。</param>
    /// <param name="info">Info.json 根对象（可以是空对象，用于把全部字段重置为默认值）。</param>
    /// <param name="baseDirectory">资源所在目录（用于解析相对路径）；传空串表示不解析资源。</param>
    /// <exception cref="ProjectFormatException">
    /// <paramref name="baseDirectory"/> 非空且引用的资源不存在或路径不安全。
    /// </exception>
    /// <remarks>
    /// 传入空对象是**刻意的用法**：旧文本格式导入前需要把「会被导入触碰的全部字段」
    /// 重置为默认值，避免上一个工程的状态残留（对应 1.1.x <c>_apply_info_json({}, "")</c>）。
    /// </remarks>
    internal static void ApplyInfoJson(SettingsManager settings, JsonObject info, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(info);

        var basic = AsObject(info["basic"]);
        var display = AsObject(info["display"]);
        var color = AsObject(info["color"]);
        var elseSection = AsObject(info["else"]);

        settings.File.Encoding = ReadString(info, "encoding") ?? "Shift-JIS";

        settings.Project.ProjectName = ReadString(basic, "project_name") ?? string.Empty;
        settings.File.UstPath = ResolveResource(basic, "ust_path", baseDirectory);
        settings.Project.MusicPath = ResolveResource(basic, "music_path", baseDirectory);
        settings.Project.SongName = ReadString(basic, "song_name") ?? string.Empty;
        settings.Project.SongAuthor = ReadString(basic, "song_author") ?? string.Empty;
        settings.Project.UstAuthor = ReadString(basic, "ust_author") ?? string.Empty;

        // 默认值与 1.1.x 一致：show_lyric 默认 false，其余显示项默认 true
        settings.Display.ShowBpm = ReadFlag(display, "show_bpm", true);
        settings.Display.ShowPlayTime = ReadFlag(display, "show_play_time", true);
        settings.Display.ShowSongName = ReadFlag(display, "show_song_name", true);
        settings.Display.ShowSongAuthor = ReadFlag(display, "show_song_author", true);
        settings.Display.ShowUstAuthor = ReadFlag(display, "show_ust_author", true);
        settings.Display.Fullscreen = ReadFlag(display, "fullscreen", true);
        settings.Display.ShowLyric = ReadFlag(display, "show_lyric", false);
        settings.Display.ShowNoteName = ReadFlag(display, "show_note_name", true);
        settings.Display.ShowUstLyric = ReadFlag(display, "show_ust_lyric", true);
        settings.Display.ShowCopyright = ReadFlag(display, "show_copyright", true);

        settings.Display.FontNote = ReadString(display, "font_note") ?? string.Empty;
        settings.Display.FontUstLyric = ReadString(display, "font_ust_lyric") ?? string.Empty;
        settings.Display.FontLrc = ReadString(display, "font_lrc") ?? string.Empty;
        settings.Display.FontOther = ReadString(display, "font_other") ?? string.Empty;
        settings.Display.CustomFontPaths = ReadStringList(display, "custom_font_paths");

        // curve_show：.uplr 在 display，.uprd 在 else，两处都读
        settings.File.CurveShow =
            ReadFlag(display, "curve_show", false) || ReadFlag(elseSection, "curve_show", false);

        settings.Color.BackgroundColor = ReadString(color, "bg_color") ?? ColorSettings.DefaultBackgroundColor;
        settings.Color.NoteColor = ReadString(color, "note_color") ?? ColorSettings.DefaultNoteColor;
        settings.Color.LyricColor = ReadString(color, "lyric_color") ?? ColorSettings.DefaultForegroundColor;
        settings.Color.LyricTextColor =
            ReadString(color, "lyric_text_color") ?? ColorSettings.DefaultForegroundColor;
        settings.Color.OtherTextColor =
            ReadString(color, "other_text_color") ?? ColorSettings.DefaultForegroundColor;
        settings.Color.PitchCurveColor =
            ReadString(color, "pitch_curve_color") ?? ColorSettings.DefaultForegroundColor;

        // 枚举经 setter 完成「合法值优先 → 旧中文映射 → 默认」
        settings.Player.LyricPosition = ReadString(elseSection, "lyric_pos") ?? SettingsEnums.DefaultLyricPosition;
        settings.Player.LrcPath = ResolveResource(elseSection, "lrc_path", baseDirectory);
        settings.Player.SilentDisplay = ReadString(elseSection, "silent_display") ?? SettingsEnums.DefaultSilentDisplay;
        settings.Player.SilentCustomText = ReadString(elseSection, "silent_custom_text") ?? string.Empty;
        settings.Player.EndDisplay = ReadString(elseSection, "end_display") ?? SettingsEnums.DefaultEndDisplay;
        settings.Player.EndCustomText = ReadString(elseSection, "end_custom_text") ?? string.Empty;
        settings.Player.PitchPlaceholder =
            ReadString(elseSection, "pitch_placeholder") ?? SettingsEnums.DefaultPitchPlaceholder;
        settings.Player.PitchCustomText = ReadString(elseSection, "pitch_custom_text") ?? string.Empty;
    }

    // ===================== 转换辅助 =====================

    /// <summary>布尔 → <c>0</c>/<c>1</c> 整数（不是 <c>true/false</c>，也不是 <c>"1"</c>）。</summary>
    /// <param name="value">布尔值。</param>
    /// <returns>整数标志。</returns>
    internal static int ToFlag(bool value) => value ? 1 : 0;

    /// <summary>空字符串 → <c>null</c>（对应 Python 的 <c>or None</c>）。</summary>
    /// <param name="value">字符串。</param>
    /// <returns>非空串或 <see langword="null"/>。</returns>
    internal static string? OrNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>空列表 → <c>null</c>。</summary>
    /// <param name="values">字符串列表。</param>
    /// <returns>JSON 数组或 <see langword="null"/>。</returns>
    internal static JsonNode? ListOrNull(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    /// <summary>
    /// 把 JSON 值当对象取；非对象（含 <c>null</c>）返回空对象。
    /// </summary>
    /// <param name="node">JSON 节点。</param>
    /// <returns>对象。</returns>
    /// <remarks>
    /// <b>返回的是深拷贝</b>：<see cref="JsonNode"/> 一个实例只能有一个父节点，
    /// 而本类会把取出的段重新组装进新对象（<see cref="NormalizeUprdInfo"/>）。
    /// 若直接返回原节点，赋给新父节点时会抛「The node already has a parent」。
    /// 这也与 Python 侧 <c>dict(info.get(k) or {})</c> 的「得到独立副本」意图一致。
    /// </remarks>
    internal static JsonObject AsObject(JsonNode? node) =>
        node is JsonObject source ? (JsonObject)source.DeepClone() : new JsonObject();

    /// <summary>
    /// 宽松读字符串：非字符串（含缺失、<c>null</c>、数字、对象）返回 <see langword="null"/>。
    /// </summary>
    /// <param name="group">对象。</param>
    /// <param name="key">键。</param>
    /// <returns>字符串或 <see langword="null"/>。</returns>
    internal static string? ReadString(JsonObject group, string key) =>
        group.TryGetPropertyValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text)
            ? text
            : null;

    /// <summary>
    /// 宽松读布尔标志。
    /// </summary>
    /// <param name="group">对象。</param>
    /// <param name="key">键。</param>
    /// <param name="fallback">缺失或为 <c>null</c> 时的默认值。</param>
    /// <returns>布尔值。</returns>
    /// <remarks>
    /// <b>值为 <c>null</c> 视同缺失</b>，与 1.1.x 的 <c>dict.get(key, default)</c> 语义一致：
    /// 那边整块读的是 Python dict，<c>None</c> 传给 <c>as_bool</c> 会走默认分支。
    /// 若把 <c>null</c> 当成有效值，<c>.uplr</c> 里的 <c>null</c> 字段会被解析为
    /// <c>false</c>（或反向），破坏往返保真。
    /// </remarks>
    internal static bool ReadFlag(JsonObject group, string key, bool fallback) =>
        group.TryGetPropertyValue(key, out var node) && node is not null
            ? SettingsValueConverter.ToBool(node, fallback)
            : fallback;

    /// <summary>宽松读字符串列表：非数组或元素非字符串时跳过。</summary>
    /// <param name="group">对象。</param>
    /// <param name="key">键。</param>
    /// <returns>字符串列表（永不为 <see langword="null"/>）。</returns>
    internal static List<string> ReadStringList(JsonObject group, string key)
    {
        var result = new List<string>();

        if (!group.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
        {
            return result;
        }

        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>
    /// 解析资源路径：非空引用必须真实存在，且路径不能越出基目录。
    /// </summary>
    /// <param name="group">对象。</param>
    /// <param name="key">键。</param>
    /// <param name="baseDirectory">基目录；为空表示不解析（返回空串）。</param>
    /// <returns>资源绝对路径；未引用时返回空串。</returns>
    /// <exception cref="ProjectFormatException">路径不安全或资源不存在。</exception>
    private static string ResolveResource(JsonObject group, string key, string baseDirectory)
    {
        var name = ReadString(group, key);
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(baseDirectory))
        {
            // 空对象重置阶段或旧文本格式：此时不做存在性校验
            return name;
        }

        // 与 ZIP 成员同一套防护：Info.json 登记的资源名也可能是恶意的
        var target = ProjectPathSafety.ResolveInside(baseDirectory, name);

        if (!File.Exists(target))
        {
            throw new ProjectFormatException($"工程文件引用的资源不存在：{name}");
        }

        return target;
    }

    /// <summary>把旧中文枚举值迁移为英文 key。</summary>
    /// <param name="group">对象。</param>
    /// <param name="field">字段名。</param>
    private static void MigrateLegacyEnum(JsonObject group, string field)
    {
        var (valid, legacy, _) = field switch
        {
            "lyric_pos" => (SettingsEnums.LyricPositions, SettingsEnums.LegacyLyricPositions,
                SettingsEnums.DefaultLyricPosition),
            "silent_display" => (SettingsEnums.SilentDisplays, SettingsEnums.LegacySilentDisplays,
                SettingsEnums.DefaultSilentDisplay),
            "end_display" => (SettingsEnums.EndDisplays, SettingsEnums.LegacyEndDisplays,
                SettingsEnums.DefaultEndDisplay),
            _ => (SettingsEnums.PitchPlaceholders, SettingsEnums.LegacyPitchPlaceholders,
                SettingsEnums.DefaultPitchPlaceholder),
        };

        var current = ReadString(group, field);
        if (current is null)
        {
            return;
        }

        group[field] = SettingsValueConverter.MigrateEnumValue(current, valid, legacy, valid[0]);
    }
}
