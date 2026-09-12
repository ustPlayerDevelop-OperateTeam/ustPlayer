using Avalonia.Media;

using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 颜色文本（存储层的 <c>#RRGGBB</c>）与 Avalonia 颜色之间转换的测试。
/// </summary>
public class HexColorTextTests
{
    /// <summary>合法的 #RRGGBB 能解析出正确分量（大小写均可）。</summary>
    /// <param name="text">文本。</param>
    /// <param name="red">红。</param>
    /// <param name="green">绿。</param>
    /// <param name="blue">蓝。</param>
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#123456", 0x12, 0x34, 0x56)]
    [InlineData("#abcdef", 0xAB, 0xCD, 0xEF)]
    [InlineData("  #6c6c6c  ", 0x6C, 0x6C, 0x6C)]
    public void 解析合法颜色(string text, byte red, byte green, byte blue)
    {
        Assert.True(HexColorText.TryParse(text, out var color));
        Assert.Equal(red, color.R);
        Assert.Equal(green, color.G);
        Assert.Equal(blue, color.B);
    }

    /// <summary>
    /// 只认 #RRGGBB：三位简写、带 alpha、颜色名都不接受——
    /// 它们写回设置层同样会被判非法并回退默认值，不如一开始就拒绝。
    /// </summary>
    /// <param name="text">文本。</param>
    [Theory]
    [InlineData("#FFF")]
    [InlineData("#AARRGGBB")]
    [InlineData("#80FFFFFF")]
    [InlineData("FFFFFF")]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void 拒绝非法颜色(string? text)
    {
        Assert.False(HexColorText.TryParse(text, out _));
    }

    /// <summary>格式化结果始终是大写 #RRGGBB，可直接写回设置层。</summary>
    [Fact]
    public void 格式化为大写十六进制()
    {
        Assert.Equal("#123456", HexColorText.Format(Color.FromRgb(0x12, 0x34, 0x56)));
        Assert.Equal("#000000", HexColorText.Format(Colors.Black));
        Assert.Equal("#FFFFFF", HexColorText.Format(Colors.White));
    }

    /// <summary>解析后再格式化应还原原文（去掉空白并统一大小写）。</summary>
    /// <param name="text">文本。</param>
    /// <param name="expected">期望的规范化结果。</param>
    [Theory]
    [InlineData("#123456", "#123456")]
    [InlineData("  #abcdef ", "#ABCDEF")]
    [InlineData("#000000", "#000000")]
    public void 往返一致(string text, string expected)
    {
        Assert.True(HexColorText.TryParse(text, out var color));
        Assert.Equal(expected, HexColorText.Format(color));
    }

    /// <summary>格式化结果必须能通过设置层的颜色校验（两道防线不能互相矛盾）。</summary>
    [Fact]
    public void 格式化结果通过设置层校验()
    {
        var formatted = HexColorText.Format(Color.FromRgb(0x0A, 0xB0, 0xC0));

        Assert.True(UstPlayer.Settings.SettingsValueConverter.IsValidHexColor(formatted));
    }
}
