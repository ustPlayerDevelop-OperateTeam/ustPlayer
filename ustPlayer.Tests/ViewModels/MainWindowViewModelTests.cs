using System;
using System.IO;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 主窗口 ViewModel（外壳关注点）的测试。
/// </summary>
public class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public MainWindowViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"mainvm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败无关紧要
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 三种主题模式分别映射到正确的应用级主题变体。
    /// </summary>
    /// <remarks>
    /// 用 <c>AvaloniaFact</c>：<see cref="MainWindowViewModel.ApplyTheme"/> 会写
    /// <see cref="Application.Current"/>，需要有已初始化的 Avalonia 应用。
    /// <c>auto</c> 必须映射为 <see cref="ThemeVariant.Default"/>——跟随系统要是**持续**的，
    /// 自己判断一次系统主题会在用户改系统设置后失效。
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("light", "Light")]
    [InlineData("dark", "Dark")]
    [InlineData("auto", "Default")]
    public void 主题模式映射到主题变体(string mode, string expectedVariantKey)
    {
        using var services = CreateServices();
        var viewModel = new MainWindowViewModel(services);

        services.Settings.Theme.ThemeMode = mode;
        viewModel.ApplyTheme();

        Assert.NotNull(Application.Current);
        Assert.Equal(
            new ThemeVariant(expectedVariantKey, null),
            Application.Current!.RequestedThemeVariant);
    }

    /// <summary>安装语言不应抛异常（缺目录时内部回退）。</summary>
    [AvaloniaFact]
    public void 安装语言不抛异常()
    {
        using var services = CreateServices();
        var viewModel = new MainWindowViewModel(services);

        viewModel.ApplyLanguage();
        viewModel.ApplyLanguage();
    }

    /// <summary>创建指向临时目录的组合根。</summary>
    /// <returns>组合根（调用方负责释放）。</returns>
    private AppServices CreateServices()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new AppServices(Path.Combine(root, "Settings.json"));
    }
}
