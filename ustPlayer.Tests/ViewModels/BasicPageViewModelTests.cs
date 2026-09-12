using System;
using System.IO;

using UstPlayer.Settings;
using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 基础页 ViewModel 的测试。
/// </summary>
/// <remarks>
/// 这里刻意**不测**「界面是否同步」——2.0 中控件直接绑定设置子域，
/// 设置变更会自动推给界面，没有可测的中间同步步骤。
/// 本类覆盖的是页面逻辑本身：工程导入 / 导出、最近目录记忆、默认文件名。
/// </remarks>
public class BasicPageViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public BasicPageViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"basicvm-{Guid.NewGuid():N}");
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

    /// <summary>项目名为空时给出「未命名」，避免保存对话框出现空文件名。</summary>
    [Theory]
    [InlineData("", "未命名")]
    [InlineData("   ", "未命名")]
    [InlineData("我的歌", "我的歌")]
    public void 建议文件名(string projectName, string expected)
    {
        using var services = CreateServices();
        var viewModel = new BasicPageViewModel(services);

        services.Settings.Project.ProjectName = projectName;

        Assert.Equal(expected, viewModel.SuggestProjectFileName());
    }

    /// <summary>导出后再导入应完整还原项目信息（并顺带验证 ViewModel 接的是同一个设置门面）。</summary>
    [Fact]
    public void 导出后导入可还原项目信息()
    {
        var uplrPath = Path.Combine(_tempDirectory, "song.uplr");

        using (var services = CreateServices())
        {
            var viewModel = new BasicPageViewModel(services);

            services.Settings.Project.ProjectName = "往返工程";
            services.Settings.Project.SongName = "曲名&曲师";
            services.Settings.Project.UstAuthor = "调音师";

            viewModel.ExportProject(uplrPath);
        }

        Assert.True(File.Exists(uplrPath));

        // 用一条全新的链路导入，确认是从文件读回来的
        using (var services = CreateServices())
        {
            var viewModel = new BasicPageViewModel(services);

            services.Settings.Project.ProjectName = "导入前会被覆盖";

            viewModel.ImportProject(uplrPath);

            Assert.Equal("往返工程", services.Settings.Project.ProjectName);
            Assert.Equal("曲名&曲师", services.Settings.Project.SongName);
            Assert.Equal("调音师", services.Settings.Project.UstAuthor);
        }
    }

    /// <summary>导出与导入各自记住对应方向的最近目录。</summary>
    [Fact]
    public void 导入导出分别记住最近目录()
    {
        var exportDirectory = Path.Combine(_tempDirectory, "export");
        var openDirectory = Path.Combine(_tempDirectory, "open");
        Directory.CreateDirectory(exportDirectory);
        Directory.CreateDirectory(openDirectory);

        using var services = CreateServices();
        var viewModel = new BasicPageViewModel(services);

        var exportPath = Path.Combine(exportDirectory, "a.uplr");
        viewModel.ExportProject(exportPath);
        Assert.Equal(exportDirectory, viewModel.LastExportDirectory);

        // 把导出的工程搬到另一个目录再导入，验证记的是打开方向的目录
        var importPath = Path.Combine(openDirectory, "a.uplr");
        File.Copy(exportPath, importPath);

        viewModel.ImportProject(importPath);
        Assert.Equal(openDirectory, viewModel.LastOpenDirectory);
    }

    /// <summary>空路径参数必须被拒绝（调用方保证路径已规范化）。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 空路径被拒绝(string path)
    {
        using var services = CreateServices();
        var viewModel = new BasicPageViewModel(services);

        Assert.Throws<ArgumentException>(() => viewModel.ImportProject(path));
        Assert.Throws<ArgumentException>(() => viewModel.ExportProject(path));
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
