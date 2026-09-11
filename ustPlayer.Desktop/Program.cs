using System;

using Avalonia;

namespace UstPlayer.Desktop;

/// <summary>
/// 桌面平台入口（Windows / macOS / Linux 共用）。
/// </summary>
internal static class Program
{
    /// <summary>应用程序主入口。</summary>
    /// <param name="args">命令行参数（1.1.x 支持把 .uplr/.uprd 路径作为首个参数传入以自动打开）。</param>
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>由 Avalonia 设计器与 <see cref="Main"/> 共用；方法名不可更改。</summary>
    /// <returns>已配置的 <see cref="AppBuilder"/>。</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
