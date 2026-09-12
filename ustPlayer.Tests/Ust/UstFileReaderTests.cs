using System;
using System.IO;
using System.Text;

using UstPlayer.Ust;

using Xunit;

namespace UstPlayer.Tests.Ust;

/// <summary>
/// UST 解析器测试 — 重点覆盖 1.1.x 踩坑后加固的三处行为
/// （UTF-8 BOM、速度边界校验、OpenUtau 音高曲线），以及编码处理。
/// </summary>
public class UstFileReaderTests
{
    /// <summary>启用 Shift-JIS / GBK 等代码页（.NET 默认只带 Unicode 家族）。</summary>
    static UstFileReaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ===================== 基础解析 =====================

    /// <summary>版本、速度、轨道数与音符应被正确解析。</summary>
    [Fact]
    public void 解析基础字段与音符()
    {
        var ust = UstFileReader.ParseText(SampleUst);

        Assert.Equal("UST Version1.2", ust.Version);
        Assert.Equal(120.0, ust.Tempo);
        Assert.Equal(2, ust.Tracks);
        Assert.Equal(2, ust.Notes.Count);

        Assert.Equal("0000", ust.Notes[0].Index);
        Assert.Equal(480, ust.Notes[0].Length);
        Assert.Equal("do", ust.Notes[0].Lyric);
        Assert.Equal(60, ust.Notes[0].NoteNumber);

        Assert.Equal("re", ust.Notes[1].Lyric);
        Assert.Equal(62, ust.Notes[1].NoteNumber);
    }

    /// <summary>音素字段保留。</summary>
    [Fact]
    public void 解析音素字段()
    {
        var text = "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPhoneme=a\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Equal("a", ust.Notes[0].Phoneme);
    }

    /// <summary>传统 <c>PitchBend</c> 数值列表按原样解析。</summary>
    [Fact]
    public void 解析传统音高曲线()
    {
        var text = "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPitchBend=0,64,128,0,-64\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Equal([0, 64, 128, 0, -64], ust.Notes[0].PitchBend);
    }

    /// <summary>传统音高曲线中的非法项被跳过，而不是整段失败。</summary>
    [Fact]
    public void 音高曲线非法项被跳过()
    {
        var text = "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPitchBend=0,abc,,64\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Equal([0, 64], ust.Notes[0].PitchBend);
    }

    // ===================== 踩坑点 1：UTF-8 BOM =====================

    /// <summary>
    /// 带 BOM 的文件必须能正确解析出 <c>[#VERSION]</c>。
    /// 1.1.x 的 bug：<c>\uFEFF</c> 残留首行使该段匹配失败，版本号静默丢失。
    /// </summary>
    [Fact]
    public void 带_BOM_时版本号不丢失()
    {
        var ust = UstFileReader.ParseText("\uFEFF" + SampleUst);

        Assert.Equal("UST Version1.2", ust.Version);
        Assert.Equal(2, ust.Notes.Count);
    }

    /// <summary>
    /// 首个段就是音符段且文件带 BOM 时，该音符不得被静默丢弃。
    /// 这正是 1.1.x 修复的另一种表现（首行即音符段时整段丢失且无提示）。
    /// </summary>
    [Fact]
    public void 带_BOM_时首个音符段不丢失()
    {
        var text = "\uFEFF[#0000]\nLength=480\nLyric=do\nNoteNum=60\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Single(ust.Notes);
        Assert.Equal("do", ust.Notes[0].Lyric);
    }

    // ===================== 踩坑点 2：速度边界校验 =====================

    /// <summary>
    /// 非法速度统一回退 120 BPM。0 会让时间轴永远停在第 0 tick；NaN/Inf 会让比较行为怪异。
    /// </summary>
    /// <param name="tempoText">文件里写的速度值。</param>
    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void 非法速度回退默认值(string tempoText)
    {
        var text = $"[#SETTING]\nTempo={tempoText}\n[#0000]\nLength=480\nLyric=a\nNoteNum=60\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Equal(120.0, ust.Tempo);
    }

    /// <summary>合法速度按原值保留（含小数）。</summary>
    [Theory]
    [InlineData("90", 90.0)]
    [InlineData("120.000", 120.0)]
    [InlineData("95.5", 95.5)]
    public void 合法速度按原值保留(string tempoText, double expected)
    {
        var text = $"[#SETTING]\nTempo={tempoText}\n[#0000]\nLength=480\nLyric=a\nNoteNum=60\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Equal(expected, ust.Tempo);
    }

    /// <summary>速度值无法解析时保持默认 120（不报错）。</summary>
    [Fact]
    public void 速度非数值时保持默认()
    {
        var text = "[#SETTING]\nTempo=abc\n[#0000]\nLength=480\nLyric=a\nNoteNum=60\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Equal(120.0, ust.Tempo);
    }

    // ===================== 踩坑点 3：OpenUtau 音高曲线 =====================

    /// <summary><c>PBS</c>（起点）+ <c>PBY</c>（后续点）合成为音高曲线。</summary>
    [Fact]
    public void 合成_OpenUtau_音高曲线()
    {
        var text =
            "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPBS=10,20\nPBY=0,50,100,-50\n";

        var ust = UstFileReader.ParseText(text);

        // 起点取 PBS 的第一个值，随后接 PBY 的全部点
        Assert.Equal([10, 0, 50, 100, -50], ust.Notes[0].PitchBend);
    }

