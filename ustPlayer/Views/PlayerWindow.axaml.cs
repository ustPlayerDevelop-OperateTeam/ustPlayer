using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using UstPlayer.Diagnostics;
using UstPlayer.Interop;
using UstPlayer.Models;
using UstPlayer.Platform;
using UstPlayer.Settings;
using UstPlayer.Timing;

namespace UstPlayer.Views;

/// <summary>
/// 全屏播放窗口 — 把帧合成器的输出显示出来，并驱动帧循环。
/// </summary>
/// <remarks>
/// <para>
/// 本类刻意保持**很薄**：画面由 <see cref="PlayerFrameCompositor"/> 产出，
/// 时序与文字规则在 <see cref="PlaybackSession"/>，
/// 这里只负责窗口、定时器、键盘与关闭时机。
/// </para>
/// <para>
/// 与 1.1.x <c>NotePlayerLauncher</c> 相同的教训：<b>窗口标志必须在显示之前设好</b>，
/// 否则全屏与置顶互相冲突会导致边角漏出——因此
/// <c>WindowState</c> / <c>SystemDecorations</c> / <c>Topmost</c> 都写在 XAML 里，
/// 构造时即生效。
/// </para>
/// </remarks>
internal sealed partial class PlayerWindow : Window
{
    /// <summary>帧间隔：约 60fps。</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>结束文字显示时长（对应 1.1.x 的 1 秒）。</summary>
    private static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(1);

    /// <summary>第一次音频检查的延迟。</summary>
    /// <remarks>
    /// 比看门狗间隔（3 秒）短得多：音频通常几百毫秒内就绪，
    /// 早检查能更早补上 <c>Play()</c>，也避免开头一段没有伴奏。
    /// </remarks>
    private static readonly TimeSpan FirstAudioCheckDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>初始渲染尺寸上限的兜底值（拿不到屏幕信息时用）。</summary>
    private const int FallbackViewWidth = 1920;

    /// <summary>初始渲染尺寸上限的兜底值。</summary>
    private const int FallbackViewHeight = 1080;

    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _closeTimer;

    /// <summary>音频看门狗定时器：等就绪 / 等播放开始，超限则降级为墙钟计时。</summary>
    private readonly DispatcherTimer _audioWatchdogTimer;

    private PlayerFrameCompositor? _compositor;
    private bool _suspended;
    private bool _closing;
    private bool _firstFrameLogged;

    /// <summary>创建播放窗口。</summary>
    private PlayerWindow()
    {
        InitializeComponent();

        _frameTimer = new DispatcherTimer { Interval = FrameInterval };
        _frameTimer.Tick += OnFrameTick;

        _closeTimer = new DispatcherTimer { Interval = EndGrace };
        _closeTimer.Tick += OnCloseTick;
        _closeTimer.IsEnabled = false;

        // 首次检查的间隔取小值而不是看门狗间隔（3 秒）：音频通常几百毫秒内就绪，
        // 早一点看到「已加载但未播放」就能早一点补上 Play()，不必白等 3 秒
        _audioWatchdogTimer = new DispatcherTimer { Interval = FirstAudioCheckDelay };
        _audioWatchdogTimer.Tick += OnAudioWatchdogTick;
    }

    /// <summary>
    /// 创建并显示播放窗口。
    /// </summary>
    /// <param name="parameters">播放参数。</param>
    /// <param name="settings">设置管理器。</param>
    /// <param name="lrcLines">已解析的歌词行。</param>
    /// <param name="audio">音频后端；为 <see langword="null"/> 时走纯可视化计时。</param>
    /// <returns>播放窗口。</returns>
    /// <exception cref="RendererException">渲染器缺失或配置失败。</exception>
    internal static PlayerWindow Show(
        PlayerLaunchParams parameters,
        SettingsManager settings,
        IReadOnlyList<LrcLine> lrcLines,
        IAudioBackend? audio)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(settings);

        var window = new PlayerWindow();
        var (viewWidth, viewHeight) = window.EstimateViewSize();

        window._compositor = PlayerFrameCompositor.Create(
            parameters, settings, lrcLines, new SystemClock(), audio, viewWidth, viewHeight);

