using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UstPlayer.Timing;

using Xunit;

namespace UstPlayer.Tests.Timing;

/// <summary>
/// LRC 解析测试。
/// </summary>
/// <remarks>
/// 重点覆盖 1.1.x 逐条踩过的写法：一行多时间戳、无毫秒、1/2 位毫秒（位数决定倍率，
/// 不是小数位——搞错会让整句歌词提前约半秒）。
/// </remarks>
public class LrcParserTests
{
    // ===================== 基础 =====================

    /// <summary>基本的三行歌词。</summary>
    [Fact]
    public void 解析基本歌词()
    {
        var lines = LrcParser.Parse("[00:01.00]第一行\n[00:05.50]第二行\n[01:02.25]第三行");

        Assert.Equal(3, lines.Count);
        Assert.Equal(1.0, lines[0].TimestampSeconds, precision: 6);
        Assert.Equal("第一行", lines[0].Text);
        Assert.Equal(5.5, lines[1].TimestampSeconds, precision: 6);
        Assert.Equal(62.25, lines[2].TimestampSeconds, precision: 6);
    }

    /// <summary>结果按时间戳升序排列，即使文件里顺序错乱。</summary>
    [Fact]
    public void 结果按时间升序排列()
    {
        var lines = LrcParser.Parse("[00:10.00]c\n[00:01.00]a\n[00:05.00]b");

        Assert.Equal(["a", "b", "c"], lines.Select(line => line.Text));
    }

    /// <summary>无时间戳的行（元信息）与空歌词行被跳过。</summary>
    [Fact]
    public void 跳过元信息与空歌词()
    {
        var lines = LrcParser.Parse(
            "[ar:歌手]\n[ti:标题]\n[00:01.00]\n[00:02.00]有词\n不是时间戳");

        Assert.Single(lines);
        Assert.Equal("有词", lines[0].Text);
    }

    /// <summary>空内容返回空列表。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("没有时间戳")]
    public void 无歌词时返回空(string content) =>
        Assert.Empty(LrcParser.Parse(content));

    // ===================== 一行多时间戳 =====================

