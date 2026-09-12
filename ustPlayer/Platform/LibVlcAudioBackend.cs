using System;
using System.Threading;
using System.Threading.Tasks;

using LibVLCSharp.Shared;

using UstPlayer.Diagnostics;

namespace UstPlayer.Platform;

/// <summary>
/// 伴奏音频后端 — 基于 <b>LibVLCSharp</b>（libvlc）。
/// </summary>
/// <remarks>
/// <para>
/// 实现既有的窄接口 <see cref="IAudioBackend"/>：播放器只依赖该接口，
/// 因此这里可以把 libvlc 的加载、状态与线程模型完全隔离在内部。
/// </para>
/// <para>
/// <b>为什么选 LibVLC</b>：跨平台覆盖 Windows / macOS / Linux，且是候选（LibVLC / NAudio /
/// FFmpeg.AutoGen）里唯一能延伸到 Android 的——NAudio 仅 Windows，与跨平台目标直接冲突。
/// 代价是每个平台要带原生库，Linux 需系统安装 libvlc。
/// </para>
/// <para>
/// <b>线程模型（本类最容易写错的地方）</b>：libvlc 的事件回调发生在它自己的线程上，
/// 而在回调里再调用 libvlc 的 API（例如播放器收到 <see cref="Ready"/> 后立刻
/// <c>Play()</c>）会**死锁**。因此所有事件都经线程池转投出去再触发，
/// 让调用方永远不在 libvlc 的回调线程上执行。
/// </para>
/// <para>
/// 本类只**如实汇报**媒体状态，不含任何「降级/看门狗」判断——那属于播放时序逻辑
/// （见 <c>Timing/PlaybackSession</c>）。
/// </para>
/// </remarks>
internal sealed class LibVlcAudioBackend : IAudioBackend
{
    /// <summary>libvlc 仅用于音频时不创建视频输出，避免拉起窗口与视频解码。</summary>
    private static readonly string[] VlcOptions = ["--no-video", "--no-video-title-show"];

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;

    private Media? _media;
    private string _currentPath = string.Empty;
    private bool _disposed;

    // ---------- 对外汇报的状态（都由 libvlc 事件驱动） ----------
    private bool _loaded;
    private bool _loading;
    private bool _invalid;
    private bool _finished;

    /// <summary>创建后端并初始化 libvlc。</summary>
    /// <exception cref="DllNotFoundException">找不到原生 libvlc（Linux 上通常是没装）。</exception>
    internal LibVlcAudioBackend()
    {
        // Core.Initialize 负责定位原生库；Windows / macOS 由 NuGet 包提供路径，
        // Linux 走系统库。只在这里调用一次即可（内部幂等）。
        Core.Initialize();

        _libVlc = new LibVLC(VlcOptions);
        _player = new MediaPlayer(_libVlc);

        _player.Playing += OnPlaying;
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnEncounteredError;
    }

    /// <inheritdoc />
    public event EventHandler? Ready;

    /// <inheritdoc />
    public event EventHandler? Ended;

    /// <inheritdoc />
    public event EventHandler<string>? Failed;

    /// <inheritdoc />
    public double PositionSeconds
    {
        get
        {
            var milliseconds = _player.Time;

            // 尚未开始播放时 libvlc 返回 -1
            return milliseconds > 0 ? milliseconds / 1000.0 : 0.0;
        }
    }

    /// <inheritdoc />
    public double DurationSeconds
    {
        get
        {
            var milliseconds = _media?.Duration ?? 0;

            if (milliseconds <= 0)
            {
                milliseconds = _player.Length;
            }

            return milliseconds > 0 ? milliseconds / 1000.0 : 0.0;
        }
    }

    /// <inheritdoc />
    public bool IsPlaying => !_disposed && _player.IsPlaying;

    /// <inheritdoc />
    public bool IsLoaded => _loaded;

    /// <inheritdoc />
    public bool IsLoading => _loading;

    /// <inheritdoc />
    public bool IsInvalid => _invalid;

    /// <inheritdoc />
    public bool IsFinished => _finished;