    /// <summary>缺少 <c>PBS</c> 时起点按 0 处理。</summary>
    [Fact]
    public void 缺_PBS_时起点为零()
    {
        var text = "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPBY=30,-30\n";

        var ust = UstFileReader.ParseText(text);

        Assert.Equal([0, 30, -30], ust.Notes[0].PitchBend);
    }

    /// <summary>OpenUtau 的小数音分值四舍五入为整数。</summary>
    [Fact]
    public void OpenUtau_小数音分值四舍五入()
    {
        var text = "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPBS=10.4\nPBY=0.6,-0.6,2.5\n";

        var ust = UstFileReader.ParseText(text);

        // 10.4 → 10；0.6 → 1；-0.6 → -1；2.5 → 3（AwayFromZero，与 Python round() 对齐）
        Assert.Equal([10, 1, -1, 3], ust.Notes[0].PitchBend);
    }

    /// <summary>同时存在传统 <c>PitchBend</c> 与 OpenUtau 字段时，以传统值为准。</summary>
    [Fact]
    public void 传统音高曲线优先于_OpenUtau_字段()
    {
        var text =
            "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPitchBend=1,2,3\nPBS=10\nPBY=40\n";

        var ust = UstFileReader.ParseText(text);

        Assert.Equal([1, 2, 3], ust.Notes[0].PitchBend);
    }

    /// <summary><c>PBW</c> / <c>PBM</c> 仅被识别，不参与合成（当前按均匀点近似）。</summary>
    [Fact]
    public void PBW与PBM不参与合成()
    {
        var text =
            "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPBS=5\nPBW=10,20\nPBM=0,1\nPBY=25\n";

        var ust = UstFileReader.ParseText(text);

        Assert.Equal([5, 25], ust.Notes[0].PitchBend);
    }

    /// <summary>OpenUtau 字段不跨音符泄漏。</summary>
    [Fact]
    public void OpenUtau_字段不跨音符泄漏()
    {
        var text =
            "[#0000]\nLength=480\nLyric=a\nNoteNum=60\nPBS=10\nPBY=40\n" +
            "[#0001]\nLength=480\nLyric=b\nNoteNum=62\n";

        var ust = UstFileReader.ParseText(text);

        Assert.Equal([10, 40], ust.Notes[0].PitchBend);
        Assert.Empty(ust.Notes[1].PitchBend);
    }

    // ===================== 编码 =====================

    /// <summary>Shift-JIS 文件按默认编码（Shift-JIS）解析出日文歌词。</summary>
    [Fact]
    public void 按_Shift_JIS_读取日文歌词()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ust-sjis-{Guid.NewGuid():N}.ust");
        var content = "[#0000]\nLength=480\nLyric=あ\nNoteNum=60\n";

        try
        {
            File.WriteAllText(path, content, Encoding.GetEncoding("Shift-JIS"));

            var ust = new UstFileReader().Parse(path);

            Assert.Equal("あ", ust.Notes[0].Lyric);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>显式传入 UTF-8 时读取中文歌词。</summary>
    [Fact]
    public void 按_UTF8_读取中文歌词()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ust-utf8-{Guid.NewGuid():N}.ust");
        var content = "[#0000]\nLength=480\nLyric=测\nNoteNum=60\n";

        try
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));

            var ust = new UstFileReader().Parse(path, "UTF-8");

            Assert.Equal("测", ust.Notes[0].Lyric);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>文件不存在时抛出 <see cref="FileNotFoundException"/>。</summary>
    [Fact]
    public void 文件不存在时抛出异常()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.ust");

        Assert.Throws<FileNotFoundException>(() => new UstFileReader().Parse(missing));
    }

    // ===================== 健壮性 =====================

    /// <summary>空内容不报错，返回默认值。</summary>
    [Fact]
    public void 空内容返回默认值()
    {
        var ust = UstFileReader.ParseText(string.Empty);

        Assert.Empty(ust.Version);
        Assert.Equal(120.0, ust.Tempo);
        Assert.Equal(1, ust.Tracks);
        Assert.Empty(ust.Notes);
    }

    /// <summary>CRLF 与 LF 混用都能解析。</summary>
    [Fact]
    public void 兼容_CRLF_与_LF()
    {
        var ust = UstFileReader.ParseText("[#0000]\r\nLength=480\r\nLyric=a\r\nNoteNum=60\n");

        Assert.Single(ust.Notes);
        Assert.Equal("a", ust.Notes[0].Lyric);
    }

    /// <summary>非数字的段头（如 <c>[#PREV]</c>）不被当作音符段。</summary>
    [Fact]
    public void 非数字段头不被当作音符()
    {
        var text = "[#PREV]\nLength=480\nLyric=x\nNoteNum=60\n";
        var ust = UstFileReader.ParseText(text);

        Assert.Empty(ust.Notes);
    }

    private const string SampleUst =
        "[#VERSION]\n" +
        "UST Version1.2\n" +
        "[#SETTING]\n" +
        "Tempo=120.000\n" +
        "Tracks=2\n" +
        "[#0000]\n" +
        "Length=480\n" +
        "Lyric=do\n" +
        "NoteNum=60\n" +
        "[#0001]\n" +
        "Length=480\n" +
        "Lyric=re\n" +
        "NoteNum=62\n";
}
