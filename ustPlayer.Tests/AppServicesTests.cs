using System;
using System.IO;

using UstPlayer.Settings;

using Xunit;

namespace UstPlayer.Tests;

/// <summary>
/// 组合根（<see cref="AppServices"/>）的装配与生命周期测试。
/// </summary>
/// <remarks>
/// 它不承载业务逻辑，但它决定「谁是唯一的设置持有者」——装配错了会让设置被重复读取、
/// 退出时互相覆盖，且不会立刻报错。因此这里把装配结果与写盘时机钉死。
/// </remarks>
public class AppServicesTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsPath;

    /// <summary>建立临时目录与设置文件路径。</summary>
    public AppServicesTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"appservices-{Guid.NewGuid():N}");
        _settingsPath = Path.Combine(_tempDirectory, "Settings.json");
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

    /// <summary>四个服务都必须装配完成，且导出链路复用同一个设置门面。</summary>
    [Fact]
    public void 装配全部服务()
    {
        using var services = new AppServices(_settingsPath);

        Assert.NotNull(services.Settings);
        Assert.NotNull(services.Ust);
        Assert.NotNull(services.ProjectIo);
        Assert.NotNull(services.VideoExporter);

        // 工程 IO 必须复用同一个设置门面：各自持有一份就会在导出时写出旧的配置。
        // 这里做行为验证（而不是断言内部字段）——改设置后导出，结果必须反映改动。
        services.Settings.Project.SongName = "装配验证";

        var exportPath = Path.Combine(_tempDirectory, "composed.uplr");
        services.ProjectIo.ExportUplr(exportPath);

        using var archive = System.IO.Compression.ZipFile.OpenRead(exportPath);
        var entry = archive.GetEntry(UstPlayer.Projects.UplrInfoJson.InfoFileName);
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open());
        Assert.Contains("装配验证", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    /// <summary>注入的设置路径被原样采用（测试与便携模式依赖这一点）。</summary>
    [Fact]
    public void 使用注入的设置路径()
    {
        using var services = new AppServices(_settingsPath);

        Assert.Equal(_settingsPath, services.Settings.SettingsPath);
    }

    /// <summary>设置只在释放时写回磁盘——构造时不应产生文件。</summary>
    [Fact]
    public void 构造时不写盘且释放时写回()
    {
        var services = new AppServices(_settingsPath);

        Assert.False(File.Exists(_settingsPath), "构造组合根不应写盘");

        services.Settings.Project.SongName = "测试曲名";
        services.Dispose();

        Assert.True(File.Exists(_settingsPath), "释放组合根应把设置写回磁盘");

        // 用一条新链路读回，确认确实落盘（而不是只改在内存里）
        var reloaded = new SettingsManager(_settingsPath);
        Assert.Equal("测试曲名", reloaded.Project.SongName);
    }

    /// <summary>重复释放是安全的（退出路径可能被调用两次）。</summary>
    [Fact]
    public void 重复释放安全()
    {
        var services = new AppServices(_settingsPath);

        services.Dispose();
        services.Dispose();

        // 文件内容仍然可读——重复释放没有把文件写坏
        var reloaded = new SettingsManager(_settingsPath);
        Assert.Equal(_settingsPath, reloaded.SettingsPath);
    }
}
