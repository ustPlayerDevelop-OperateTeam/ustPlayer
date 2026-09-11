using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using UstPlayer.Views;

namespace UstPlayer;

/// <summary>
/// 应用入口（共享工程）：初始化主题与主窗口。
/// 各平台头（<c>ustPlayer.Desktop</c> / <c>ustPlayer.Android</c>）负责创建 AppBuilder，
/// 本类不关心具体平台。
/// </summary>
public partial class App : Application
{
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
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
