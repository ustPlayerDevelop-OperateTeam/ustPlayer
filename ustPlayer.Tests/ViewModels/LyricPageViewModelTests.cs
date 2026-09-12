using System;
using System.IO;

using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 歌词页 ViewModel 的测试：选择对话框起始目录的决策。
/// </summary>
/// <remarks>
/// 这条逻辑的分支（歌词所在目录 / 上次打开目录 / 目录已不存在）在手工点击时很难逐个覆盖。
/// </remarks>
public class LyricPageViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public LyricPageViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"lyricvm-{Guid.NewGuid():N}");
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

    /// <summary>已选歌词时优先用它所在目录。</summary>
    [Fact]
    public void 优先用歌词所在目录()
    {
        using var services = CreateServices();
        var viewModel = new LyricPageViewModel(services);

        var lyricDirectory = CreateDirectory("lyrics");
        services.Settings.Player.LrcPath = Path.Combine(lyricDirectory, "a.lrc");
        services.Settings.LastOpenDirectory = CreateDirectory("last-open");

        Assert.Equal(lyricDirectory, viewModel.ResolveStartDirectory());
    }

    /// <summary>未选歌词时退回上次打开工程的目录。</summary>
    [Fact]
    public void 未选歌词时用上次打开目录()
    {
        using var services = CreateServices();
        var viewModel = new LyricPageViewModel(services);

        var lastOpen = CreateDirectory("last-open");
        services.Settings.Player.LrcPath = string.Empty;
        services.Settings.LastOpenDirectory = lastOpen;

        Assert.Equal(lastOpen, viewModel.ResolveStartDirectory());
    }

    /// <summary>歌词所在目录已被删除/改名时不能把它交给对话框，应退回上次打开目录。</summary>
    [Fact]
    public void 歌词目录不存在时退回上次目录()
    {
        using var services = CreateServices();
        var viewModel = new LyricPageViewModel(services);

        var lastOpen = CreateDirectory("last-open");
        services.Settings.Player.LrcPath = Path.Combine(_tempDirectory, "已删除的目录", "a.lrc");
        services.Settings.LastOpenDirectory = lastOpen;

        Assert.Equal(lastOpen, viewModel.ResolveStartDirectory());
    }

    /// <summary>两个目录都不可用时返回空（对话框用系统默认位置），而不是抛异常。</summary>
    [Fact]
    public void 都不可用时返回空()
    {
        using var services = CreateServices();
        var viewModel = new LyricPageViewModel(services);

        services.Settings.Player.LrcPath = string.Empty;
        services.Settings.LastOpenDirectory = Path.Combine(_tempDirectory, "不存在的目录");

        Assert.Null(viewModel.ResolveStartDirectory());
    }

    /// <summary>创建指向临时目录的组合根。</summary>
    /// <returns>组合根（调用方负责释放）。</returns>
    private AppServices CreateServices()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new AppServices(Path.Combine(root, "Settings.json"));
    }

    /// <summary>在临时目录下建一个子目录。</summary>
    /// <param name="name">目录名。</param>
    /// <returns>目录绝对路径。</returns>
    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(path);

        return path;
    }
}
