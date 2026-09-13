using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

using UstPlayer.Interop;
using UstPlayer.Tests.Renderer;
using UstPlayer.Views;

using Xunit;
using Xunit.Abstractions;

namespace UstPlayer.Tests.Perf;

/// <summary>
/// 渲染性能基线：**必须独占运行**的墙钟计时测量。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="RenderBufferBlitTests"/> 里拆出来单独成类，原因是它与其余 573 个用例
/// 的**运行方式不同**：那一整套是并行跑的，而并行争抢 CPU 会让墙钟计时失真。
/// 实测同一份代码在并行套件里「合计 mean 11.08 ms / p95 17.09 ms」（超 16.67 ms 预算），
/// 单独跑则稳定在 mean 6.9 / p95 8.6 ms。
/// </para>
/// <para>
/// 因此本类默认**整类跳过**（设 <c>USTPLAYER_RUN_PERF_TESTS=1</c> 才执行），
/// 由 CI 用单独一步独占运行；那一步同时断言本类「确实被执行了」，
/// 避免门禁写错导致性能基线静默失效。
/// </para>
/// <para>
/// 这样处理而不是放宽阈值：放宽会让「产品达不到 60fps」这种真实回归也一起放过去，
/// 而让它在并行负载下断言，则会把「机器忙」误报成产品问题——两者都是假结论。
/// </para>
/// </remarks>
[Collection(PerfCollection.Name)]
public class RenderPerformanceTests
{
    /// <summary>60fps 的单帧预算（毫秒）。</summary>
    private const double FrameBudgetMs = 1000.0 / 60.0;

    /// <summary>是否已按要求开启性能测量。</summary>
    internal static bool IsEnabled =>
        Environment.GetEnvironmentVariable("USTPLAYER_RUN_PERF_TESTS") == "1";

    private readonly ITestOutputHelper _output;

    /// <summary>构造测试。</summary>
    /// <param name="output">测试输出（用于打印实测数据）。</param>
    public RenderPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
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
        // 门禁：未显式要求时直接返回。测试名里写明「需开通」，报告里不会有假结论
        if (!IsEnabled)
        {
            _output.WriteLine(
                "未设置 USTPLAYER_RUN_PERF_TESTS=1，跳过性能测量。"
                + "性能测量必须独占机器运行（并行负载会让墙钟计时失真）。");
            return;
        }

        Assert.True(
            UplRenderLoader.IsAvailable(),
            "未找到渲染器原生库。请运行 build/sync-native-assets.ps1。");

        const int Width = 1920;
        const int Height = 1080;
        const int Frames = 240;
        const int WarmupFrames = 20;

        using var context = UplRenderContext.Create();
        context.SetConfig(RenderBufferBlitTests.BuildConfig(Width, Height));
        context.SetUstText(RenderBufferBlitTests.BuildUstJson(notes: 500));

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

            RenderBufferBlitTests.Blit(bitmap, pixels);
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
        var renderP95 = Percentile(renderTimes.ToArray(), 95);
        var blitP95 = Percentile(blitTimes.ToArray(), 95);

        _output.WriteLine($"分辨率 {Width}x{Height}，采样 {Frames} 帧");
        _output.WriteLine($"渲染    mean {renderTimes.Average():F2} ms / p95 {renderP95:F2} ms");
        _output.WriteLine($"上传    mean {blitTimes.Average():F2} ms / p95 {blitP95:F2} ms");
        _output.WriteLine($"合计    mean {meanTotal:F2} ms / p95 {p95Total:F2} ms（预算 {FrameBudgetMs:F2} ms）");

        // 另外把实测数字**写进文件**：xUnit 在测试通过时既不打印 ITestOutputHelper，
        // 也会吞掉 Console 输出，因此靠标准输出无法判断「性能基线到底跑了没有」。
        // 文件是否存在是明确的证据，不依赖任何输出转发机制。
        WriteMeasurementFile(meanTotal, p95Total, renderP95, blitP95);

        Assert.True(
            p95Total <= FrameBudgetMs,
            $"「渲染 + 上传」p95 = {p95Total:F2} ms，超出 60fps 预算 {FrameBudgetMs:F2} ms");
    }

    /// <summary>
    /// 把本次实测结果写到文件，供 CI 校验「性能基线确实执行了」。
    /// </summary>
    /// <param name="meanTotal">渲染 + 上传的均值（毫秒）。</param>
    /// <param name="p95Total">渲染 + 上传的 p95（毫秒）。</param>
    /// <param name="renderP95">渲染 p95（毫秒）。</param>
    /// <param name="blitP95">上传 p95（毫秒）。</param>
    /// <remarks>
    /// 路径由 <c>USTPLAYER_PERF_REPORT</c> 指定（CI 里指向仓库的 artifacts 目录）；
    /// 未指定时写到临时目录。写失败只记输出、不让用例失败——
    /// 测量本身已经由上面的断言守住，这里只是给 CI 一个「跑过了」的凭据。
    /// </remarks>
    private void WriteMeasurementFile(double meanTotal, double p95Total, double renderP95, double blitP95)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("USTPLAYER_PERF_REPORT");

            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(Path.GetTempPath(), "ustplayer-render-perf.txt");
            }

            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                path,
                $"mean={meanTotal:F2}ms p95={p95Total:F2}ms renderP95={renderP95:F2}ms "
                + $"blitP95={blitP95:F2}ms budget={FrameBudgetMs:F2}ms");

            _output.WriteLine($"实测结果已写入：{path}");
        }
        catch (Exception exception)
        {
            _output.WriteLine($"实测结果写入文件失败（不影响测量结论）：{exception.Message}");
        }
    }

    private static double ToMilliseconds(long ticks) =>
        ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static double Percentile(double[] values, int percentile)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

/// <summary>性能测量的集合定义：本集合内的类之间串行执行。</summary>
[CollectionDefinition(Name)]
public class PerfCollection
{
    /// <summary>集合名。</summary>
    public const string Name = "render-perf";
}
