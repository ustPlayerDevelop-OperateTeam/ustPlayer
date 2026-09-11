using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using UstPlayer.Views;

using Xunit;

namespace UstPlayer.Tests;

/// <summary>
/// 工程骨架冒烟测试：验证 App.axaml（FluentAvaloniaTheme 接入）能在无头环境下
/// 真实加载。<c>App.axaml</c> 的主题接入一旦写错，会在 XAML 编译或运行时立刻暴露。
/// </summary>
public class SkeletonSmokeTests
{
    /// <summary>
    /// FluentAvaloniaTheme 应已挂载到 Application.Styles。
    /// 若有人误改成 Avalonia 自带的 FluentTheme 或漏挂主题，此测试失败。
    /// </summary>
    [AvaloniaFact]
    public void FluentAvalonia_主题已挂载()
    {
        var app = Avalonia.Application.Current;
        Assert.NotNull(app);

        var hasFluentAvaloniaTheme = app!.Styles.Any(
            style => style.GetType().FullName == "FluentAvalonia.Styling.FluentAvaloniaTheme");

        Assert.True(hasFluentAvaloniaTheme, "App.axaml 未挂载 FluentAvaloniaTheme");
    }

    /// <summary>不应同时挂载 Avalonia 自带 FluentTheme（官方说明会产生视觉瑕疵）。</summary>
    [AvaloniaFact]
    public void 不应叠加_Avalonia_自带_FluentTheme()
    {
        var app = Avalonia.Application.Current;
        Assert.NotNull(app);

        var hasBuiltInFluentTheme = app!.Styles.Any(
            style => style.GetType().FullName == "Avalonia.Themes.Fluent.FluentTheme");

        Assert.False(hasBuiltInFluentTheme, "不应与 FluentAvaloniaTheme 叠加 Avalonia 自带 FluentTheme");
    }

    /// <summary>
    /// 窗口基类应为 FluentAvalonia 的 <c>AppWindow</c>（WinUI 风格窗口），
    /// 做法与 ClassIsland 一致；详见 docs/adr-0002-window-chrome.md。
    /// </summary>
    /// <remarks>
    /// 这里只做类型关系断言，**不实例化窗口**：<c>AppWindow</c> 构造函数会无条件创建
    /// <c>FluentAvalonia.UI.Windowing.Win32WindowManager</c>，在 Avalonia 的 headless
    /// 平台上会因重复注册 Win32 属性而抛
    /// <c>ArgumentException: An item with the same key has already been added. Key: FluentAvalonia.Interop.Win32.HWND</c>。
    /// 该问题只影响 headless，不影响真实桌面运行；窗口的实例化与启动由
    /// <c>build/verify-app-launch.ps1</c> 以真实进程启动的方式验证。
    /// </remarks>
    [Fact]
    public void 窗口基类应继承_AppWindow()
    {
        Assert.True(
            typeof(FluentAvalonia.UI.Windowing.AppWindow).IsAssignableFrom(typeof(ShellWindow)),
            "ShellWindow 应继承 FluentAvalonia 的 AppWindow");
        Assert.True(
            typeof(ShellWindow).IsAssignableFrom(typeof(MainWindow)),
            "MainWindow 应继承 ShellWindow");
        Assert.True(
            typeof(Window).IsAssignableFrom(typeof(ShellWindow)),
            "ShellWindow 最终应仍是 Avalonia 的 Window");
    }

    /// <summary>版本号应可解析，且不含 SDK 自动附加的 +{git-sha} 构建元数据。</summary>
    [Fact]
    public void 版本号应可解析且不含构建元数据()
    {
        var version = ShellWindow.AppVersion;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain("+", version, StringComparison.Ordinal);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }
}
