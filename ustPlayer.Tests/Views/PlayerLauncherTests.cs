using System;
using System.IO;
using System.Text;

using UstPlayer.Models;
using UstPlayer.Views;

using Xunit;

namespace UstPlayer.Tests.Views;

/// <summary>
/// 播放器启动门面（<see cref="PlayerLauncher"/>）的资源解析测试。
/// </summary>
/// <remarks>
/// 只覆盖**不涉及窗口**的纯逻辑：歌词怎么取、缺失时怎么降级、音频后端当前的行为。
/// 窗口本身无法在无头环境实例化（<c>AppWindow</c> 构造即建 Win32 窗口管理器，
/// 见 <c>docs/adr-0002-window-chrome.md</c>），因此由
/// <c>build/verify-app-launch.ps1</c> 用真实进程验证。
/// </remarks>
public class PlayerLauncherTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public PlayerLauncherTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"launcher-{Guid.NewGuid():N}");
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

    // ===================== 歌词解析 =====================

    /// <summary>未配置歌词路径时返回空列表（不是错误）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 未配置歌词时返回空列表(string? lrcPath)
    {
        var lines = PlayerLauncher.ResolveLrcLines(BuildParameters(lrcPath));

        Assert.Empty(lines);
    }

    /// <summary>歌词文件不存在时降级为空列表而不抛异常（用户删了文件不该让播放失败）。</summary>
    [Fact]
    public void 歌词文件缺失时降级()
    {
        var missing = Path.Combine(_tempDirectory, "不存在.lrc");

        var lines = PlayerLauncher.ResolveLrcLines(BuildParameters(missing));

        Assert.Empty(lines);
    }

    /// <summary>歌词文件存在时按时间戳升序解析（文件里的顺序无关）。</summary>
    [Fact]
    public void 歌词文件存在时按时间升序解析()
    {
        var path = Path.Combine(_tempDirectory, "test.lrc");
        File.WriteAllText(path, "[00:01.50]第二句\n[00:00.50]第一句", new UTF8Encoding(false));

        var lines = PlayerLauncher.ResolveLrcLines(BuildParameters(path));

        Assert.Equal(2, lines.Count);
        Assert.Equal(0.5, lines[0].TimestampSeconds, precision: 3);
        Assert.Equal("第一句", lines[0].Text);
        Assert.Equal(1.5, lines[1].TimestampSeconds, precision: 3);
        Assert.Equal("第二句", lines[1].Text);
    }

    // ===================== 音频后端 =====================

    /// <summary>
    /// 未配置伴奏、或伴奏文件不存在时返回空（播放器走墙钟计时）。
    /// </summary>
    /// <remarks>
    /// 这条断言原本是「音频后端尚未实现」的刻意标记，接入 LibVLCSharp 之后使命完成，
    /// 改成断言真实契约：**只有文件确实存在时才会去创建后端**。
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\music\不存在的伴奏.mp3")]
    public void 无可用伴奏时返回空(string? musicPath)
    {
        Assert.Null(PlayerLauncher.CreateAudioBackend(musicPath));
    }

    // ===================== 辅助 =====================

    private static PlayerLaunchParams BuildParameters(string? lrcPath) => new()
    {
        Ust = new UstInfo { Version = "UST Version1.2", Tempo = 120.0, Tracks = 1 },
        Show = new ShowConfig(),
        Project = new ProjectInfo(),
        Style = new PlayerStyle { LrcPath = lrcPath ?? string.Empty },
    };
}
