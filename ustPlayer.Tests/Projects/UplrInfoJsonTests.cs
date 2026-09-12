using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using UstPlayer.Projects;
using UstPlayer.Settings;

using Xunit;

namespace UstPlayer.Tests.Projects;

/// <summary>
/// <c>Info.json</c> 映射测试。
/// </summary>
/// <remarks>
/// 这些用例守住「格式双向兼容」：字段名、取值形态（布尔是整数 <c>0/1</c>、空串转 <c>null</c>）、
/// 以及 <c>.uplr</c> 与 <c>.uprd</c> 的段结构差异。任何一处写错都不会报错——
/// 只会让 1.1.x 读不到，或让渲染器静默忽略。
/// </remarks>
public class UplrInfoJsonTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public UplrInfoJsonTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"uplr-info-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败无关紧要
        }

        GC.SuppressFinalize(this);
    }

    // ===================== 往返保真 =====================

    /// <summary>
    /// 写入的每一个值都必须能原样读回——**包括空串与 null 的往返**。
    /// </summary>
    /// <remarks>
    /// 空串转 <c>null</c> 是刻意的（1.1.x 的 <c>or None</c>），但 null 读回时必须是空串，
    /// 这样才能保证「导出 → 导入 → 再导出」得到同一份 <c>Info.json</c>。
    /// </remarks>
    [Fact]
    public void 往返后_Info_json_应完全一致()
    {
        var settings = CreateSettings();
        Populate(settings);

        var members = new ResourceMembers();
        var first = UplrInfoJson.BuildUplrInfo(settings, members);

        // 读回另一份全新设置
        var reloaded = CreateSettings();
        UplrInfoJson.ApplyInfoJson(reloaded, first, baseDirectory: string.Empty);

        // 再导出：必须与第一次完全相同
        var second = UplrInfoJson.BuildUplrInfo(reloaded, members);

        Assert.Equal(Serialize(first), Serialize(second));
    }

    /// <summary>往返后各个属性值应逐一相等。</summary>
    [Fact]
    public void 往返后设置值应逐一相等()
    {
        var settings = CreateSettings();
        Populate(settings);

        var info = UplrInfoJson.BuildUplrInfo(settings, new ResourceMembers());
        var reloaded = CreateSettings();
        UplrInfoJson.ApplyInfoJson(reloaded, info, baseDirectory: string.Empty);

        // 逐项收集差异，一次报全（而不是遇到第一个失败就停）
        var differences = new List<string>();
        void Check(string name, object expected, object actual)
        {
            if (!Equals(expected, actual))
            {
                differences.Add($"{name}: 期望 {expected}，实得 {actual}");
            }
        }

        Check("encoding", settings.File.Encoding, reloaded.File.Encoding);
        Check("curve_show", settings.File.CurveShow, reloaded.File.CurveShow);
        Check("project_name", settings.Project.ProjectName, reloaded.Project.ProjectName);
        Check("song_name", settings.Project.SongName, reloaded.Project.SongName);
        Check("song_author", settings.Project.SongAuthor, reloaded.Project.SongAuthor);
        Check("ust_author", settings.Project.UstAuthor, reloaded.Project.UstAuthor);

        Check("show_bpm", settings.Display.ShowBpm, reloaded.Display.ShowBpm);
        Check("show_play_time", settings.Display.ShowPlayTime, reloaded.Display.ShowPlayTime);
        Check("show_song_name", settings.Display.ShowSongName, reloaded.Display.ShowSongName);
        Check("show_song_author", settings.Display.ShowSongAuthor, reloaded.Display.ShowSongAuthor);
        Check("show_ust_author", settings.Display.ShowUstAuthor, reloaded.Display.ShowUstAuthor);
        Check("fullscreen", settings.Display.Fullscreen, reloaded.Display.Fullscreen);
        Check("show_lyric", settings.Display.ShowLyric, reloaded.Display.ShowLyric);
        Check("show_note_name", settings.Display.ShowNoteName, reloaded.Display.ShowNoteName);
        Check("show_ust_lyric", settings.Display.ShowUstLyric, reloaded.Display.ShowUstLyric);
        Check("show_copyright", settings.Display.ShowCopyright, reloaded.Display.ShowCopyright);

        Check("font_note", settings.Display.FontNote, reloaded.Display.FontNote);
        Check("font_ust_lyric", settings.Display.FontUstLyric, reloaded.Display.FontUstLyric);
        Check("font_lrc", settings.Display.FontLrc, reloaded.Display.FontLrc);
        Check("font_other", settings.Display.FontOther, reloaded.Display.FontOther);
        Check(
            "custom_font_paths",
            string.Join('|', settings.Display.CustomFontPaths),
            string.Join('|', reloaded.Display.CustomFontPaths));

        Check("bg_color", settings.Color.BackgroundColor, reloaded.Color.BackgroundColor);
        Check("note_color", settings.Color.NoteColor, reloaded.Color.NoteColor);
        Check("lyric_color", settings.Color.LyricColor, reloaded.Color.LyricColor);
        Check("lyric_text_color", settings.Color.LyricTextColor, reloaded.Color.LyricTextColor);
        Check("other_text_color", settings.Color.OtherTextColor, reloaded.Color.OtherTextColor);
        Check("pitch_curve_color", settings.Color.PitchCurveColor, reloaded.Color.PitchCurveColor);

        Check("lyric_pos", settings.Player.LyricPosition, reloaded.Player.LyricPosition);
        Check("silent_display", settings.Player.SilentDisplay, reloaded.Player.SilentDisplay);
        Check("silent_custom_text", settings.Player.SilentCustomText, reloaded.Player.SilentCustomText);
        Check("end_display", settings.Player.EndDisplay, reloaded.Player.EndDisplay);
        Check("end_custom_text", settings.Player.EndCustomText, reloaded.Player.EndCustomText);
        Check("pitch_placeholder", settings.Player.PitchPlaceholder, reloaded.Player.PitchPlaceholder);
        Check("pitch_custom_text", settings.Player.PitchCustomText, reloaded.Player.PitchCustomText);

        Assert.True(differences.Count == 0, "往返后以下字段不一致：" + string.Join("；", differences));
    }

    /// <summary>
    /// 布尔必须写成**整数** <c>0/1</c>——既不是 <c>true/false</c>，
    /// 也不是设置文件里的字符串 <c>"1"/"0"</c>。同一个概念三处写法不同。
    /// </summary>
    [Fact]
    public void 布尔应写成整数零和一()
    {
        var settings = CreateSettings();
        settings.Display.ShowBpm = true;
        settings.Display.ShowLyric = false;
        settings.File.CurveShow = true;

        var info = UplrInfoJson.BuildUplrInfo(settings, new ResourceMembers());
        var display = (JsonObject)info["display"]!;

        Assert.Equal(JsonValueKind.Number, display["show_bpm"]!.GetValueKind());
        Assert.Equal(1, display["show_bpm"]!.GetValue<int>());
        Assert.Equal(0, display["show_lyric"]!.GetValue<int>());
        Assert.Equal(1, display["curve_show"]!.GetValue<int>());
    }

    /// <summary>空字符串与空列表转 <c>null</c>（对应 Python 的 <c>or None</c>）。</summary>
    [Fact]
    public void 空值与空列表转为_null()
    {
        var settings = CreateSettings();

        var info = UplrInfoJson.BuildUplrInfo(settings, new ResourceMembers());
        var basic = (JsonObject)info["basic"]!;
        var display = (JsonObject)info["display"]!;
        var elseSection = (JsonObject)info["else"]!;

        Assert.Null(basic["project_name"]);
        Assert.Null(basic["song_name"]);
        Assert.Null(display["font_note"]);
        Assert.Null(display["custom_font_paths"]);
        Assert.Null(elseSection["silent_custom_text"]);
        Assert.Null(elseSection["end_custom_text"]);
        Assert.Null(elseSection["pitch_custom_text"]);
    }

    /// <summary>资源未收集时 <c>ust_path</c> / <c>lrc_path</c> / <c>music_path</c> 为 <c>null</c>。</summary>
    [Fact]
    public void 未收集资源时为_null()
    {
        var settings = CreateSettings();

        var info = UplrInfoJson.BuildUplrInfo(settings, new ResourceMembers());

        Assert.Null(((JsonObject)info["basic"]!)["ust_path"]);
        Assert.Null(((JsonObject)info["basic"]!)["music_path"]);
        Assert.Null(((JsonObject)info["else"]!)["lrc_path"]);
    }

    /// <summary>资源名以包内文件名（相对名）写入，而不是本机绝对路径。</summary>
    [Fact]
    public void 资源以包内文件名写入()
    {
        var settings = CreateSettings();
        var members = new ResourceMembers
        {
            // 直接构造清单，避免依赖真实文件
        };

        var ustFile = CreateTempFile("song.ust");
        Assert.True(members.Add("ust_path", ustFile));

        var info = UplrInfoJson.BuildUplrInfo(settings, members);

        Assert.Equal("song.ust", ((JsonObject)info["basic"]!)["ust_path"]!.GetValue<string>());
    }

    // ===================== .uplr 与 .uprd 的结构差异 =====================

    /// <summary><c>.uplr</c> 的 <c>curve_show</c> 在 <c>display</c> 段。</summary>
    [Fact]
    public void uplr_的_curve_show_在_display_段()
    {
        var settings = CreateSettings();

        var info = UplrInfoJson.BuildUplrInfo(settings, new ResourceMembers());

        Assert.True(((JsonObject)info["display"]!).ContainsKey("curve_show"));
        Assert.False(((JsonObject)info["else"]!).ContainsKey("curve_show"));
    }

    /// <summary>
    /// <c>.uprd</c> 的 <c>curve_show</c> 在 <c>else</c> 段，且 <c>display</c> 另含
    /// 渲染器不消费的三个开关（恒 <c>0</c>），并新增 <c>video</c> 段。
    /// </summary>
    [Fact]
    public void uprd_的结构差异()
    {
        var settings = CreateSettings();
        settings.File.CurveShow = true;

        var info = UplrInfoJson.BuildUprdInfo(settings, new ResourceMembers(), (1920, 1080, 60));
        var display = (JsonObject)info["display"]!;
        var elseSection = (JsonObject)info["else"]!;

        Assert.False(display.ContainsKey("curve_show"));
        Assert.Equal(1, elseSection["curve_show"]!.GetValue<int>());

        Assert.Equal(0, display["show_phoneme"]!.GetValue<int>());
        Assert.Equal(0, display["show_midinote"]!.GetValue<int>());
        Assert.Equal(0, display["show_waveform"]!.GetValue<int>());

        var video = (JsonObject)info["video"]!;
        Assert.Equal(1920, video["width"]!.GetValue<int>());
        Assert.Equal(1080, video["height"]!.GetValue<int>());
        Assert.Equal(60, video["fps"]!.GetValue<int>());
    }

    /// <summary>
    /// <c>.uprd</c> 归一化后应能被当作 <c>.uplr</c> 导入：<c>curve_show</c> 回迁、
    /// 多余开关被移除、枚举中的旧中文值被迁移。
    /// </summary>
    [Fact]
    public void uprd_归一化后可导入()
    {
        var uprdDisplay = new JsonObject
        {
            ["show_bpm"] = 1,
            ["show_phoneme"] = 0,
            ["show_midinote"] = 0,
            ["show_waveform"] = 0,
            ["font_note"] = "微软雅黑",
        };

        var uprdElse = new JsonObject
        {
            // .uprd 的历史写法：枚举是中文旧值，curve_show 在 else
            ["lyric_pos"] = "上",
            ["silent_display"] = "R",
            ["end_display"] = "END",
            ["pitch_placeholder"] = "无",
            ["curve_show"] = 1,
        };

        var source = new JsonObject
        {
            ["encoding"] = "Shift-JIS",
            ["basic"] = new JsonObject { ["project_name"] = "工程" },
            ["display"] = uprdDisplay,
            ["color"] = new JsonObject { ["bg_color"] = "#000000" },
            ["else"] = uprdElse,
            ["video"] = new JsonObject { ["width"] = 1920, ["height"] = 1080, ["fps"] = 60 },
        };

        var normalized = UplrInfoJson.NormalizeUprdInfo(source);
        var display = (JsonObject)normalized["display"]!;
        var elseSection = (JsonObject)normalized["else"]!;

        // 多余开关被移除
        Assert.False(display.ContainsKey("show_phoneme"));
        Assert.False(display.ContainsKey("show_midinote"));
        Assert.False(display.ContainsKey("show_waveform"));

        // curve_show 回迁到 display
        Assert.Equal(1, display["curve_show"]!.GetValue<int>());
        Assert.False(elseSection.ContainsKey("curve_show"));

        // 枚举迁移为英文 key
        Assert.Equal("top", elseSection["lyric_pos"]!.GetValue<string>());
        Assert.Equal("r", elseSection["silent_display"]!.GetValue<string>());
        Assert.Equal("end", elseSection["end_display"]!.GetValue<string>());
        Assert.Equal("none", elseSection["pitch_placeholder"]!.GetValue<string>());

        // 缺失的 pitch_curve_color 补默认
        Assert.Equal("#FFFFFF", ((JsonObject)normalized["color"]!)["pitch_curve_color"]!.GetValue<string>());

        // 归一化结果可直接应用到设置
        var settings = CreateSettings();
        UplrInfoJson.ApplyInfoJson(settings, normalized, baseDirectory: string.Empty);

        Assert.Equal("工程", settings.Project.ProjectName);
        Assert.True(settings.File.CurveShow);
        Assert.Equal("top", settings.Player.LyricPosition);
        Assert.Equal("r", settings.Player.SilentDisplay);
    }

    // ===================== 宽松读取 =====================

    /// <summary>字段缺失时一律回退默认值，不抛异常。</summary>
    [Fact]
    public void 字段缺失时回退默认值()
    {
        var settings = CreateSettings();
        Populate(settings);

        // 空对象：应把全部被触碰的字段重置为默认
        UplrInfoJson.ApplyInfoJson(settings, new JsonObject(), baseDirectory: string.Empty);

        Assert.Equal("Shift-JIS", settings.File.Encoding);
        Assert.False(settings.File.CurveShow);
        Assert.Equal(string.Empty, settings.Project.ProjectName);
        Assert.False(settings.Display.ShowLyric);
        Assert.Equal("top", settings.Player.LyricPosition);
        Assert.Equal("r", settings.Player.SilentDisplay);
        Assert.Equal("#000000", settings.Color.BackgroundColor);
    }

    /// <summary>类型不对的字段被忽略（回退默认），而不是抛出。</summary>
    [Fact]
    public void 类型不对时回退默认()
    {
        var info = new JsonObject
        {
            ["encoding"] = 12345,
            ["basic"] = "不是对象",
            ["display"] = new JsonObject { ["show_bpm"] = "不是布尔" },
            ["color"] = new JsonObject { ["bg_color"] = 42 },
            ["else"] = new JsonArray(),
        };

        var settings = CreateSettings();
        UplrInfoJson.ApplyInfoJson(settings, info, baseDirectory: string.Empty);

        Assert.Equal("Shift-JIS", settings.File.Encoding);
        Assert.Equal(string.Empty, settings.Project.ProjectName);
        Assert.False(settings.Display.ShowBpm);
        Assert.Equal("#000000", settings.Color.BackgroundColor);
        Assert.Equal("top", settings.Player.LyricPosition);
    }

    /// <summary>读取设置文件里的字符串布尔写法也应被接受（整数、字符串、布尔三者通吃）。</summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    public void 兼容字符串布尔写法(string raw, bool expected)
    {
        var info = new JsonObject
        {
            ["display"] = new JsonObject { ["show_bpm"] = raw },
        };

        var settings = CreateSettings();
        UplrInfoJson.ApplyInfoJson(settings, info, baseDirectory: string.Empty);

        Assert.Equal(expected, settings.Display.ShowBpm);
    }

    // ===================== 辅助 =====================

    private SettingsManager CreateSettings() =>
        new(Path.Combine(_tempDirectory, $"Settings-{Guid.NewGuid():N}.json"));

    private string CreateTempFile(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, "dummy");
        return path;
    }

    private static void Populate(SettingsManager settings)
    {
        settings.File.Encoding = "UTF-8";
        settings.File.CurveShow = true;

        settings.Project.ProjectName = "测试工程";
        settings.Project.SongName = "测试曲";
        settings.Project.SongAuthor = "曲作者";
        settings.Project.UstAuthor = "调音师";

        settings.Display.ShowBpm = false;
        settings.Display.ShowLyric = true;
        settings.Display.FontNote = "微软雅黑";
        settings.Display.FontUstLyric = "等线";
        settings.Display.FontLrc = "黑体";
        settings.Display.FontOther = "宋体";
        settings.Display.CustomFontPaths = ["D:\\fonts\\a.ttf", "D:\\fonts\\b.otf"];

        settings.Color.BackgroundColor = "#123456";
        settings.Color.NoteColor = "#654321";
        settings.Color.PitchCurveColor = "#ABCDEF";

        settings.Player.LyricPosition = "bottom";
        settings.Player.SilentDisplay = "dash";
        settings.Player.SilentCustomText = "（空）";
        settings.Player.EndDisplay = "custom";
        settings.Player.EndCustomText = "（完）";
        settings.Player.PitchPlaceholder = "dash";
        settings.Player.PitchCustomText = "升";
    }

    private static string Serialize(JsonObject info) =>
        info.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
}
