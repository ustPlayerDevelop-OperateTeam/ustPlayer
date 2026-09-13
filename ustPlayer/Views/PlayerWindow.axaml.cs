using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using UstPlayer.Diagnostics;
using UstPlayer.Interop;
using UstPlayer.Models;
using UstPlayer.Platform;
using UstPlayer.Settings;
using UstPlayer.Timing;
using UstPlayer.Views.Rendering;

namespace UstPlayer.Views;

/// <summary>
/// 全屏播放器 —— 自绘画面，并驱动帧循环。
/// </summary>
/// <remarks>
/// <para>
/// <b>画面由本窗口自己画</b>（<see cref="PlayerCanvasRenderer"/> 按 1.1.x 的
/// <c>paintEvent</c> 版式绘制到 <see cref="PlayerCanvas"/> 上），
/// **不经过 uPlRender 渲染器**：渲染器是为视频导出设计的，它的版式与 1.1.x
/// 播放器的观感不一致（实测音名与歌字重叠、缺少播放器应有的布局）。
/// 播放器画面属于播放器自己的职责，渲染器只留给导出。
/// </para>
/// <para>
/// 本类刻意保持**薄**：时序与文字规则在 <see cref="PlaybackSession"/>，
/// 版式在 <see cref="PlayerCanvasRenderer"/>，这里只负责窗口、定时器、
/// 键盘，以及把「会话状态 + 设置」组装成每一帧的快照。
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
    /// <summary>帧间隔：约 60fps（与 1.1.x 的定时器节奏一致）。</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>结束文字显示时长（对应 1.1.x 的 1 秒）。</summary>
    private static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(1);

    /// <summary>第一次音频检查的延迟。</summary>
    /// <remarks>
    /// 比看门狗间隔（3 秒）短得多：音频通常几百毫秒内就绪，
    /// 早检查能更早补上 <c>Play()</c>，也避免开头一段没有伴奏。
    /// </remarks>
    private static readonly TimeSpan FirstAudioCheckDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>版权文案（1.1.x 同款）。</summary>
    private static readonly string CopyrightText =
        $"Presented with {AppInfo.Name} - {AppInfo.Version}";

    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _closeTimer;

    /// <summary>音频看门狗定时器：等就绪 / 等播放开始，超限则降级为墙钟计时。</summary>
    private readonly DispatcherTimer _audioWatchdogTimer;

    private PlaybackSession? _session;
    private PlayerLaunchParams? _parameters;
    private SettingsManager? _settings;

    /// <summary>已解析的 LRC 歌词行（可能为空）。</summary>
    private IReadOnlyList<LrcLine> _lrcLines = [];

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
    internal static PlayerWindow Show(
        PlayerLaunchParams parameters,
        SettingsManager settings,
        IReadOnlyList<LrcLine> lrcLines,
        IAudioBackend? audio)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(settings);

        var window = new PlayerWindow
        {
            _parameters = parameters,
            _settings = settings,
            _lrcLines = lrcLines ?? [],
        };

        // 会话直接由工厂创建：播放器不再需要渲染器（画面自绘）
        window._session = PlaybackSessionFactory.Create(parameters, new SystemClock(), audio);

        // 字体族来自显示设置（空则用默认）；音名与歌字分槽，与 1.1.x 的字体槽一致
        window.Canvas.FontFamilyName = ResolveFontFamily(parameters.Show.FontNote);

        // 先设好窗口标志（XAML 已声明）再显示：全屏与置顶在显示后调整会漏出边角
        window.Show();

        // 时间轴零点锚在「窗口已显示」这一刻（创建到显示之间的几百毫秒不应被计入播放）
        window._session.StartOrResume();
        window._frameTimer.Start();

        // 音频看门狗必须有人周期性驱动，否则伴奏永远不会开始播
        // （曾经漏了这一步：音频一次都没响过，而单元测试全绿）
        window._audioWatchdogTimer.Start();

        window.RenderFrame();

        AppLogger.Info(
            $"播放器已启动（画面自绘，窗口 {window.ClientSize.Width:F0}x{window.ClientSize.Height:F0}）");

        return window;
    }

    /// <summary>
    /// 解析字体族：设置为空或本机没有该字体时回退默认。
    /// </summary>
    /// <param name="configured">设置里的字体族名。</param>
    /// <returns>可用的字体族名；用默认时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// Avalonia 没有运行时字体注册 API（见 <c>docs/plan-deviations.md</c> D9），
    /// 因此这里只做「名字可用就用、否则回默认」，不假装能加载字体文件。
    /// </remarks>
    private static string? ResolveFontFamily(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        try
        {
            return FontManager.Current.TryGetGlyphTypeface(new Typeface(configured), out _)
                ? configured
                : null;
        }
        catch (Exception)
        {
            return null;
        }
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
        if (_session is null || _closing)
        {
            _audioWatchdogTimer.Stop();
            return;
        }

        // 先停下：本次检查若判断还需再等，回调里会按新间隔重新启动
        _audioWatchdogTimer.Stop();

        // 间隔与会话配置同源，不在窗口里另写一份常量
        _audioWatchdogTimer.Interval = WatchdogInterval;

        try
        {
            var retry = CheckAudioReady();

            // 播放真正开始后收工。不能一发出 Play() 就停表——那是异步的，
            // 而定时器一旦启动没人取消，3 秒后就会带着过期状态再检查一次，
            // 把正常播放的音乐误判成「已加载但没播」并掐掉（实测正是如此）。
            var done = !retry && (_session.IsAudioPlaying || !_session.IsAudioHealthy);

            AppLogger.Info(
                $"音频看门狗：{(retry ? "仍在等待" : "已结束检查")}，"
                + $"音频{(_session.IsAudioHealthy ? "在驱动时间轴" : "已降级为墙钟计时")}"
                + $"，{(done ? "停止检查" : "继续检查")}"
                + $"（{_session.AudioState ?? "无伴奏后端"}）");

            if (done)
            {
                _audioWatchdogTimer.Stop();
            }
        }
        catch (Exception exception)
        {
            // 看门狗自身出错不该让播放崩掉：停表并记录，时间轴仍按既有状态运行
            _audioWatchdogTimer.Stop();
            AppLogger.Error("音频看门狗检查失败，已停止检查", exception);
        }
    }

    /// <summary>看门狗重试间隔（与会话配置同源，见会话工厂）。</summary>
    private static TimeSpan WatchdogInterval => TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// 驱动会话的音频看门狗一次，必要时预约下一次。
    /// </summary>
    /// <returns>是否已预约下一次检查。</returns>
    private bool CheckAudioReady()
    {
        if (_session is null)
        {
            return false;
        }

        var retry = false;

        _session.CheckAudioReady(delay =>
        {
            retry = true;
            _audioWatchdogTimer.Interval = delay;
            _audioWatchdogTimer.Start();
        });

        return retry;
    }

    /// <summary>推进一帧并重绘。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_session is null || _closing)
        {
            return;
        }

        try
        {
            var state = _session.Advance();

            RenderFrame(state);

            if (!_firstFrameLogged)
            {
                // 只记一次：证明画面真的画出来了。
                // 部署验证（build/verify-player-launch.ps1）依赖这条日志——
                // 没有它，「窗口开着但一帧都没画出来」会被误判为通过。
                _firstFrameLogged = true;
                AppLogger.Info(
                    $"首帧已绘制（自绘 {ClientSize.Width:F0}x{ClientSize.Height:F0}）");
            }

            if (state.IsPlayerFinished)
            {
                // 结束画面已画出：停帧、停留 1 秒后关闭（与 1.1.x 的时序一致）
                _closing = true;
                _frameTimer.Stop();
                _closeTimer.Start();
                AppLogger.Info(
                    $"播放完成，1 秒后关闭窗口（位置 {state.ElapsedSeconds:F2} 秒，"
                    + $"音频到结尾时 {_session.MediaEndedAtSeconds?.ToString("F2", CultureInfo.InvariantCulture) ?? "未记录"} 秒）");
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

    /// <summary>
    /// 按当前会话状态重绘画面。
    /// </summary>
    /// <param name="state">本帧时序状态；为 <see langword="null"/> 时自己推进一次。</param>
    private void RenderFrame(PlaybackState? state = null)
    {
        if (_session is null || _parameters is null || _settings is null)
        {
            return;
        }

        state ??= _session.Advance();

        // 当前音符要用来画音高曲线；会话把「当前音符」放在状态里，直接取用
        var note = state.CurrentNote;

        var snapshot = PlayerCanvasRenderer.Build(
            _parameters.Style,
            ProjectInfoOf(_parameters, _settings),
            _parameters.Show,
            state,
            note,
            tempo: _parameters.Ust.Tempo,
            showPlayTime: state.Step != PlaybackEndStep.End,
            CopyrightText);

        Canvas.Snapshot = WithLrc(snapshot, state.ElapsedSeconds);
        Canvas.InvalidateVisual();
    }

    /// <summary>取项目信息：优先用设置里的实时值（导入工程后立即反映）。</summary>
    /// <param name="parameters">播放参数。</param>
    /// <param name="settings">设置管理器。</param>
    /// <returns>项目信息。</returns>
    private static ProjectInfo ProjectInfoOf(PlayerLaunchParams parameters, SettingsManager settings) =>
        new()
        {
            ProjectName = settings.Project.ProjectName,
            SongName = settings.Project.SongName,
            SongAuthor = settings.Project.SongAuthor,
            UstAuthor = settings.Project.UstAuthor,
        };

    /// <summary>补上当前 LRC 歌词行。</summary>
    /// <param name="snapshot">快照。</param>
    /// <param name="elapsedSeconds">当前播放位置（秒）。</param>
    /// <returns>带 LRC 文本的快照。</returns>
    private PlayerCanvasSnapshot WithLrc(PlayerCanvasSnapshot snapshot, double elapsedSeconds)
    {
        if (_lrcLines.Count == 0)
        {
            return snapshot;
        }

        var index = LrcTimeline.FindLineIndex(_lrcLines, elapsedSeconds);

        if (index < 0 || index >= _lrcLines.Count)
        {
            return snapshot;
        }

        return snapshot with { LrcText = _lrcLines[index].Text };
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

    /// <summary>关闭时释放会话与定时器。</summary>
    /// <param name="e">事件参数。</param>
    protected override void OnClosed(EventArgs e)
    {
        _frameTimer.Stop();
        _closeTimer.Stop();
        _audioWatchdogTimer.Stop();

        _frameTimer.Tick -= OnFrameTick;
        _closeTimer.Tick -= OnCloseTick;
        _audioWatchdogTimer.Tick -= OnAudioWatchdogTick;

        // 会话释放会停止并释放音频后端（否则关窗后音乐继续播到曲末）
        _session?.Dispose();
        _session = null;

        AppLogger.Info("播放器已关闭");

        base.OnClosed(e);
    }
}
