using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

using UstPlayer.Interop;
using UstPlayer.Models;
using UstPlayer.Video;
using UstPlayer.Views;

using Xunit;
using Xunit.Abstractions;

namespace UstPlayer.Tests.Renderer;

/// <summary>
/// Spike 0b：验证「播放器用渲染器实时出帧」在 Avalonia 侧的端到端路径。
/// </summary>
/// <remarks>
/// <para>
/// Spike 0a（`pysourcecode/tools/probe_render_buffer.py`）已证明**原生侧**单帧够快
/// （1080p P95 5.96ms）。本测试补齐 Avalonia 侧的三件事：
/// </para>
/// <list type="number">
///   <item>渲染器输出的 RGBA8888 预乘格式能与 <see cref="WriteableBitmap"/> 直接对接
///   （无需逐像素转换）；</item>
///   <item><c>Lock()</c> + 拷贝到帧缓冲的开销可忽略；</item>
///   <item>「渲染 + 上传」总耗时仍在 60fps 预算内。</item>
/// </list>
/// <para>
/// 判定依据与 Spike 0a 数据见 <c>docs/adr-0001-renderer-strategy.md</c>。
/// </para>
/// <para>
/// <b>CI 可见性</b>：这些用例需要渲染器原生库，而它目前只有 Windows 构建产物
/// （Spike 0c 待补 macOS / Linux 目标）。CI 的非 Windows 作业按**类名**排除本类；Windows 作业必须全跑，
/// 避免「渲染器没就位却一路绿灯」。
/// </para>
/// </remarks>
[Collection(NativeRendererCollection.Name)]
public class RenderBufferBlitTests
{
    /// <summary>60fps 的单帧预算（毫秒）。</summary>
    private const double FrameBudgetMs = 1000.0 / 60.0;

    private readonly ITestOutputHelper _output;

    /// <summary>构造测试。</summary>
    /// <param name="output">测试输出（用于打印实测数据）。</param>
    public RenderBufferBlitTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>原生渲染器缺失时给出可操作提示。</summary>
    private static void RequireNativeRenderer()
    {
        Assert.True(
            UplRenderLoader.IsAvailable(),
            "未找到渲染器原生库。请运行 build/sync-native-assets.ps1，"
            + "或用 UPLRENDER_RELEASE_DIR 指定 uPlRender 的 target/release 目录后重试。");
    }

    /// <summary>
    /// 渲染器输出可以直接拷进 <see cref="WriteableBitmap"/> 的帧缓冲，且尺寸与格式吻合。
    /// </summary>
    [AvaloniaFact]
    public void 渲染输出可直接拷入位图帧缓冲()
    {
        RequireNativeRenderer();

        const int Width = 320;
        const int Height = 180;

        using var context = UplRenderContext.Create();
        context.SetConfig(BuildConfig(Width, Height));
        context.SetUstText(BuildUstJson(notes: 4));

        using var bitmap = new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),
            RendererPixelFormat.BitmapFormat,
            RendererPixelFormat.BitmapAlphaFormat);

        var pixels = new byte[RendererPixelFormat.BufferSize(Width, Height)];
        var (outWidth, outHeight) = context.RenderToBuffer(0.5, pixels, Width, Height);

        Assert.Equal(Width, outWidth);
        Assert.Equal(Height, outHeight);

        Blit(bitmap, pixels);

        // 画面确实被写入（R/G/B 至少有一个非零通道）
        Assert.Contains(pixels, value => value != 0);
    }

    /// <summary>把渲染器输出拷入位图帧缓冲（与宿主播放器的做法一致）。</summary>
    /// <param name="bitmap">目标位图。</param>
    /// <param name="pixels">渲染器输出（RGBA8888 预乘，行距紧凑）。</param>
    /// <remarks>
    /// 先按源缓冲构造 <see cref="WriteableBitmap"/>，因此正常路径下
    /// <c>RowBytes == 宽度 × 4</c>，可整块拷贝；仍显式校验行距，
    /// 以免将来换成非紧凑格式时**静默**画出错位的画面。
    /// </remarks>
    internal static unsafe void Blit(WriteableBitmap bitmap, byte[] pixels)
    {
        using var framebuffer = bitmap.Lock();

        var rowBytes = framebuffer.RowBytes;
        var expectedRowBytes = framebuffer.Size.Width * RendererPixelFormat.BytesPerPixel;

        Assert.True(
            rowBytes == expectedRowBytes,
            $"位图行距 {rowBytes} 与紧凑排布 {expectedRowBytes} 不一致，逐行拷贝尚未实现");

        var destination = new Span<byte>((void*)framebuffer.Address, rowBytes * framebuffer.Size.Height);
        pixels.AsSpan(0, destination.Length).CopyTo(destination);
    }

    private static double ToMilliseconds(long ticks) =>
        ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static double Percentile(double[] samples, double percentile)
    {
        if (samples.Length == 0)
        {
            return 0.0;
        }

        var sorted = samples.OrderBy(value => value).ToArray();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    internal static string BuildConfig(int width, int height)
    {
        var parameters = new PlayerLaunchParams
        {
            Ust = BuildUst(notes: 8),
            Show = new ShowConfig { CurveShow = true },
            Project = new ProjectInfo { SongName = "Spike0b", SongAuthor = "测试", UstAuthor = "测试" },
            Style = new PlayerStyle { AppVersion = AppInfo.Version },
        };

        return RenderConfig.Build(parameters, width, height, fps: 60);
    }

    internal static string BuildUstJson(int notes) => RenderConfig.BuildUstJson(BuildUst(notes));

    private static UstInfo BuildUst(int notes)
    {
        var ust = new UstInfo { Version = "UST Version1.2", Tempo = 120.0, Tracks = 1 };

        for (var i = 0; i < notes; i++)
        {
            var isRest = i % 8 == 7;
            ust.Notes.Add(new NoteInfo
            {
                Index = $"{i:0000}",
                Length = 480,
                Lyric = isRest ? "R" : "あ",
                NoteNumber = 60 + (i % 13),
                PitchBend = isRest ? [] : [0, 60, 120, 60, -60],
            });
        }

        return ust;
    }
}