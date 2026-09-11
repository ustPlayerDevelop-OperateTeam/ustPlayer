using System;

using Avalonia;
using Avalonia.Headless;

using UstPlayer;
using UstPlayer.Tests;

using Xunit;

// Headless 平台由 Avalonia.Headless 提供：不需要显示器即可初始化 Avalonia，
// 使 AppBuilder / XAML / 主题接入能在 CI 与无头环境被真实验证。
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace UstPlayer.Tests;

/// <summary>为 headless 测试提供与 ustPlayer.Desktop/Program.cs 等价的 AppBuilder。</summary>
public static class TestAppBuilder
{
    /// <summary>构建测试用 AppBuilder（必须命名为 BuildAvaloniaApp）。</summary>
    /// <returns>已配置的 <see cref="AppBuilder"/>。</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
