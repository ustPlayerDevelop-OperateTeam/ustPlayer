using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace UstPlayer.Models;

/// <summary>
/// UST 音符数据 — 对应 1.1.x <c>core/contracts.py</c> 的 <c>NoteInfo</c>。
/// </summary>
/// <remarks>
/// <para>
/// 属性可写：解析器逐行填充字段，且 OpenUtau 风格音高曲线需要在校验后再合成
/// <see cref="PitchBend"/>（对应 Python 版逐行赋值 + <c>_finalize_pitch_bend</c> 的行为）。
/// </para>
/// <para>
/// <b>字段名是硬契约</b>：本类型直接序列化为渲染器 <c>RenderConfig.ust.notes[]</c>
/// （见根 <c>API_Docs.md</c>）。字段名与 1.1.x 不一致时不会报错——渲染器的 serde
/// 会**静默忽略**未知字段，表现为画面缺内容。因此每个属性都显式标注
/// <see cref="JsonPropertyNameAttribute"/>，并由 <c>JsonContractTests</c> 逐字段钉死。
/// </para>
/// </remarks>
internal sealed class NoteInfo
{
    /// <summary>段编号，如 <c>"0000"</c>。</summary>
    [JsonPropertyName("index")]
    public string Index { get; set; } = string.Empty;

    /// <summary>长度（tick）。</summary>
    [JsonPropertyName("length")]
    public int Length { get; set; }

    /// <summary>歌词。</summary>
    [JsonPropertyName("lyric")]
    public string Lyric { get; set; } = string.Empty;

    /// <summary>MIDI 音号。</summary>
    [JsonPropertyName("note_num")]
    public int NoteNumber { get; set; }

    /// <summary>音素，可为空。</summary>
    [JsonPropertyName("phoneme")]
    public string Phoneme { get; set; } = string.Empty;

    /// <summary>音高曲线点序列（音分）。</summary>
    [JsonPropertyName("pitch_bend")]
    public List<int> PitchBend { get; set; } = [];
}

/// <summary>
/// UST 解析结果 — 对应 1.1.x <c>UstInfo</c> 与渲染器 <c>RenderConfig.ust</c>。
/// </summary>
internal sealed class UstInfo
{
    /// <summary>UST 版本号（如 <c>UST Version1.2</c>）。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>速度（BPM），默认 120。</summary>
    [JsonPropertyName("tempo")]
    public double Tempo { get; set; } = 120.0;

    /// <summary>轨道数。</summary>
    [JsonPropertyName("tracks")]
    public int Tracks { get; set; } = 1;

    /// <summary>音符序列。</summary>
    [JsonPropertyName("notes")]
    public List<NoteInfo> Notes { get; set; } = [];
}

/// <summary>
/// 项目信息 — 对应 1.1.x <c>ProjectInfo</c> 与渲染器 <c>RenderConfig.project</c>。
/// </summary>
internal sealed class ProjectInfo
{
    /// <summary>工程名。</summary>
    [JsonPropertyName("project_name")]
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>曲名。</summary>
    [JsonPropertyName("song_name")]
    public string SongName { get; set; } = string.Empty;

    /// <summary>曲作者。</summary>
    [JsonPropertyName("song_author")]
    public string SongAuthor { get; set; } = string.Empty;

    /// <summary>调音师。</summary>
    [JsonPropertyName("ust_author")]
    public string UstAuthor { get; set; } = string.Empty;
}

/// <summary>
/// 播放器显示开关 — 对应 1.1.x <c>ShowConfig</c> 与渲染器 <c>RenderConfig.show</c>。
/// </summary>
internal sealed class ShowConfig
{
    /// <summary>显示 BPM。</summary>
    [JsonPropertyName("bpm")]
    public bool Bpm { get; set; } = true;

    /// <summary>显示播放时间。</summary>
    [JsonPropertyName("play_time")]
    public bool PlayTime { get; set; } = true;

    /// <summary>显示曲名。</summary>
    [JsonPropertyName("song_name")]
    public bool SongName { get; set; } = true;

    /// <summary>显示曲作者。</summary>
    [JsonPropertyName("song_author")]
    public bool SongAuthor { get; set; } = true;

    /// <summary>显示调音师。</summary>
    [JsonPropertyName("ust_author")]
    public bool UstAuthor { get; set; } = true;

    /// <summary>显示 LRC 歌词。</summary>
    [JsonPropertyName("lyric")]
    public bool Lyric { get; set; } = true;

    /// <summary>显示音高曲线。</summary>
    [JsonPropertyName("curve_show")]
    public bool CurveShow { get; set; }

    /// <summary>显示音名。</summary>
    [JsonPropertyName("note_name")]
    public bool NoteName { get; set; } = true;

    /// <summary>显示歌字（UST 歌词）。</summary>
    [JsonPropertyName("ust_lyric")]
    public bool UstLyric { get; set; } = true;

    /// <summary>显示底部版权信息。</summary>
    [JsonPropertyName("copyright")]
    public bool Copyright { get; set; } = true;

    /// <summary>音名字体族（空 = 语言默认）。</summary>
    [JsonPropertyName("font_note")]
    public string FontNote { get; set; } = string.Empty;

    /// <summary>歌字字体族。</summary>
    [JsonPropertyName("font_ust_lyric")]
    public string FontUstLyric { get; set; } = string.Empty;

    /// <summary>LRC 歌词字体族。</summary>
    [JsonPropertyName("font_lrc")]
    public string FontLrc { get; set; } = string.Empty;

    /// <summary>其他文字（BPM / 时间 / 标题 / 版权）字体族。</summary>
    [JsonPropertyName("font_other")]
    public string FontOther { get; set; } = string.Empty;

