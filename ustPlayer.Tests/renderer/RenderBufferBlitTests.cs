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

    /// <summary>
    /// 「渲染 + 拷入位图」的单帧总耗时应在 60fps 预算内。
    /// </summary>
    /// <remarks>
    /// 测的是 1920×1080：这是播放器的目标分辨率（4K 屏按 ADR 0001 的决策降到此值再放大）。
    /// 阈值取整个 16.6ms 预算而非 Spike 0a 的 8ms 判定线——CI 机器可能明显慢于开发机，
    /// 这里要守住的是「能不能实时」，而不是复现开发机的性能数字。
    /// </remarks>
    [AvaloniaFact]
    public void 渲染加上传应在六十帧预算内()
    {
        RequireNativeRenderer();

        const int Width = 1920;
        const int Height = 1080;
        const int Frames = 240;
        const int WarmupFrames = 20;

        using var context = UplRenderContext.Create();
        context.SetConfig(BuildConfig(Width, Height));
        context.SetUstText(BuildUstJson(notes: 500));

        using var bitmap = new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),
            RendererPixelFormat.BitmapFormat,
            RendererPixelFormat.BitmapAlphaFormat);

        // 复用同一份缓冲，避免把每帧分配算进耗时
        var pixels = new byte[RendererPixelFormat.BufferSize(Width, Height)];
        var renderTimes = new List<double>(Frames);
        var blitTimes = new List<double>(Frames);

        for (var i = 0; i < Frames + WarmupFrames; i++)
        {
            var elapsed = i / 60.0;

            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            context.RenderToBuffer(elapsed, pixels, Width, Height);
            var afterRender = System.Diagnostics.Stopwatch.GetTimestamp();

            Blit(bitmap, pixels);
            var afterBlit = System.Diagnostics.Stopwatch.GetTimestamp();

            // 预热帧计入渲染器内部的首次字体加载与缓存构建，不计入统计
            if (i < WarmupFrames)
            {
                continue;
            }

            renderTimes.Add(ToMilliseconds(afterRender - start));
            blitTimes.Add(ToMilliseconds(afterBlit - afterRender));
        }

        var total = renderTimes.Zip(blitTimes, (render, blit) => render + blit).ToArray();
        var meanTotal = total.Average();
        var p95Total = Percentile(total, 95);

        _output.WriteLine($"分辨率 {Width}x{Height}，采样 {Frames} 帧");
        _output.WriteLine($"渲染    mean {renderTimes.Average():F2} ms / p95 {Percentile(renderTimes.ToArray(), 95):F2} ms");
        _output.WriteLine($"上传    mean {blitTimes.Average():F2} ms / p95 {Percentile(blitTimes.ToArray(), 95):F2} ms");
        _output.WriteLine($"合计    mean {meanTotal:F2} ms / p95 {p95Total:F2} ms（预算 {FrameBudgetMs:F2} ms）");

        Assert.True(
            p95Total <= FrameBudgetMs,
            $"「渲染 + 上传」p95 = {p95Total:F2} ms，超出 60fps 预算 {FrameBudgetMs:F2} ms");
    }

    /// <summary>把渲染器输出拷入位图帧缓冲（与宿主播放器的做法一致）。</summary>
    /// <param name="bitmap">目标位图。</param>
    /// <param name="pixels">渲染器输出（RGBA8888 预乘，行距紧凑）。</param>
    /// <remarks>
    /// 先按源缓冲构造 <see cref="WriteableBitmap"/>，因此正常路径下
    /// <c>RowBytes == 宽度 × 4</c>，可整块拷贝；仍显式校验行距，
    /// 以免将来换成非紧凑格式时**静默**画出错位的画面。
    /// </remarks>
    private static unsafe void Blit(WriteableBitmap bitmap, byte[] pixels)
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

    private static string BuildConfig(int width, int height)
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

    private static string BuildUstJson(int notes) => RenderConfig.BuildUstJson(BuildUst(notes));

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
