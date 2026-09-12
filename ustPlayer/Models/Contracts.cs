using System.Collections.Generic;

namespace UstPlayer.Models;

/// <summary>
/// UST 音符数据 — 对应 1.1.x <c>core/contracts.py</c> 的 <c>NoteInfo</c>。
/// </summary>
/// <remarks>
/// 属性可写：解析器逐行填充字段，且 OpenUtau 风格音高曲线需要在校验后再合成
/// <see cref="PitchBend"/>（对应 Python 版逐行赋值 + <c>_finalize_pitch_bend</c> 的行为）。
/// JSON 字段名须与渲染器 <c>UstInfo</c> 契约一致（见根 <c>API_Docs.md</c>）。
/// </remarks>
internal sealed class NoteInfo
{
    /// <summary>段编号，如 <c>"0000"</c>。</summary>
    public string Index { get; set; } = string.Empty;

    /// <summary>长度（tick）。</summary>
    public int Length { get; set; }

    /// <summary>歌词。</summary>
    public string Lyric { get; set; } = string.Empty;

    /// <summary>MIDI 音号。</summary>
    public int NoteNumber { get; set; }

    /// <summary>音素，可为空。</summary>
    public string Phoneme { get; set; } = string.Empty;

    /// <summary>音高曲线点序列（音分）。</summary>
    public List<int> PitchBend { get; set; } = [];
}

/// <summary>
/// UST 解析结果 — 对应 1.1.x <c>UstInfo</c>。
/// </summary>
internal sealed class UstInfo
{
    /// <summary>UST 版本号（如 <c>UST Version1.2</c>）。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>速度（BPM），默认 120。</summary>
    public double Tempo { get; set; } = 120.0;

    /// <summary>轨道数。</summary>
    public int Tracks { get; set; } = 1;

    /// <summary>音符序列。</summary>
    public List<NoteInfo> Notes { get; set; } = [];
}

/// <summary>
/// 项目信息 — 对应 1.1.x <c>ProjectInfo</c> 与 <c>.uplr</c> 的 <c>basic</c> 段。
/// </summary>
internal sealed class ProjectInfo
{
    /// <summary>工程名。</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>曲名。</summary>
    public string SongName { get; set; } = string.Empty;

    /// <summary>曲作者。</summary>
    public string SongAuthor { get; set; } = string.Empty;

    /// <summary>调音师。</summary>
    public string UstAuthor { get; set; } = string.Empty;
}

/// <summary>
/// 播放器显示开关 — 对应 1.1.x <c>ShowConfig</c> 与渲染器 <c>RenderConfig.show</c>。
/// </summary>
internal sealed class ShowConfig
{
    /// <summary>显示 BPM。</summary>
    public bool Bpm { get; set; } = true;

    /// <summary>显示播放时间。</summary>
    public bool PlayTime { get; set; } = true;

    /// <summary>显示曲名。</summary>
    public bool SongName { get; set; } = true;

    /// <summary>显示曲作者。</summary>
    public bool SongAuthor { get; set; } = true;

    /// <summary>显示调音师。</summary>
    public bool UstAuthor { get; set; } = true;

    /// <summary>显示 LRC 歌词。</summary>
    public bool Lyric { get; set; } = true;

    /// <summary>显示音高曲线。</summary>
    public bool CurveShow { get; set; }

    /// <summary>显示音名。</summary>
    public bool NoteName { get; set; } = true;

    /// <summary>显示歌字（UST 歌词）。</summary>
    public bool UstLyric { get; set; } = true;

    /// <summary>显示底部版权信息。</summary>
    public bool Copyright { get; set; } = true;

    /// <summary>音名字体族（空 = 语言默认）。</summary>
    public string FontNote { get; set; } = string.Empty;

    /// <summary>歌字字体族。</summary>
    public string FontUstLyric { get; set; } = string.Empty;

    /// <summary>LRC 歌词字体族。</summary>
    public string FontLrc { get; set; } = string.Empty;

    /// <summary>其他文字（BPM / 时间 / 标题 / 版权）字体族。</summary>
    public string FontOther { get; set; } = string.Empty;

    /// <summary>自定义字体文件路径（恢复注册用）。</summary>
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
    public string BackgroundColor { get; set; } = "#000000";

    /// <summary>音名颜色。</summary>
    public string NoteColor { get; set; } = "#6c6c6c";

    /// <summary>歌字颜色。</summary>
    public string LyricColor { get; set; } = "#FFFFFF";

    /// <summary>LRC 歌词颜色。</summary>
    public string LyricTextColor { get; set; } = "#FFFFFF";

    /// <summary>其他文字颜色。</summary>
    public string OtherTextColor { get; set; } = "#FFFFFF";

    /// <summary>歌词位置（<c>top</c> / <c>bottom</c>）。</summary>
    public string LyricPosition { get; set; } = "top";

    /// <summary>是否全屏。</summary>
    public bool Fullscreen { get; set; } = true;

    /// <summary>LRC 文件路径。</summary>
    public string LrcPath { get; set; } = string.Empty;

    /// <summary>伴奏文件路径。</summary>
    public string MusicPath { get; set; } = string.Empty;

    /// <summary>空拍显示方式（<c>r</c> / <c>dash</c> / <c>custom</c> / <c>none</c>）。</summary>
    public string SilentDisplay { get; set; } = "r";

    /// <summary>空拍自定义文字。</summary>
    public string SilentCustomText { get; set; } = string.Empty;

    /// <summary>结束显示方式（<c>end</c> / <c>dash</c> / <c>custom</c> / <c>none</c>）。</summary>
    public string EndDisplay { get; set; } = "end";

    /// <summary>结束自定义文字。</summary>
    public string EndCustomText { get; set; } = string.Empty;

    /// <summary>音名占位符规则（<c>none</c> / <c>dash</c> / <c>custom</c>）。</summary>
    public string PitchPlaceholder { get; set; } = "none";

    /// <summary>音名自定义占位文字。</summary>
    public string PitchCustomText { get; set; } = string.Empty;

    /// <summary>音高曲线颜色。</summary>
    public string PitchCurveColor { get; set; } = "#FFFFFF";

    /// <summary>版权行版本号（渲染器 <c>RenderConfig.style.app_version</c>；由宿主显式传入）。</summary>
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>
/// 播放器启动参数 — 对应 1.1.x <c>PlayerLaunchParams</c>。
/// </summary>
internal sealed class PlayerLaunchParams
{
    /// <summary>UST 解析结果。</summary>
    public UstInfo Ust { get; set; } = new();

    /// <summary>显示开关。</summary>
    public ShowConfig Show { get; set; } = new();

    /// <summary>项目信息。</summary>
    public ProjectInfo Project { get; set; } = new();

    /// <summary>播放器样式。</summary>
    public PlayerStyle Style { get; set; } = new();
}
