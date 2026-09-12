using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Media.Imaging;

using UstPlayer.Diagnostics;
using UstPlayer.Interop;
using UstPlayer.Models;
using UstPlayer.Platform;
using UstPlayer.Settings;
using UstPlayer.Timing;
using UstPlayer.Video;

namespace UstPlayer.Views;

/// <summary>一帧的产出：位图（供显示）与当前时序状态（供判断是否结束）。</summary>
/// <param name="Bitmap">位图（每帧复用同一实例）。</param>
/// <param name="State">本帧的时序状态。</param>
internal readonly record struct ComposedFrame(WriteableBitmap Bitmap, PlaybackState State);

/// <summary>
/// 播放器帧合成器 — 驱动「渲染器出帧 → 拷入位图」这条链路。
/// </summary>
/// <remarks>
/// <para>
/// 职责边界：本类只做**画面**（渲染尺寸决策、缓冲复用、位图拷贝）；
/// 时序与文字规则在 <see cref="PlaybackSession"/>；窗口与定时器在 <c>PlayerWindow</c>。
/// 这样这条链路可以在无头环境被验证（Spike 0b 已验证耗时，本类验证正确性与生命周期）。
/// </para>
/// <para>
/// <b>渲染尺寸</b>：按窗口实际尺寸渲染，并 clamp 到
/// <see cref="MaxRenderWidth"/> × <see cref="MaxRenderHeight"/>。
/// 之所以不是「固定 1080p 再放大」——渲染器的字号是按画布尺寸算的，
/// 小窗口用 1080p 画布渲染后缩小显示会让文字小到看不清。
/// 上限则用于规避 ADR 0001 记录的 4K 实时性能不足（4K 下渲染约 18.7ms/帧，达不到 60fps）。
/// </para>
/// </remarks>
internal sealed class PlayerFrameCompositor : IDisposable
{
    /// <summary>渲染尺寸上限（见 ADR 0001：4K 全屏实时渲染达不到 60fps）。</summary>
    internal const int MaxRenderWidth = 1920;

    /// <summary>渲染尺寸上限。</summary>
    internal const int MaxRenderHeight = 1080;

    private readonly UplRenderContext _renderer;
    private readonly PlaybackSession _session;
    private readonly string _renderConfigTemplate;
    private readonly string _ustJson;

    private WriteableBitmap? _bitmap;

    /// <summary>复用同一份渲染缓冲，避免每帧分配（Spike 0b 已确认这是必要的）。</summary>
    private byte[] _buffer = [];

    private bool _disposed;

    /// <summary>创建帧合成器（尚未分配缓冲）。</summary>
    /// <param name="renderer">渲染上下文（调用方负责其生命周期）。</param>
    /// <param name="session">播放时序会话（调用方负责其生命周期）。</param>
    /// <param name="renderConfigTemplate">已配置的渲染配置（复用；尺寸变化时只改宽高）。</param>
    /// <param name="ustJson">UST JSON。</param>
    private PlayerFrameCompositor(
        UplRenderContext renderer,
        PlaybackSession session,
        string renderConfigTemplate,
        string ustJson)
    {
        _renderer = renderer;
        _session = session;
        _renderConfigTemplate = renderConfigTemplate;
        _ustJson = ustJson;
    }

    /// <summary>当前渲染宽度（首次 <see cref="Advance"/> 前为 0）。</summary>
    internal int RenderWidth { get; private set; }

    /// <summary>当前渲染高度（首次 <see cref="Advance"/> 前为 0）。</summary>
    internal int RenderHeight { get; private set; }

    /// <summary>位图（供 UI 绑定显示）；首次推进前为 <see langword="null"/>。</summary>
    internal WriteableBitmap? Bitmap => _bitmap;

