using System;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using UstPlayer.Diagnostics;
using UstPlayer.Views;

namespace UstPlayer;

/// <summary>
/// 应用入口（共享工程）：初始化主题、组装 <see cref="AppServices"/> 并创建主窗口。
/// 各平台头（<c>ustPlayer.Desktop</c> / <c>ustPlayer.Android</c>）负责创建 AppBuilder，
/// 本类不关心具体平台。
/// </summary>
public partial class App : Application
{
    /// <summary>组合根；桌面生命周期内持有，退出时释放（释放即写回设置）。</summary>
    private AppServices? _services;

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        // 桌面：经典桌面生命周期。Android 等其他生命周期在后续阶段接入。
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = new AppServices();

            // 设置只在退出时写盘（1.1.x 即如此）：避免每次改动都触碰磁盘
            desktop.Exit += OnDesktopExit;

            var options = StartupOptions.Parse(desktop.Args);

            if (options.IsPlayerMode)
            {
                // 直接播放模式：不开主窗口。不设 MainWindow 时生命周期为
                // 「最后一个窗口关闭」→ 播放器关掉即退出。
                StartPlayerOnly(options.PlayUstPath!);
            }
            else
            {
                desktop.MainWindow = new MainWindow(_services);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// <c>--play</c> 模式：解析指定 UST 并直接进入全屏播放。
    /// </summary>
    /// <param name="ustPath">UST 路径。</param>
    /// <remarks>
    /// 失败时记录日志并**不**创建任何窗口——没有主窗口可回退，进程会自然退出。
    /// 这条路径也是 <c>build/verify-player-launch.ps1</c> 验证播放链路的方式。
    /// </remarks>
    private void StartPlayerOnly(string ustPath)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var ustInfo = _services.Ust.Parse(ustPath, _services.Settings.File.Encoding);

            if (ustInfo.Notes.Count == 0)
            {
                AppLogger.Error($"UST 中没有音符，无法播放：{ustPath}");
                return;
            }

            _services.Settings.File.UstPath = ustPath;
            PlayerLauncher.Launch(_services.Settings, ustInfo);

            AppLogger.Info($"已进入直接播放模式：{ustPath}（音符 {ustInfo.Notes.Count} 个）");
        }
        catch (Exception exception)
        {
            AppLogger.Error($"直接播放失败：{ustPath}", exception);
        }
    }

    /// <summary>退出时释放组合根，把设置写回磁盘。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;

        AppLogger.Info("应用已退出");
    }
}