        window.FrameImage.Source = window._compositor.Bitmap;

        // 先设好窗口标志（XAML 已声明）再显示：全屏与置顶在显示后调整会漏出边角
        window.Show();

        // 时间轴零点锚在「窗口已显示」这一刻（创建到显示之间的几百毫秒不应被计入播放）
        window._compositor.Start();
        window._frameTimer.Start();

        // 音频看门狗必须有人周期性驱动，否则伴奏永远不会开始播
        // （曾经漏了这一步：音频一次都没响过，而单元测试全绿）
        window._audioWatchdogTimer.Start();

        AppLogger.Info($"播放器已启动（初始渲染尺寸 {viewWidth}x{viewHeight}）");

        return window;
    }

    /// <summary>
    /// 音频看门狗的一次检查。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 会话在需要继续等待时会回调调度器（就是本窗口的定时器）：换上下一次的间隔再等。
    /// 不需要等待即停表——后续状态由音频事件自己驱动。
    /// </remarks>
    private void OnAudioWatchdogTick(object? sender, EventArgs e)
    {
        if (_compositor is null || _closing)
        {
            _audioWatchdogTimer.Stop();
            return;
        }

        // 先停下：本次检查若判断还需再等，回调里会按新间隔重新启动
        _audioWatchdogTimer.Stop();

        // 间隔从合成器取（与会话配置同源），不在窗口里另写一份常量
        _audioWatchdogTimer.Interval = _compositor.WatchdogInterval;

        try
        {
            var retry = _compositor.CheckAudioReady(new DispatcherPlaybackScheduler(_audioWatchdogTimer));

            // 播放真正开始后收工。不能一发出 Play() 就停表——那是异步的，
            // 而定时器一旦启动没人取消，3 秒后就会带着过期状态再检查一次，
            // 把正常播放的音乐误判成「已加载但没播」并掐掉（实测正是如此）。
            var done = !retry && (_compositor.IsAudioPlaying || !_compositor.IsAudioHealthy);

            // 把「音频到底有没有在驱动时间轴」写进日志：这条链路出问题时的表现是
            // 「画面在动但没有声音」，从界面上分不出是「没配伴奏」「后端失败」还是
            // 「看门狗没接线」——只有日志能区分，因此把后端状态一并写出来
            AppLogger.Info(
                $"音频看门狗：{(retry ? "仍在等待" : "已结束检查")}，"
                + $"音频{(_compositor.IsAudioHealthy ? "在驱动时间轴" : "已降级为墙钟计时")}"
                + $"，{(done ? "停止检查" : "继续检查")}"
                + $"（{_compositor.AudioState ?? "无伴奏后端"}）");

            if (done)
            {
                _audioWatchdogTimer.Stop();
            }
        }
        catch (ObjectDisposedException)
        {
            _audioWatchdogTimer.Stop();
        }
        catch (Exception exception)
        {
            // 看门狗自身出错不该让播放崩掉：停表并记录，时间轴仍按既有状态运行
            _audioWatchdogTimer.Stop();
            AppLogger.Error("音频看门狗检查失败，已停止检查", exception);
        }
    }

    /// <summary>
    /// 在显示之前估算窗口内容尺寸，用于决定初始渲染分辨率。
    /// </summary>
    /// <returns>估算的内容尺寸（像素）。</returns>
    /// <remarks>
    /// 全屏窗口在 <c>Show()</c> 之前拿不到最终客户区尺寸，这里按主屏的**物理像素**估算；
    /// 首帧之后 <see cref="PlayerFrameCompositor"/> 会按真实客户区尺寸自行修正。
    /// </remarks>
    private (int Width, int Height) EstimateViewSize()
    {
        try
        {
            var primary = Screens.Primary;
            if (primary is not null)
            {
                var scaling = primary.Scaling <= 0 ? 1.0 : primary.Scaling;
                var width = (int)Math.Round(primary.Bounds.Width * scaling);
                var height = (int)Math.Round(primary.Bounds.Height * scaling);

                if (width > 0 && height > 0)
                {
                    return (width, height);
                }
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"读取主屏尺寸失败，使用兜底值：{exception.Message}");
        }

        return (FallbackViewWidth, FallbackViewHeight);
    }

    /// <summary>当前窗口内容尺寸（像素），已按渲染器的 DPI 缩放换算。</summary>
    /// <returns>内容尺寸。</returns>
    private (int Width, int Height) CurrentViewSize()
    {
        var size = ClientSize;
        var scaling = RenderScaling <= 0 ? 1.0 : RenderScaling;

        var width = (int)Math.Round(size.Width * scaling);
        var height = (int)Math.Round(size.Height * scaling);

        if (width <= 0 || height <= 0)
        {
            return (FallbackViewWidth, FallbackViewHeight);
        }

        return (width, height);
    }

    /// <summary>推进一帧并刷新画面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_compositor is null || _closing)
        {
            return;
        }

        try
        {
            var (width, height) = CurrentViewSize();
            var frame = _compositor.Advance(width, height);

            // 位图可能因尺寸变化被重建，需要重新绑定
            if (!ReferenceEquals(FrameImage.Source, frame.Bitmap))
            {
                FrameImage.Source = frame.Bitmap;
            }

            if (!_firstFrameLogged)
            {
                // 只记一次：证明渲染器真的出了帧并拷进了位图。
                // 部署验证（build/verify-player-launch.ps1）依赖这条日志——
                // 没有它，「窗口开着但一帧都没画出来」会被误判为通过。
                _firstFrameLogged = true;
                AppLogger.Info($"首帧已渲染（{_compositor.RenderWidth}x{_compositor.RenderHeight}）");
            }

            if (frame.State.IsPlayerFinished)
            {
                // 结束画面已画出：停帧、停留 1 秒后关闭（与 1.1.x 的时序一致）
                _closing = true;
                _frameTimer.Stop();
                _closeTimer.Start();
                AppLogger.Info(
                    $"播放完成，1 秒后关闭窗口（位置 {frame.State.ElapsedSeconds:F2} 秒，"
                    + $"音频到结尾时 {_compositor.AudioEndedAtSeconds?.ToString("F2") ?? "未记录"} 秒）"
                    + $" | {_compositor.PlaybackStateDescription}");
            }
        }
        catch (ObjectDisposedException)
        {
            // 窗口已在关闭流程中
            _frameTimer.Stop();
        }
        catch (Exception exception)
        {
            // 单帧失败不应让播放崩溃：记录并停帧，交由用户按 Esc 关闭
            AppLogger.Error("播放帧渲染失败，已停止播放", exception);
            _frameTimer.Stop();
        }
    }

    private void OnCloseTick(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        Close();
    }

    /// <summary>Esc 退出播放。</summary>
    /// <param name="e">按键事件参数。</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>最小化 / 恢复时暂停与恢复帧循环，避免后台空转。</summary>
    /// <param name="e">属性变更事件参数。</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property != WindowStateProperty || _closing)
        {
            return;
        }

        var minimized = WindowState == WindowState.Minimized;

        if (minimized && !_suspended)
        {
            _suspended = true;
            _frameTimer.Stop();
            return;
        }

        if (!minimized && _suspended)
        {
            _suspended = false;
            _frameTimer.Start();
        }
    }

    /// <summary>关闭时释放合成器与定时器。</summary>
    /// <param name="e">事件参数。</param>
    protected override void OnClosed(EventArgs e)
    {
        _frameTimer.Stop();
        _closeTimer.Stop();
        _audioWatchdogTimer.Stop();

        _frameTimer.Tick -= OnFrameTick;
        _closeTimer.Tick -= OnCloseTick;
        _audioWatchdogTimer.Tick -= OnAudioWatchdogTick;

        // 先解绑显示源再释放位图，避免渲染线程仍引用已释放的位图
        FrameImage.Source = null;

        _compositor?.Dispose();
        _compositor = null;

        AppLogger.Info($"播放器已关闭（音频={_compositor?.AudioState ?? "已释放"}）");
        base.OnClosed(e);
    }
}