    /// <summary>
    /// 创建合成器并完成渲染器配置。
    /// </summary>
    /// <param name="parameters">播放参数（含 UST 与样式）。</param>
    /// <param name="settings">设置管理器。</param>
    /// <param name="lrcLines">已解析的歌词行（可为空）。</param>
    /// <param name="clock">时钟。</param>
    /// <param name="audio">音频后端；为 <see langword="null"/> 时走纯可视化计时。</param>
    /// <param name="viewWidth">初始窗口内容宽度；传 <see langword="null"/> 时首次推进才分配缓冲。</param>
    /// <param name="viewHeight">初始窗口内容高度。</param>
    /// <returns>合成器。</returns>
    /// <remarks>
    /// <b>尺寸可以后到</b>：全屏窗口在显示之前拿不到真实客户区尺寸，
    /// 因此允许不传初始尺寸——首次 <see cref="Advance"/> 会按当时给定的尺寸分配位图，
    /// 之后仅在尺寸真变化时重建（见 <see cref="EnsureTargetSize"/>）。
    /// </remarks>
    internal static PlayerFrameCompositor Create(
        PlayerLaunchParams parameters,
        SettingsManager settings,
        IReadOnlyList<LrcLine> lrcLines,
        IClock clock,
        IAudioBackend? audio,
        int? viewWidth = null,
        int? viewHeight = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        var renderConfig = RenderConfig.Build(parameters, width: 1, height: 1, fps: 60);
        var ustJson = RenderConfig.BuildUstJson(parameters.Ust);

        var renderer = UplRenderContext.Create();

        try
        {
            renderer.SetConfig(renderConfig);
            renderer.SetUstText(ustJson);

            if (BuildLrcText(lrcLines) is { } lrcText)
            {
                renderer.SetLrcText(lrcText);
            }
        }
        catch (Exception)
        {
            // 配置失败时释放上下文，避免句柄泄漏
            renderer.Dispose();
            throw;
        }

        var session = new PlaybackSession(CreateSessionOptions(parameters), clock, audio);

        AppLogger.Info($"播放器帧合成器就绪：音符 {parameters.Ust.Notes.Count} 个");

        var compositor = new PlayerFrameCompositor(renderer, session, renderConfig, ustJson);

        // 给了初始尺寸就先分配好，使窗口能在首帧之前拿到位图绑定到 Image
        if (viewWidth is { } width && viewHeight is { } height)
        {
            compositor.EnsureTargetSize(width, height);
        }

        return compositor;
    }

    /// <summary>
    /// 按窗口尺寸解析实际渲染尺寸（clamp 到上限，且至少 1 像素）。
    /// </summary>
    /// <param name="viewWidth">窗口宽度。</param>
    /// <param name="viewHeight">窗口高度。</param>
    /// <returns>渲染尺寸。</returns>
    internal static (int Width, int Height) ResolveRenderSize(int viewWidth, int viewHeight)
    {
        var width = Math.Clamp(viewWidth, 1, MaxRenderWidth);
        var height = Math.Clamp(viewHeight, 1, MaxRenderHeight);
        return (width, height);
    }

    /// <summary>
    /// 开始计时：锚定时间轴零点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须在窗口真正显示之后调用</b>：全屏窗口从创建到显示要几百毫秒，
    /// 在那之前锚定会让开头一小段被跳过。可重复调用，只有第一次生效。
    /// </para>
    /// <para>
    /// 忘记调用不会导致错误结果——<see cref="PlaybackSession.Advance"/> 会自行兜底锚定；
    /// 这里的显式调用是为了让零点落在「窗口显示」这一刻，而不是「第一帧」这一刻。
    /// </para>
    /// </remarks>
    internal void Start() => _session.StartOrResume();

    /// <summary>
    /// 推进一帧：渲染到缓冲并拷入位图。
    /// </summary>
    /// <param name="viewWidth">当前窗口内容宽度（像素）。</param>
    /// <param name="viewHeight">当前窗口内容高度（像素）。</param>
    /// <returns>位图与当前时序状态。</returns>
    /// <exception cref="ObjectDisposedException">已释放。</exception>
    internal ComposedFrame Advance(int viewWidth, int viewHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var state = _session.Advance();

        EnsureTargetSize(viewWidth, viewHeight);
        _renderer.RenderToBuffer(state.ElapsedSeconds, _buffer, RenderWidth, RenderHeight);
        Blit();

        return new ComposedFrame(_bitmap!, state);
    }

    /// <summary>按给定窗口尺寸确保位图与缓冲就绪（尺寸变化时重建）。</summary>
    /// <param name="viewWidth">窗口内容宽度。</param>
    /// <param name="viewHeight">窗口内容高度。</param>
    /// <remarks>
    /// 只在尺寸**真的**变化时重建。全屏窗口在 Show() 之后才拿到真实客户区尺寸，
    /// 因此首次推进时完成分配，之后通常不再变化。
    /// </remarks>
    private void EnsureTargetSize(int viewWidth, int viewHeight)
    {
        var (width, height) = ResolveRenderSize(viewWidth, viewHeight);

        if (_bitmap is not null && width == RenderWidth && height == RenderHeight)
        {
            return;
        }

        var previous = _bitmap;
        var previousWidth = RenderWidth;
        var previousHeight = RenderHeight;

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            RendererPixelFormat.BitmapFormat,
            RendererPixelFormat.BitmapAlphaFormat);

        _bitmap = bitmap;
        _buffer = new byte[RendererPixelFormat.BufferSize(width, height)];
        RenderWidth = width;
        RenderHeight = height;

        // 渲染配置里的宽高决定画面布局，必须同步更新，否则文字与画布尺寸不匹配
        _renderer.SetConfig(RenderConfigTemplateWithSize(width, height));
        _renderer.SetUstText(_ustJson);

