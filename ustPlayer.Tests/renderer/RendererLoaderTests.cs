using System;
using System.Collections.Generic;
using System.Linq;

using UstPlayer.Interop;

using Xunit;

namespace UstPlayer.Tests.Renderer;

/// <summary>
/// 渲染器加载路径解析的测试（**不依赖原生库**，三平台都会执行）。
/// </summary>
/// <remarks>
/// 依赖原生库的用例在 <see cref="RendererInteropTests"/> 与
/// <see cref="RenderBufferBlitTests"/>，它们标了 <c>RequiresNative</c>；
/// 本类刻意拆开，使「路径解析」这条纯托管逻辑在 macOS / Linux CI 上也被验证。
/// </remarks>
[Collection(NativeRendererCollection.Name)]
public class RendererLoaderTests
{
    /// <summary>候选目录不含重复项（重复会让解析器做无谓的重复磁盘探测）。</summary>
    [Fact]
    public void 候选目录不含重复项()
    {
        IReadOnlyList<string> directories = UplRenderLoader.SearchDirectories();

        var distinct = new HashSet<string>(directories, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(distinct.Count, directories.Count);
    }

    /// <summary>候选目录里每个都必须是真实存在的绝对路径。</summary>
    [Fact]
    public void 候选目录应存在且为绝对路径()
    {
        foreach (var directory in UplRenderLoader.SearchDirectories())
        {
            Assert.True(System.IO.Path.IsPathRooted(directory), $"应为绝对路径：{directory}");
            Assert.True(System.IO.Directory.Exists(directory), $"应真实存在：{directory}");
        }
    }

    /// <summary>平台文件名按当前平台解析（Windows <c>.dll</c>、Unix <c>lib*.so</c> 等）。</summary>
    [Fact]
    public void 平台文件名与当前平台一致()
    {
        var fileName = NativeLibraryResolver.PlatformFileName;

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("ustplayer_renderer.dll", fileName);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("libustplayer_renderer.dylib", fileName);
        }
        else
        {
            Assert.Equal("libustplayer_renderer.so", fileName);
        }
    }

    /// <summary>未找到渲染器时 <c>FindLibraryPath</c> 返回 null 而不是抛异常。</summary>
    [Fact]
    public void 未找到时返回_null()
    {
        var path = UplRenderLoader.FindLibraryPath();

        // 有则必须真实存在；无则为 null——两者都不允许抛异常
        if (path is not null)
        {
            Assert.True(System.IO.File.Exists(path));
        }

        Assert.Equal(path is not null, UplRenderLoader.IsAvailable());
    }
}
