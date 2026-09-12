using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using UstPlayer.Settings;
using UstPlayer.Settings.Domains;

using Xunit;

namespace UstPlayer.Tests.Settings;

/// <summary>
/// 设置子域的读写与兼容性测试。
/// </summary>
/// <remarks>
/// 重点是把 <c>Settings.json</c> 的**键名与取值形态**钉死——它们是与 1.1.x
/// 双向兼容的硬承诺，写错一个键名不会报错，只会让对方的设置静默丢失。
/// </remarks>
public class SettingsDomainsTests
{
    // ===================== 分组名与键名契约 =====================

    /// <summary>整套设置写出的分组名必须与 1.1.x 逐字一致。</summary>
    [Fact]
    public void 分组名应与_1_1_一致()
    {
        var config = new SettingsConfig(new JsonObject());
        var settings = CreateAll();

        foreach (var domain in settings)
        {
            Write(domain, config);
        }

        var sections = config.Root.Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal);
        var expected = new[]
        {
            "ColorSettings", "DisplaySettings", "FileSettings",
            "LyricSettings", "PlayerSettings", "ProjectSettings",
            "ThemeSettings",
        };

        // LanguageSettings 单独断言（它不参与 .uplr，但同样要落盘）
        Assert.Equal(expected, sections.Where(name => name != "LanguageSettings"));
    }

    /// <summary>语言设置写入独立分组。</summary>
    [Fact]
    public void 语言设置写入独立分组()
    {
        var config = new SettingsConfig(new JsonObject());
        new LanguageSettings().WriteTo(config);

        Assert.Equal(["LanguageSettings"], config.Root.Select(pair => pair.Key));
    }

    /// <summary>各分组的键名集合必须与 1.1.x 逐字一致。</summary>
    [Fact]
    public void 各组键名应与_1_1_一致()
    {
        var config = new SettingsConfig(new JsonObject());
        foreach (var domain in CreateAll())
        {
            Write(domain, config);
        }

        new LanguageSettings().WriteTo(config);

        AssertKeys(config, "ProjectSettings",
            "project_name", "song_name", "song_author", "ust_author", "music_path");

        AssertKeys(config, "FileSettings", "ust_path", "encoding", "curve_show");

        AssertKeys(config, "DisplaySettings",
            "show_bpm", "show_play_time", "show_song_name", "show_song_author", "show_ust_author",
            "fullscreen", "show_lyric", "show_note_name", "show_ust_lyric", "show_copyright",
            "font_note", "font_ust_lyric", "font_lrc", "font_other", "custom_font_paths");

        AssertKeys(config, "ColorSettings",
            "bg_color", "note_color", "lyric_color", "lyric_text_color",
            "other_text_color", "pitch_curve_color");

        AssertKeys(config, "PlayerSettings",
            "lyric_pos", "silent_display", "silent_custom_text",
            "end_display", "end_custom_text", "pitch_placeholder", "pitch_custom_text");

        AssertKeys(config, "LyricSettings", "lrc_path");
        AssertKeys(config, "ThemeSettings",
            "theme_mode", "accent_color_mode", "custom_accent_color", "window_effect");
        AssertKeys(config, "LanguageSettings", "language");
    }

    /// <summary>
    /// 布尔值必须写成字符串 <c>"1"</c>/<c>"0"</c>，而不是 JSON 布尔——
    /// 1.1.x 就是这么存的（<c>"1" if self._x else "0"</c>）。
    /// </summary>
    [Fact]
    public void 布尔值应写成字符串一和零()
    {
        var config = new SettingsConfig(new JsonObject());

        var file = new FileSettings { CurveShow = true };
        file.WriteTo(config);

        var display = new DisplaySettings();
        display.WriteTo(config);

        Assert.Equal("1", NodeText(config, "FileSettings", "curve_show"));
        Assert.Equal("1", NodeText(config, "DisplaySettings", "show_bpm"));
        Assert.Equal("0", NodeText(config, "DisplaySettings", "show_lyric"));
    }

    /// <summary>读取端应同时接受字符串 <c>"1"</c> 与 JSON 布尔（向后与向前都兼容）。</summary>
    [Theory]
    [InlineData("\"1\"", true)]
    [InlineData("\"0\"", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"yes\"", true)]
    [InlineData("\"on\"", true)]
    [InlineData("\"false\"", false)]
    [InlineData("\"随便什么\"", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void 宽松布尔解析(string rawJson, bool expected)
    {
        var config = new SettingsConfig(new JsonObject
        {
            ["FileSettings"] = new JsonObject { ["curve_show"] = JsonNode.Parse(rawJson) },
        });

        var file = new FileSettings();
        file.ReadFrom(config);

        Assert.Equal(expected, file.CurveShow);
    }

    // ===================== 默认值 =====================

    /// <summary>默认值必须与 1.1.x 一致，其中 <c>ShowLyric</c> 默认是 <b>关闭</b>。</summary>
    [Fact]
    public void 默认值应与_1_1_一致()
    {
        var display = new DisplaySettings();

        Assert.True(display.ShowBpm);
        Assert.True(display.ShowPlayTime);
        Assert.True(display.ShowSongName);
        Assert.True(display.ShowSongAuthor);
        Assert.True(display.ShowUstAuthor);
        Assert.True(display.Fullscreen);
        Assert.False(display.ShowLyric);
        Assert.True(display.ShowNoteName);
        Assert.True(display.ShowUstLyric);
        Assert.True(display.ShowCopyright);

        var file = new FileSettings();
        Assert.Equal("Shift-JIS", file.Encoding);
        Assert.False(file.CurveShow);

        var color = new ColorSettings();
        Assert.Equal("#000000", color.BackgroundColor);
        Assert.Equal("#6c6c6c", color.NoteColor);
        Assert.Equal("#FFFFFF", color.LyricColor);

        var player = new PlayerSettings();
        Assert.Equal("top", player.LyricPosition);
        Assert.Equal("r", player.SilentDisplay);
        Assert.Equal("end", player.EndDisplay);
        Assert.Equal("none", player.PitchPlaceholder);

        var theme = new ThemeSettings();
        Assert.Equal("auto", theme.ThemeMode);
        Assert.Equal("auto", theme.AccentColorMode);
        Assert.Equal("#009faa", theme.CustomAccentColor);
        Assert.Equal("mica", theme.WindowEffect);

        Assert.Equal("system", new LanguageSettings().Language);
    }

    // ===================== 旧中文值迁移 =====================

    /// <summary>
    /// 迁移后**绝不留中文**：旧文件的枚举值经读取后必须都是英文 key。
    /// </summary>
    /// <remarks>
    /// 精确到字段的迁移映射由 <see cref="按字段精确迁移"/> 覆盖；
    /// 本条只守住「存储层不得残留显示文案」这条仓库约定。
    /// </remarks>
    [Fact]
    public void 迁移后不留中文枚举值()
    {
        string[] legacyValues = ["上", "下", "R", "END", "自定义文字", "什么都不显示", "无"];

        foreach (var legacy in legacyValues)
        {
            var group = new JsonObject
            {
                ["lyric_pos"] = legacy,
                ["silent_display"] = legacy,
                ["end_display"] = legacy,
                ["pitch_placeholder"] = legacy,
            };

            var config = new SettingsConfig(new JsonObject { ["PlayerSettings"] = group });
            var player = new PlayerSettings();
            player.ReadFrom(config);

            Assert.Matches("^[a-z]+$", player.LyricPosition);
            Assert.Matches("^[a-z]+$", player.SilentDisplay);
            Assert.Matches("^[a-z]+$", player.EndDisplay);
            Assert.Matches("^[a-z]+$", player.PitchPlaceholder);
        }
    }

    /// <summary>按字段的精确迁移验证。</summary>
    [Theory]
    [InlineData("lyric_pos", "上", "top")]
    [InlineData("lyric_pos", "下", "bottom")]
    [InlineData("silent_display", "R", "r")]
    [InlineData("silent_display", "自定义文字", "custom")]
    [InlineData("silent_display", "什么都不显示", "none")]
    [InlineData("end_display", "END", "end")]
    [InlineData("end_display", "-", "dash")]
    [InlineData("pitch_placeholder", "无", "none")]
    [InlineData("pitch_placeholder", "自定义文字", "custom")]
    public void 按字段精确迁移(string key, string legacy, string expected)
    {
        var config = new SettingsConfig(new JsonObject
        {
            ["PlayerSettings"] = new JsonObject { [key] = legacy },
        });

        var player = new PlayerSettings();
        player.ReadFrom(config);

        var actual = key switch
        {
            "lyric_pos" => player.LyricPosition,
            "silent_display" => player.SilentDisplay,
            "end_display" => player.EndDisplay,
            _ => player.PitchPlaceholder,
        };

        Assert.Equal(expected, actual);
    }

    /// <summary>既非合法 key 也非已知旧值的枚举值回退各自默认。</summary>
    [Fact]
    public void 未知枚举值回退默认()
    {
        var config = new SettingsConfig(new JsonObject
        {
            ["PlayerSettings"] = new JsonObject
            {
                ["lyric_pos"] = "中间",
                ["silent_display"] = "???",
                ["end_display"] = "???",
                ["pitch_placeholder"] = "???",
            },
        });

        var player = new PlayerSettings();
        player.ReadFrom(config);

        Assert.Equal("top", player.LyricPosition);
        Assert.Equal("r", player.SilentDisplay);
        Assert.Equal("end", player.EndDisplay);
        Assert.Equal("none", player.PitchPlaceholder);
    }

    /// <summary>合法的英文 key 原样保留（不被迁移表干扰）。</summary>
    [Fact]
    public void 合法英文_key_原样保留()
    {
        var config = new SettingsConfig(new JsonObject
        {
            ["PlayerSettings"] = new JsonObject
            {
                ["lyric_pos"] = "bottom",
                ["silent_display"] = "dash",
                ["end_display"] = "custom",
                ["pitch_placeholder"] = "custom",
            },
        });

        var player = new PlayerSettings();
        player.ReadFrom(config);

        Assert.Equal("bottom", player.LyricPosition);
        Assert.Equal("dash", player.SilentDisplay);
        Assert.Equal("custom", player.EndDisplay);
        Assert.Equal("custom", player.PitchPlaceholder);
    }

    // ===================== 颜色校验 =====================

    /// <summary>非法颜色在 setter 即回退默认，不会进入存储层。</summary>
    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("123456")]
    [InlineData("")]
    public void 非法颜色回退默认(string invalid)
    {
        var color = new ColorSettings { BackgroundColor = invalid };

        Assert.Equal("#000000", color.BackgroundColor);
    }

    /// <summary>合法颜色（大小写混合）原样保留。</summary>
    [Theory]
    [InlineData("#abcdef")]
    [InlineData("#ABCDEF")]
    [InlineData("#123456")]
    public void 合法颜色原样保留(string valid)
    {
        var color = new ColorSettings { BackgroundColor = valid };

        Assert.Equal(valid, color.BackgroundColor);
    }

    // ===================== 往返 =====================

    /// <summary>写入后再读出，所有值应完全一致。</summary>
    [Fact]
    public void 写入后再读出保持一致()
    {
        var config = new SettingsConfig(new JsonObject());

        var project = new ProjectSettings
        {
            ProjectName = "测试工程",
            SongName = "测试曲",
            SongAuthor = "作者",
            UstAuthor = "调音师",
            MusicPath = @"D:\music\a.wav",
        };
        var file = new FileSettings
        {
            UstPath = @"D:\ust\a.ust",
            Encoding = "UTF-8",
            CurveShow = true,
        };
        var display = new DisplaySettings
        {
            ShowBpm = false,
            ShowLyric = true,
            FontNote = "微软雅黑",
            CustomFontPaths = ["D:\\fonts\\a.ttf"],
        };
        var player = new PlayerSettings
        {
            LyricPosition = "bottom",
            SilentDisplay = "dash",
            SilentCustomText = "（空）",
            EndDisplay = "custom",
            EndCustomText = "（完）",
            PitchPlaceholder = "custom",
            PitchCustomText = "升",
            LrcPath = @"D:\lrc\a.lrc",
        };

        project.WriteTo(config);
        file.WriteTo(config);
        display.WriteTo(config);
        player.WriteTo(config);

        var reloadedProject = new ProjectSettings();
        var reloadedFile = new FileSettings();
        var reloadedDisplay = new DisplaySettings();
        var reloadedPlayer = new PlayerSettings();

        reloadedProject.ReadFrom(config);
        reloadedFile.ReadFrom(config);
        reloadedDisplay.ReadFrom(config);
        reloadedPlayer.ReadFrom(config);

        Assert.Equal("测试工程", reloadedProject.ProjectName);
        Assert.Equal(@"D:\music\a.wav", reloadedProject.MusicPath);
        Assert.Equal(@"D:\ust\a.ust", reloadedFile.UstPath);
        Assert.Equal("UTF-8", reloadedFile.Encoding);
        Assert.True(reloadedFile.CurveShow);

        Assert.False(reloadedDisplay.ShowBpm);
        Assert.True(reloadedDisplay.ShowLyric);
        Assert.Equal("微软雅黑", reloadedDisplay.FontNote);
        Assert.Equal(["D:\\fonts\\a.ttf"], reloadedDisplay.CustomFontPaths);

        Assert.Equal("bottom", reloadedPlayer.LyricPosition);
        Assert.Equal("dash", reloadedPlayer.SilentDisplay);
        Assert.Equal("（空）", reloadedPlayer.SilentCustomText);
        Assert.Equal("custom", reloadedPlayer.EndDisplay);
        Assert.Equal("（完）", reloadedPlayer.EndCustomText);
        Assert.Equal("custom", reloadedPlayer.PitchPlaceholder);
        Assert.Equal("升", reloadedPlayer.PitchCustomText);
        Assert.Equal(@"D:\lrc\a.lrc", reloadedPlayer.LrcPath);
    }

    // ===================== 变更通知 =====================

    /// <summary>属性变化触发通知；同值重复赋值不触发（与 1.1.x 的 <c>if != </c> 守卫一致）。</summary>
    [Fact]
    public void 变更通知只在值实际变化时触发()
    {
        var file = new FileSettings();
        var notifications = new List<string?>();
        file.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        file.UstPath = "a.ust";
        file.UstPath = "a.ust";
        file.CurveShow = true;

        Assert.Equal(2, notifications.Count);
        Assert.Contains("UstPath", notifications);
        Assert.Contains("CurveShow", notifications);
    }

    // ===================== 辅助 =====================

    /// <summary>创建全部子域（用于整体写入）。基类是泛型自引用，故此处以 <see cref="object"/> 承载。</summary>
    private static object[] CreateAll() =>
    [
        new ProjectSettings(),
        new FileSettings(),
        new DisplaySettings(),
        new ColorSettings(),
        new PlayerSettings(),
        new ThemeSettings(),
    ];

    /// <summary>把子域写入配置（基类是泛型自引用，无法作集合元素类型，故按具体类型分派）。</summary>
    /// <param name="domain">子域实例。</param>
    /// <param name="config">目标配置。</param>
    private static void Write(object domain, SettingsConfig config)
    {
        switch (domain)
        {
            case ProjectSettings project:
                project.WriteTo(config);
                break;

            case FileSettings file:
                file.WriteTo(config);
                break;

            case DisplaySettings display:
                display.WriteTo(config);
                break;

            case ColorSettings color:
                color.WriteTo(config);
                break;

            case PlayerSettings player:
                player.WriteTo(config);
                break;

            case ThemeSettings theme:
                theme.WriteTo(config);
                break;

            default:
                throw new ArgumentException($"未知的设置子域：{domain.GetType().Name}", nameof(domain));
        }
    }

    private static void AssertKeys(SettingsConfig config, string section, params string[] expected)
    {
        var group = config.GetSection(section);
        Assert.NotNull(group);

        var node = (JsonObject)config.Root[section]!;
        var actual = node.Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var sortedExpected = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(sortedExpected, actual);
    }

    private static string? NodeText(SettingsConfig config, string section, string key)
    {
        var value = config.Root[section]?[key];
        return value?.GetValue<string>();
    }
}