        // 旧位图在没有窗口引用之后才可释放；否则正在显示的帧会被销毁
        if (previous is not null)
        {
            AppLogger.Debug($"渲染尺寸变化：{previousWidth}x{previousHeight} → {width}x{height}");
            previous.Dispose();
        }
    }

    /// <summary>把配置模板里的宽高替换为指定值。</summary>
    /// <param name="width">宽。</param>
    /// <param name="height">高。</param>
    /// <returns>配置 JSON。</returns>
    private string RenderConfigTemplateWithSize(int width, int height)
    {
        // 模板由 RenderConfig.Build 生成，宽高固定为 1x1；此处按真实尺寸替换。
        // 用 JSON 解析而不是字符串替换，避免误改到其他含 "1" 的字段。
        var node = System.Text.Json.Nodes.JsonNode.Parse(_renderConfigTemplate)!.AsObject();
        node["width"] = width;
        node["height"] = height;

        return node.ToJsonString(RenderConfig.SerializerOptions);
    }

    /// <summary>把渲染缓冲拷入位图帧缓冲。</summary>
    /// <remarks>
    /// 用 <c>Lock()</c> 取帧缓冲后整块拷贝。写时按行距校验过：本合成器用
    /// <see cref="RendererPixelFormat.BitmapFormat"/> 构造位图，行距应等于宽度 × 4；
    /// 若将来换成非紧凑格式，这里会立刻抛错而不是**静默画出错位的画面**。
    /// </remarks>
    private unsafe void Blit()
    {
        using var framebuffer = _bitmap!.Lock();

        var rowBytes = framebuffer.RowBytes;
        var expectedRowBytes = framebuffer.Size.Width * RendererPixelFormat.BytesPerPixel;

        if (rowBytes != expectedRowBytes)
        {
            throw new InvalidOperationException(
                $"位图行距 {rowBytes} 与紧凑排布 {expectedRowBytes} 不一致，逐行拷贝尚未实现");
        }

        var destination = new Span<byte>((void*)framebuffer.Address, rowBytes * framebuffer.Size.Height);
        _buffer.AsSpan(0, destination.Length).CopyTo(destination);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
        _renderer.Dispose();
        _bitmap?.Dispose();
    }

    /// <summary>构造会话选项（把设置与播放参数映射为时序配置）。</summary>
    /// <param name="parameters">播放参数。</param>
    /// <returns>会话选项。</returns>
    private static PlaybackSessionOptions CreateSessionOptions(PlayerLaunchParams parameters)
    {
        var text = new PlaybackTextOptions(
            ParseSilentMode(parameters.Style.SilentDisplay),
            parameters.Style.SilentCustomText,
            ParseEndMode(parameters.Style.EndDisplay),
            parameters.Style.EndCustomText,
            ParsePitchMode(parameters.Style.PitchPlaceholder),
            parameters.Style.PitchCustomText);

        return new PlaybackSessionOptions(parameters.Ust.Tempo, parameters.Ust.Notes, text);
    }

    /// <summary>把存储层稳定 key 映射为静默显示方式。</summary>
    /// <param name="key">稳定 key。</param>
    /// <returns>显示方式。</returns>
    private static SilentDisplayMode ParseSilentMode(string key) => key switch
    {
        "r" => SilentDisplayMode.Rest,
        "dash" => SilentDisplayMode.Dash,
        "custom" => SilentDisplayMode.Custom,
        _ => SilentDisplayMode.None,
    };

    /// <summary>把存储层稳定 key 映射为结束显示方式。</summary>
    /// <param name="key">稳定 key。</param>
    /// <returns>显示方式。</returns>
    private static EndDisplayMode ParseEndMode(string key) => key switch
    {
        "end" => EndDisplayMode.End,
        "dash" => EndDisplayMode.Dash,
        "custom" => EndDisplayMode.Custom,
        _ => EndDisplayMode.None,
    };

    /// <summary>把存储层稳定 key 映射为音名占位符方式。</summary>
    /// <param name="key">稳定 key。</param>
    /// <returns>占位符方式。</returns>
    private static PitchPlaceholderMode ParsePitchMode(string key) => key switch
    {
        "dash" => PitchPlaceholderMode.Dash,
        "custom" => PitchPlaceholderMode.Custom,
        _ => PitchPlaceholderMode.None,
    };

    /// <summary>把解析好的歌词行还原为渲染器需要的 LRC 文本。</summary>
    /// <param name="lines">歌词行。</param>
    /// <returns>LRC 文本；无歌词返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 渲染器接受的是 LRC 文本（内部再自行解析），因此这里做一次回写。
    /// 保持 <c>[mm:ss.cc]</c> 格式，与渲染器的解析器兼容。
    /// </remarks>
    private static string? BuildLrcText(IReadOnlyList<LrcLine> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            var totalSeconds = line.TimestampSeconds;
            var minutes = (int)(totalSeconds / 60);
            var seconds = totalSeconds - (minutes * 60);

            builder.Append('[')
                .Append(minutes.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
                .Append(':')
                .Append(seconds.ToString("00.00", System.Globalization.CultureInfo.InvariantCulture))
                .Append(']')
                .Append(line.Text)
                .Append('\n');
        }

        return builder.ToString();
    }
}