    /// <inheritdoc />
    /// <remarks>
    /// 解析在后台进行，解析完成后触发 <see cref="Ready"/>。
    /// 与 1.1.x 的 Qt 后端一致：**就绪信号可能早于宿主的第一次推进**，宿主的状态机为此而设。
    /// </remarks>
    public void Load(string musicPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentException.ThrowIfNullOrWhiteSpace(musicPath);

        _loading = true;
        _loaded = false;
        _invalid = false;
        _finished = false;
        _currentPath = musicPath;

        try
        {
            _media?.Dispose();
            _media = new Media(_libVlc, musicPath, FromType.FromPath);

            // 不阻塞调用方：解析完成后再报就绪
            _ = ParseAndAnnounceAsync(_media);
        }
        catch (Exception exception)
        {
            AppLogger.Error($"伴奏媒体创建失败：{musicPath}", exception);
            Fail($"伴奏加载失败：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public void Play()
    {
        if (_disposed || _media is null)
        {
            return;
        }

        try
        {
            _player.Play(_media);
        }
        catch (Exception exception)
        {
            AppLogger.Error("伴奏播放失败", exception);
            Fail($"伴奏播放失败：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _player.Stop();
        }
        catch (Exception exception)
        {
            // 停止失败不应影响上层降级
            AppLogger.Warning($"停止伴奏失败：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _player.Playing -= OnPlaying;
        _player.EndReached -= OnEndReached;
        _player.EncounteredError -= OnEncounteredError;

        // 先停再释放：直接释放正在播放的播放器会让 libvlc 报错
        try
        {
            _player.Stop();
        }
        catch (Exception)
        {
            // 释放路径上不抛
        }

        _media?.Dispose();
        _player.Dispose();
        _libVlc.Dispose();
    }

    // ===================== 解析与事件 =====================

    /// <summary>解析媒体并在完成后报「就绪」。</summary>
    /// <param name="media">媒体。</param>
    /// <returns>任务。</returns>
    private async Task ParseAndAnnounceAsync(Media media)
    {
        try
        {
            // 只解析本地文件；网络/元数据解析在这里没有意义
            var status = await media.Parse(MediaParseOptions.ParseLocal).ConfigureAwait(false);

            if (_disposed)
            {
                return;
            }

            if (status != MediaParsedStatus.Done)
            {
                Fail($"伴奏解析未完成（{status}）");
                return;
            }

            if (media.Duration <= 0)
            {
                Fail("伴奏时长无效（可能是损坏或不支持的文件）");
                return;
            }

            _loading = false;
            _loaded = true;

            Announce(Ready);
        }
        catch (Exception exception)
        {
            AppLogger.Error($"伴奏解析失败：{_currentPath}", exception);
            Fail($"伴奏解析失败：{exception.Message}");
        }
    }

    /// <summary>播放真正开始（libvlc 回调线程）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnPlaying(object? sender, EventArgs e)
    {
        _loading = false;
        _loaded = true;
    }

    /// <summary>播放到结尾（libvlc 回调线程）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnEndReached(object? sender, EventArgs e)
    {
        _finished = true;

        // 重复的 EndReached 不应重复汇报（与 1.1.x 的「播完锚点只记一次」呼应）
        Announce(Ended);
    }

    /// <summary>libvlc 报错（libvlc 回调线程）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnEncounteredError(object? sender, EventArgs e) =>
        Fail("libvlc 播放出错");

    /// <summary>标记为无效并汇报失败。</summary>
    /// <param name="message">可读错误信息。</param>
    private void Fail(string message)
    {
        _loading = false;
        _loaded = false;
        _invalid = true;
        _finished = false;

        AppLogger.Warning($"伴奏后端失败：{message}");

        var handler = Failed;

        if (handler is null)
        {
            return;
        }

        // 转投线程池：见类注释——绝不在 libvlc 的回调线程上回调调用方
        ThreadPool.QueueUserWorkItem(_ => handler(this, message));
    }

    /// <summary>把事件转投到线程池后触发。</summary>
    /// <param name="handler">要触发的事件。</param>
    private void Announce(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => handler(this, EventArgs.Empty));
    }
}
