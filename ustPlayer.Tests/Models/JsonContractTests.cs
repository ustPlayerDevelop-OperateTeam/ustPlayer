using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using UstPlayer.Models;

using Xunit;

namespace UstPlayer.Tests.Models;

/// <summary>
/// JSON 字段名契约测试。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PlayerLaunchParams"/> 及其嵌套类型直接序列化为渲染器的
/// <c>up_set_config</c> 入参（<c>RenderConfig</c>，见根 <c>API_Docs.md</c>），
/// <c>UstInfo</c> / <c>NoteInfo</c> 也用于 <c>up_set_ust_text</c>。
/// </para>
/// <para>
/// <b>字段名错了不会报错</b>：渲染器的 serde 对未知字段是静默忽略
/// （<c>#[serde(default)]</c>），表现为画面缺内容——属于最难排查的一类缺陷。
/// 因此这里把字段名与默认值逐一钉死：任何人改成 PascalCase 或改错名字都会立刻红灯。
/// </para>
/// </remarks>
public class JsonContractTests
{
    /// <summary>渲染器 <c>RenderConfig</c> 的 <c>show</c> 段字段名（照 <c>API_Docs.md</c> 抄录）。</summary>
    private static readonly string[] ShowKeys =
    [
        "bpm", "play_time", "song_name", "song_author", "ust_author",
        "lyric", "curve_show", "note_name", "ust_lyric", "copyright",
        "font_note", "font_ust_lyric", "font_lrc", "font_other",
        "custom_font_paths",
    ];

    /// <summary>渲染器 <c>RenderConfig</c> 的 <c>style</c> 段字段名。</summary>
    private static readonly string[] StyleKeys =
    [
        "bg_color", "note_color", "lyric_color", "lyric_text_color", "other_text_color",
        "lyric_pos", "fullscreen", "lrc_path", "music_path",
        "silent_display", "silent_custom_text", "end_display", "end_custom_text",
        "pitch_placeholder", "pitch_custom_text", "pitch_curve_color", "app_version",
    ];

    /// <summary><c>UstInfo</c> 的字段名。</summary>
    private static readonly string[] UstKeys = ["version", "tempo", "tracks", "notes"];

    /// <summary><c>NoteInfo</c> 的字段名。</summary>
    private static readonly string[] NoteKeys =
        ["index", "length", "lyric", "note_num", "phoneme", "pitch_bend"];

    /// <summary>渲染器配置顶层字段名（宿主另补 width/height/fps/output_path）。</summary>
    private static readonly string[] TopLevelKeys = ["ust", "show", "project", "style"];

    /// <summary><c>ProjectInfo</c> 的字段名。</summary>
    private static readonly string[] ProjectKeys =
        ["project_name", "song_name", "song_author", "ust_author"];

    /// <summary>序列化后的字段名集合必须与契约**完全相同**（不多不少）。</summary>
    [Fact]
    public void 顶层字段名应与渲染配置契约一致()
    {
        AssertJsonKeysEqual(TopLevelKeys, Serialize(new PlayerLaunchParams()));
    }

    /// <summary><c>ust</c> 段字段名。</summary>
    [Fact]
    public void Ust段字段名应与契约一致()
    {
        AssertJsonKeysEqual(UstKeys, Serialize(new UstInfo()));
    }

    /// <summary><c>notes[]</c> 元素字段名——注意是 <c>note_num</c> 而非 <c>noteNumber</c>。</summary>
    [Fact]
    public void 音符字段名应与契约一致()
    {
        var json = Serialize(new UstInfo
        {
            Notes = [new NoteInfo { Index = "0000", Length = 480, Lyric = "あ", NoteNumber = 69 }],
        });

        using var document = JsonDocument.Parse(json);
        var note = document.RootElement.GetProperty("notes")[0];

        AssertJsonKeysEqual(NoteKeys, note.GetRawText());
    }

    /// <summary><c>show</c> 段字段名。</summary>
    [Fact]
    public void 显示开关字段名应与契约一致()
    {
        AssertJsonKeysEqual(ShowKeys, Serialize(new ShowConfig()));
    }

    /// <summary><c>project</c> 段字段名。</summary>
    [Fact]
    public void 项目信息字段名应与契约一致()
    {
        AssertJsonKeysEqual(ProjectKeys, Serialize(new ProjectInfo()));
    }

    /// <summary>
    /// <c>style</c> 段字段名。
    /// </summary>
    /// <remarks>
    /// 注意 <c>PlayerStyle.BackgroundColor</c> 对应的键是 <c>bg_color</c>——
    /// 属性名与键名刻意不同，正是本测试要守住的地方。
    /// </remarks>
    [Fact]
    public void 样式字段名应与契约一致()
    {
        AssertJsonKeysEqual(StyleKeys, Serialize(new PlayerStyle()));
    }

    /// <summary>默认值必须与 1.1.x 契约一致（颜色格式、枚举稳定 key、速度默认值）。</summary>
    [Fact]
    public void 默认值应与契约一致()
    {
        using var style = JsonDocument.Parse(Serialize(new PlayerStyle()));
        var styleRoot = style.RootElement;

        Assert.Equal("#000000", styleRoot.GetProperty("bg_color").GetString());
        Assert.Equal("#6c6c6c", styleRoot.GetProperty("note_color").GetString());
        Assert.Equal("#FFFFFF", styleRoot.GetProperty("lyric_color").GetString());
        // 存储层只放稳定英文 key，显示文案由 UI 的 tr() 翻译
        Assert.Equal("top", styleRoot.GetProperty("lyric_pos").GetString());
        Assert.Equal("r", styleRoot.GetProperty("silent_display").GetString());
        Assert.Equal("end", styleRoot.GetProperty("end_display").GetString());
        Assert.Equal("none", styleRoot.GetProperty("pitch_placeholder").GetString());
        Assert.True(styleRoot.GetProperty("fullscreen").GetBoolean());

        using var ust = JsonDocument.Parse(Serialize(new UstInfo()));
        Assert.Equal(120.0, ust.RootElement.GetProperty("tempo").GetDouble());
        Assert.Equal(1, ust.RootElement.GetProperty("tracks").GetInt32());
    }

    /// <summary>属性名不得以未标注的形式泄漏到 JSON（PascalCase 是典型错法）。</summary>
    [Fact]
    public void 不得出现未标注的_PascalCase_键()
    {
        var json = Serialize(new PlayerLaunchParams
        {
            Ust = new UstInfo { Notes = [new NoteInfo()] },
        });

        using var document = JsonDocument.Parse(json);

        var offenders = new List<string>();
        CollectPascalCaseKeys(document.RootElement, offenders);

        Assert.True(
            offenders.Count == 0,
            "JSON 中出现了疑似未标注 JsonPropertyName 的 PascalCase 键：" +
            string.Join(", ", offenders));
    }

    private static void CollectPascalCaseKeys(JsonElement element, ICollection<string> offenders)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Length > 0 && char.IsUpper(property.Name[0]))
                    {
                        offenders.Add(property.Name);
                    }

                    CollectPascalCaseKeys(property.Value, offenders);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPascalCaseKeys(item, offenders);
                }

                break;
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private static void AssertJsonKeysEqual(IReadOnlyCollection<string> expected, string json)
    {
        using var document = JsonDocument.Parse(json);

        var actual = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expectedSorted = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedSorted, actual);
    }
}
