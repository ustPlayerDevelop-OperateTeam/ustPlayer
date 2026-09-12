using System.Text.Json;
using System.Text.Json.Nodes;

using UstPlayer.Settings;

using Xunit;

namespace UstPlayer.Tests.Settings;

/// <summary>
/// 宽松布尔转换的回归测试。
/// </summary>
/// <remarks>
/// <para>
/// 这组用例的存在是因为一个**真实踩到的坑**：<see cref="JsonValue"/> 会保留创建时的具体数值类型，
/// 而 <c>TryGetValue&lt;double&gt;()</c> **不做隐式数值转换**。于是
/// <c>JsonValue.Create(1)</c>（内部是 <c>int</c>）用 <c>TryGetValue&lt;double&gt;</c> 读会返回
/// <see langword="false"/>，导致所有整数标志落到 fallback 分支——表现为「布尔值被取反」。
/// </para>
/// <para>
/// 这类错误不抛异常、不报错，只让设置静默用错默认值（本项目的 <c>.uplr</c> 里布尔正是整数
/// <c>0/1</c>，所以影响面很大）。因此这里把每种数值后备类型都固定下来。
/// </para>
/// </remarks>
public class SettingsValueConverterTests
{
    /// <summary>从 JSON 字面量解析出的节点（后端是 <c>JsonElement</c>）。</summary>
    /// <param name="json">JSON 字面量。</param>
    /// <returns>节点。</returns>
    private static JsonNode Parsed(string json) => JsonNode.Parse(json)!;

    /// <summary>程序内构造的整数节点（后端是 <c>int</c>）。</summary>
    /// <param name="value">整数值。</param>
    /// <returns>节点。</returns>
    private static JsonNode FromInt(int value) => JsonValue.Create(value);

    /// <summary>程序内构造的长整数节点。</summary>
    /// <param name="value">长整数值。</param>
    /// <returns>节点。</returns>
    private static JsonNode FromLong(long value) => JsonValue.Create(value);

    /// <summary>程序内构造的双精度节点。</summary>
    /// <param name="value">浮点值。</param>
    /// <returns>节点。</returns>
    private static JsonNode FromDouble(double value) => JsonValue.Create(value);

    /// <summary>整数形式的后备类型必须被正确处理（这是踩过的坑）。</summary>
    [Fact]
    public void 整数后备类型应正确处理()
    {
        Assert.True(SettingsValueConverter.ToBool(FromInt(1)));
        Assert.False(SettingsValueConverter.ToBool(FromInt(0)));
        Assert.True(SettingsValueConverter.ToBool(FromInt(2)));
        Assert.True(SettingsValueConverter.ToBool(FromInt(-1)));

        Assert.True(SettingsValueConverter.ToBool(FromLong(1)));
        Assert.False(SettingsValueConverter.ToBool(FromLong(0)));
    }

    /// <summary>浮点后备类型必须被正确处理。</summary>
    [Fact]
    public void 浮点后备类型应正确处理()
    {
        Assert.True(SettingsValueConverter.ToBool(FromDouble(1.0)));
        Assert.False(SettingsValueConverter.ToBool(FromDouble(0.0)));
        Assert.True(SettingsValueConverter.ToBool(FromDouble(0.5)));
    }

    /// <summary>从 JSON 文本解析出的数字同样正确处理。</summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("1.0", true)]
    [InlineData("0.0", false)]
    [InlineData("-1", true)]
    public void 解析出的数字应正确处理(string json, bool expected) =>
        Assert.Equal(expected, SettingsValueConverter.ToBool(Parsed(json)));

    /// <summary>布尔后备类型与字符串写法。</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"1\"", true)]
    [InlineData("\"0\"", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"TRUE\"", true)]
    [InlineData("\"yes\"", true)]
    [InlineData("\"on\"", true)]
    [InlineData("\"no\"", false)]
    [InlineData("\"随便什么\"", false)]
    public void 布尔与字符串写法应正确处理(string json, bool expected) =>
        Assert.Equal(expected, SettingsValueConverter.ToBool(Parsed(json)));

    /// <summary>缺失、<c>null</c>、对象、数组一律回退默认值。</summary>
    [Theory]
    [InlineData("null", true)]
    [InlineData("null", false)]
    [InlineData("{}", true)]
    [InlineData("[]", true)]
    public void 非标量回退默认值(string json, bool fallback) =>
        Assert.Equal(fallback, SettingsValueConverter.ToBool(Parsed(json), fallback));

    /// <summary><see langword="null"/> 节点回退默认值。</summary>
    [Fact]
    public void 空节点回退默认值()
    {
        Assert.True(SettingsValueConverter.ToBool(null, fallback: true));
        Assert.False(SettingsValueConverter.ToBool(null, fallback: false));
    }

    /// <summary>颜色校验：合法 <c>#RRGGBB</c> 通过，其余回退。</summary>
    [Theory]
    [InlineData("#000000", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#abcdef", true)]
    [InlineData("#ABCDEF", true)]
    [InlineData("#FFF", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("000000", false)]
    [InlineData("red", false)]
    [InlineData("", false)]
    public void 颜色校验(string value, bool valid)
    {
        Assert.Equal(valid, SettingsValueConverter.IsValidHexColor(value));
        Assert.Equal(valid ? value : "#FFFFFF", SettingsValueConverter.ValidateHexColor(value, "#FFFFFF"));
    }

    /// <summary>旧中文枚举值迁移：合法 key 优先，其次迁移表，最后默认值。</summary>
    [Theory]
    [InlineData("top", "top")]
    [InlineData("上", "top")]
    [InlineData("下", "bottom")]
    [InlineData("中间", "top")]
    [InlineData("", "top")]
    [InlineData(null, "top")]
    public void 枚举值迁移(string? value, string expected)
    {
        var actual = SettingsValueConverter.MigrateEnumValue(
            value,
            SettingsEnums.LyricPositions,
            SettingsEnums.LegacyLyricPositions,
            SettingsEnums.DefaultLyricPosition);

        Assert.Equal(expected, actual);
    }
}
