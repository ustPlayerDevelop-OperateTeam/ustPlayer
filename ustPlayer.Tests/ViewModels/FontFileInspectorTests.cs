using System;
using System.IO;

using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 字体文件检查（扩展名判定 + sfnt <c>name</c> 表解析）的测试。
/// </summary>
/// <remarks>
/// 全部使用**合成的**字体文件，不依赖本机安装的字体，因此跨平台结果一致。
/// </remarks>
public class FontFileInspectorTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public FontFileInspectorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"fontinspector-{Guid.NewGuid():N}");
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
            // 清理失败无关紧要
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>只接受 .ttf / .otf（大小写不敏感），其余一律不是可导入的字体。</summary>
    /// <param name="path">路径。</param>
    /// <param name="expected">期望结果。</param>
    [Theory]
    [InlineData(@"C:\fonts\a.ttf", true)]
    [InlineData(@"C:\fonts\a.otf", true)]
    [InlineData(@"C:\fonts\a.TTF", true)]
    [InlineData(@"C:\fonts\a.OTF", true)]
    [InlineData(@"C:\fonts\a.ttc", false)]
    [InlineData(@"C:\fonts\a.woff", false)]
    [InlineData(@"C:\fonts\a.ttf.txt", false)]
    [InlineData(@"C:\fonts\a", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void 只接受_ttf_与_otf(string? path, bool expected)
    {
        Assert.Equal(expected, FontFileInspector.IsSupportedFontFile(path));
    }

    /// <summary>路径不存在时不能作为字体导入（返回 null 让界面报错）。</summary>
    [Fact]
    public void 不存在的文件不可导入()
    {
        var path = Path.Combine(_tempDirectory, "missing.ttf");

        Assert.Null(FontFileInspector.ResolveFamilyName(path));
        Assert.Null(FontFileInspector.TryReadFamilyName(path));
    }

    /// <summary>扩展名不对的文件即使存在也不可导入。</summary>
    [Fact]
    public void 扩展名不对不可导入()
    {
        var path = Path.Combine(_tempDirectory, "not-a-font.txt");
        File.WriteAllText(path, "hello");

        Assert.Null(FontFileInspector.ResolveFamilyName(path));
    }

    /// <summary>从 <c>name</c> 表读出族名（nameID = 1，Windows / UTF-16BE 记录）。</summary>
    [Fact]
    public void 可读出字体族名()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "a.ttf", (1, "测试字体"));

        Assert.Equal("测试字体", FontFileInspector.TryReadFamilyName(path));
        Assert.Equal("测试字体", FontFileInspector.ResolveFamilyName(path));
    }

    /// <summary>nameID = 16（Typographic Family）优先于 nameID = 1。</summary>
    [Fact]
    public void 排版族名优先()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "b.otf", (1, "旧族名"), (16, "排版族名"));

        Assert.Equal("排版族名", FontFileInspector.ResolveFamilyName(path));
    }

    /// <summary>只有 nameID = 1 时用它（常见的老字体）。</summary>
    [Fact]
    public void 无排版族名时回退家族名()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "c.ttf", (4, "完整名"), (6, "PostScript名"), (1, "家族名"));

        Assert.Equal("家族名", FontFileInspector.ResolveFamilyName(path));
    }

    /// <summary>
    /// 结构不认识时不算导入失败：回退文件名（去掉扩展名），文件仍可被记录。
    /// </summary>
    [Fact]
    public void 结构不可识别时回退文件名()
    {
        var path = Path.Combine(_tempDirectory, "MyFont.ttf");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        Assert.Null(FontFileInspector.TryReadFamilyName(path));
        Assert.Equal("MyFont", FontFileInspector.ResolveFamilyName(path));
    }

    /// <summary>偏移越界的损坏文件不能让页面崩掉。</summary>
    [Fact]
    public void 越界偏移不抛异常()
    {
        var bytes = SyntheticFontFile.Create((1, "族名"));

        // 把 name 表的 offset 改成一个越界值（记录位于偏移 20 处）
        bytes[20] = 0x7F;
        bytes[21] = 0xFF;
        bytes[22] = 0xFF;
        bytes[23] = 0xFF;

        var path = Path.Combine(_tempDirectory, "broken.ttf");
        File.WriteAllBytes(path, bytes);

        Assert.Null(FontFileInspector.TryReadFamilyName(path));
        Assert.Equal("broken", FontFileInspector.ResolveFamilyName(path));
    }

    /// <summary>文件为空时不抛异常。</summary>
    [Fact]
    public void 空文件不抛异常()
    {
        var path = Path.Combine(_tempDirectory, "empty.ttf");
        File.WriteAllBytes(path, []);

        Assert.Null(FontFileInspector.TryReadFamilyName(path));
        Assert.Equal("empty", FontFileInspector.ResolveFamilyName(path));
    }
}