    /// <summary>自定义字体文件路径（恢复注册用）。</summary>
    [JsonPropertyName("custom_font_paths")]
    public List<string> CustomFontPaths { get; set; } = [];
}

/// <summary>
/// 播放器样式 — 对应 1.1.x <c>PlayerStyle</c> 与渲染器 <c>RenderConfig.style</c>。
/// </summary>
/// <remarks>
/// 颜色均为 <c>#RRGGBB</c> 文本；枚举为存储层稳定英文 key
/// （<c>top</c>/<c>r</c>/<c>custom</c>/<c>none</c> 等），详见 1.1.x 的约定。
/// </remarks>
internal sealed class PlayerStyle
{
    /// <summary>背景色。</summary>
    [JsonPropertyName("bg_color")]
    public string BackgroundColor { get; set; } = "#000000";

    /// <summary>音名颜色。</summary>
    [JsonPropertyName("note_color")]
    public string NoteColor { get; set; } = "#6c6c6c";

    /// <summary>歌字颜色。</summary>
    [JsonPropertyName("lyric_color")]
    public string LyricColor { get; set; } = "#FFFFFF";

    /// <summary>LRC 歌词颜色。</summary>
    [JsonPropertyName("lyric_text_color")]
    public string LyricTextColor { get; set; } = "#FFFFFF";

    /// <summary>其他文字颜色。</summary>
    [JsonPropertyName("other_text_color")]
    public string OtherTextColor { get; set; } = "#FFFFFF";

    /// <summary>歌词位置（<c>top</c> / <c>bottom</c>）。</summary>
    [JsonPropertyName("lyric_pos")]
    public string LyricPosition { get; set; } = "top";

    /// <summary>是否全屏。</summary>
    [JsonPropertyName("fullscreen")]
    public bool Fullscreen { get; set; } = true;

    /// <summary>LRC 文件路径。</summary>
    [JsonPropertyName("lrc_path")]
    public string LrcPath { get; set; } = string.Empty;

    /// <summary>伴奏文件路径。</summary>
    [JsonPropertyName("music_path")]
    public string MusicPath { get; set; } = string.Empty;

    /// <summary>空拍显示方式（<c>r</c> / <c>dash</c> / <c>custom</c> / <c>none</c>）。</summary>
    [JsonPropertyName("silent_display")]
    public string SilentDisplay { get; set; } = "r";

    /// <summary>空拍自定义文字。</summary>
    [JsonPropertyName("silent_custom_text")]
    public string SilentCustomText { get; set; } = string.Empty;

    /// <summary>结束显示方式（<c>end</c> / <c>dash</c> / <c>custom</c> / <c>none</c>）。</summary>
    [JsonPropertyName("end_display")]
    public string EndDisplay { get; set; } = "end";

    /// <summary>结束自定义文字。</summary>
    [JsonPropertyName("end_custom_text")]
    public string EndCustomText { get; set; } = string.Empty;

    /// <summary>音名占位符规则（<c>none</c> / <c>dash</c> / <c>custom</c>）。</summary>
    [JsonPropertyName("pitch_placeholder")]
    public string PitchPlaceholder { get; set; } = "none";

    /// <summary>音名自定义占位文字。</summary>
    [JsonPropertyName("pitch_custom_text")]
    public string PitchCustomText { get; set; } = string.Empty;

    /// <summary>音高曲线颜色。</summary>
    [JsonPropertyName("pitch_curve_color")]
    public string PitchCurveColor { get; set; } = "#FFFFFF";

    /// <summary>版权行版本号（渲染器 <c>RenderConfig.style.app_version</c>；由宿主显式传入）。</summary>
    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>
/// 播放器启动参数 — 对应 1.1.x <c>PlayerLaunchParams</c>。
/// </summary>
/// <remarks>本类型即渲染器 <c>up_set_config</c> 接受的 <c>RenderConfig</c> 主体部分
/// （宿主再补 <c>width</c> / <c>height</c> / <c>fps</c> / <c>output_path</c>）。</remarks>
internal sealed class PlayerLaunchParams
{
    /// <summary>UST 解析结果。</summary>
    [JsonPropertyName("ust")]
    public UstInfo Ust { get; set; } = new();

    /// <summary>显示开关。</summary>
    [JsonPropertyName("show")]
    public ShowConfig Show { get; set; } = new();

    /// <summary>项目信息。</summary>
    [JsonPropertyName("project")]
    public ProjectInfo Project { get; set; } = new();

    /// <summary>播放器样式。</summary>
    [JsonPropertyName("style")]
    public PlayerStyle Style { get; set; } = new();
}

/// <summary>
/// 应用元信息（名称、版本）。
/// </summary>
/// <remarks>
/// 放在 <c>Models</c> 而非 <c>Views</c>：它不含任何视图概念，而
/// <c>Diagnostics</c>（日志抬头）与 <c>Video</c>（<c>.uprd</c> 的 <c>app_version</c>）
/// 都要用它；放在 <c>Views</c> 会迫使这些层反向依赖 UI。
/// </remarks>
internal static class AppInfo
{
    /// <summary>应用名。</summary>
    internal const string Name = "ustPlayer";

    /// <summary>展示用版本号（如 <c>2.0.0</c>）。</summary>
    internal static string Version { get; } = ResolveVersion();

    /// <summary>解析展示用版本号。</summary>
    /// <returns>语义化版本字符串；无法解析时回退 <c>0.0.0</c>。</returns>
    /// <remarks>
    /// 取自程序集信息版本。SDK 会自动在 <c>AssemblyInformationalVersion</c> 后附加
    /// SourceLink 的 <c>+{git-sha}</c> 构建元数据，此处剥掉以保证展示稳定。
    /// </remarks>
    private static string ResolveVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
        return plusIndex > 0 ? informational[..plusIndex] : informational;
    }
}