    /// <summary>一行多时间戳：同一句词应生成多条记录。</summary>
    [Fact]
    public void 一行多时间戳生成多条记录()
    {
        var lines = LrcParser.Parse("[00:05.00][00:10.00]重复的句子");

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal("重复的句子", line.Text));
        Assert.Equal(5.0, lines[0].TimestampSeconds, precision: 6);
        Assert.Equal(10.0, lines[1].TimestampSeconds, precision: 6);
    }

    /// <summary>一行三个时间戳同样处理。</summary>
    [Fact]
    public void 一行三个时间戳()
    {
        var lines = LrcParser.Parse("[00:01.00][00:02.00][00:03.00]词");

        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal("词", line.Text));
    }

    // ===================== 毫秒写法 =====================

    /// <summary>
    /// 毫秒位数决定倍率：1 位 ×100、2 位 ×10、3 位原值。
    /// 这是最容易搞错的地方——把 <c>[00:05.5]</c> 当成 5 毫秒会让歌词提前约半秒。
    /// </summary>
    [Theory]
    [InlineData("[00:05.5]词", 5.5)]
    [InlineData("[00:05.50]词", 5.5)]
    [InlineData("[00:05.500]词", 5.5)]
    [InlineData("[00:05.05]词", 5.05)]
    [InlineData("[00:05.005]词", 5.005)]
    [InlineData("[00:05]词", 5.0)]
    public void 毫秒写法位数决定倍率(string content, double expected)
    {
        var lines = LrcParser.Parse(content);

        Assert.Single(lines);
        Assert.Equal(expected, lines[0].TimestampSeconds, precision: 6);
    }

    /// <summary>分钟与秒可以是一位或两位数字。</summary>
    [Theory]
    [InlineData("[0:5]词", 5.0)]
    [InlineData("[00:05]词", 5.0)]
    [InlineData("[1:00]词", 60.0)]
    [InlineData("[99:59]词", 5999.0)]
    public void 分秒位数可变(string content, double expected)
    {
        var lines = LrcParser.Parse(content);

        Assert.Single(lines);
        Assert.Equal(expected, lines[0].TimestampSeconds, precision: 6);
    }

    // ===================== 索引推进 =====================

    /// <summary>按时间推进索引。</summary>
    [Fact]
    public void 按时间推进索引()
    {
        var lines = LrcParser.Parse("[00:01.00]a\n[00:02.00]b\n[00:03.00]c");

        Assert.Equal(-1, LrcParser.AdvanceIndex(lines, -1, 0.5));
        Assert.Equal(0, LrcParser.AdvanceIndex(lines, -1, 1.0));
        Assert.Equal(0, LrcParser.AdvanceIndex(lines, 0, 1.5));
        Assert.Equal(1, LrcParser.AdvanceIndex(lines, 0, 2.0));
        Assert.Equal(2, LrcParser.AdvanceIndex(lines, 1, 99.0));
    }

    /// <summary>时间回退（seek）时从头重扫，而不是沿用旧索引。</summary>
    [Fact]
    public void 时间回退时重新定位()
    {
        var lines = LrcParser.Parse("[00:01.00]a\n[00:02.00]b\n[00:03.00]c");

        // 先推进到第 3 行
        var index = LrcParser.AdvanceIndex(lines, -1, 3.5);
        Assert.Equal(2, index);

        // 时间回退到 1.5 秒 → 应回到第 1 行
        Assert.Equal(0, LrcParser.AdvanceIndex(lines, index, 1.5));

        // 再回退到开头之前 → 无歌词
        Assert.Equal(-1, LrcParser.AdvanceIndex(lines, 0, 0.5));
    }

    /// <summary>空列表时索引恒为 -1。</summary>
    [Fact]
    public void 空列表索引为负一() =>
        Assert.Equal(-1, LrcParser.AdvanceIndex([], -1, 10.0));

    // ===================== 文件读取 =====================

    /// <summary>从 UTF-8 文件读取。</summary>
    [Fact]
    public void 从_utf8_文件读取()
    {
        var path = WriteTempLrc("[00:01.00]中文歌词", new UTF8Encoding(false));

        try
        {
            var lines = LrcParser.ParseFile(path);

            Assert.Single(lines);
            Assert.Equal("中文歌词", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>带 BOM 的 UTF-8 文件不应让首行歌词带上 BOM。</summary>
    [Fact]
    public void 带_BOM_的文件首行正常()
    {
        var path = WriteTempLrc("[00:01.00]首行", new UTF8Encoding(true));

        try
        {
            var lines = LrcParser.ParseFile(path);

            Assert.Single(lines);
            Assert.Equal("首行", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>GBK 文件应能通过编码回退读出来。</summary>
    [Fact]
    public void 从_gbk_文件读取()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = WriteTempLrc("[00:01.00]简体歌词", Encoding.GetEncoding("GBK"));

        try
        {
            var lines = LrcParser.ParseFile(path);

            Assert.Single(lines);
            Assert.Equal("简体歌词", lines[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>文件不存在时返回空列表，不抛异常。</summary>
    [Fact]
    public void 文件不存在返回空() =>
        Assert.Empty(LrcParser.ParseFile(Path.Combine(Path.GetTempPath(), "不存在.lrc")));

    /// <summary>空路径返回空列表。</summary>
    [Fact]
    public void 空路径返回空() =>
        Assert.Empty(LrcParser.ParseFile(string.Empty));

    // ===================== 辅助 =====================

    private static string WriteTempLrc(string content, Encoding encoding)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lrc-{Guid.NewGuid():N}.lrc");
        File.WriteAllText(path, content, encoding);
        return path;
    }
}
