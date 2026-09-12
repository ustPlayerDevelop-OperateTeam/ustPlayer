using System;
using System.IO;

using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// <see cref="FontFileInspector"/> 的健壮性测试：**任意字节都不得抛异常**。
/// </summary>
/// <remarks>
/// <para>
/// 字体族名解析要手工读 sfnt 二进制表（<c>name</c> 表），这类代码最容易在
/// 长度/偏移上越界。既有用例已覆盖「空文件」「越界偏移」等具体情形，
/// 本类用确定性伪随机字节做一遍更宽的扫描——用户导入的任何文件（甚至是改名的
/// 图片）都会走进这段代码，它必须只返回 <see langword="null"/> 或文件名，绝不抛异常。
/// </para>
/// <para>
/// 用固定种子：失败可复现，且不引入不稳定性。
/// </para>
/// </remarks>
public class FontFileInspectorFuzzTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public FontFileInspectorFuzzTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"fontfuzz-{Guid.NewGuid():N}");
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

    /// <summary>随机字节不得让解析抛异常。</summary>
    [Fact]
    public void 随机字节不抛异常()
    {
        var random = new Random(20260912);

        for (var iteration = 0; iteration < 200; iteration++)
        {
            var length = random.Next(0, 4096);
            var bytes = new byte[length];
            random.NextBytes(bytes);

            // 一半的样本伪装成合法 sfnt 头，好让解析走得更深（否则会被版本号直接拒绝）
            if (length >= 12 && iteration % 2 == 0)
            {
                bytes[0] = 0x00;
                bytes[1] = 0x01;
                bytes[2] = 0x00;
                bytes[3] = 0x00;
            }

            var path = Path.Combine(_tempDirectory, $"fuzz-{iteration}.ttf");
            File.WriteAllBytes(path, bytes);

            var exception = Record.Exception(() => FontFileInspector.TryReadFamilyName(path));

            Assert.True(exception is null, $"第 {iteration} 个样本（{length} 字节）抛出了 {exception}");
        }
    }

    /// <summary>把合法字体文件截断成前缀也不得抛异常。</summary>
    [Fact]
    public void 截断文件不抛异常()
    {
        var random = new Random(20260913);
        var full = new byte[2048];
        random.NextBytes(full);
        full[0] = 0x00;
        full[1] = 0x01;
        full[2] = 0x00;
        full[3] = 0x00;

        for (var length = 0; length < full.Length; length += 37)
        {
            var path = Path.Combine(_tempDirectory, $"trunc-{length}.ttf");
            File.WriteAllBytes(path, full[..length]);

            var exception = Record.Exception(() => FontFileInspector.TryReadFamilyName(path));

            Assert.True(exception is null, $"截断到 {length} 字节时抛出了 {exception}");
        }
    }
}
