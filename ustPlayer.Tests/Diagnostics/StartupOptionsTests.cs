using UstPlayer.Diagnostics;

using Xunit;

namespace UstPlayer.Tests.Diagnostics;

/// <summary>
/// 命令行启动参数解析测试。
/// </summary>
/// <remarks>
/// 解析错了不会崩，只会让程序**悄悄**进了错误的模式（该播的没播、该开界面的没开），
/// 因此这几种写法都要钉死。
/// </remarks>
public class StartupOptionsTests
{
    /// <summary>无参数时是默认模式（打开主窗口）。</summary>
    [Fact]
    public void 无参数时进入默认模式()
    {
        Assert.False(StartupOptions.Parse([]).IsPlayerMode);
        Assert.False(StartupOptions.Parse(null).IsPlayerMode);
        Assert.Null(StartupOptions.Parse([]).PlayUstPath);
    }

    /// <summary><c>--play &lt;路径&gt;</c>（两个参数）被识别。</summary>
    [Fact]
    public void 分离写法被识别()
    {
        var options = StartupOptions.Parse(["--play", @"C:\song\test.ust"]);

        Assert.True(options.IsPlayerMode);
        Assert.Equal(@"C:\song\test.ust", options.PlayUstPath);
    }

    /// <summary><c>--play=&lt;路径&gt;</c>（一个参数）被识别，且路径里的等号不被截断。</summary>
    [Fact]
    public void 等号写法被识别()
    {
        var options = StartupOptions.Parse([@"--play=C:\my song\a=b.ust"]);

        Assert.True(options.IsPlayerMode);
        Assert.Equal(@"C:\my song\a=b.ust", options.PlayUstPath);
    }

    /// <summary>大小写不敏感（Windows 用户习惯随手写）。</summary>
    [Theory]
    [InlineData("--PLAY")]
    [InlineData("--Play")]
    public void 开关大小写不敏感(string switchName)
    {
        var options = StartupOptions.Parse([switchName, "test.ust"]);

        Assert.True(options.IsPlayerMode);
        Assert.Equal("test.ust", options.PlayUstPath);
    }

    /// <summary>无法识别的参数被忽略，不影响后续参数解析。</summary>
    [Fact]
    public void 忽略无法识别的参数()
    {
        var options = StartupOptions.Parse(["-psn_0_12345", "--unknown", "--play", "test.ust", "extra"]);

        Assert.True(options.IsPlayerMode);
        Assert.Equal("test.ust", options.PlayUstPath);
    }

    /// <summary>缺少路径时退回默认模式，而不是进入「路径为空」的播放模式。</summary>
    [Fact]
    public void 缺少路径时退回默认模式()
    {
        string[][] cases =
        [
            ["--play"],
            ["--play", ""],
            ["--play", "   "],
            ["--play="],
            ["--play=   "],
        ];

        foreach (var args in cases)
        {
            var options = StartupOptions.Parse(args);

            Assert.False(options.IsPlayerMode);
            Assert.Null(options.PlayUstPath);
        }
    }

    /// <summary>无参数时不开指定页（由主窗口决定用基础页）。</summary>
    [Fact]
    public void 无参数时不指定启动页()
    {
        Assert.Null(StartupOptions.Parse([]).PageKey);
        Assert.Null(StartupOptions.Parse(null).PageKey);
    }

    /// <summary><c>--page &lt;键&gt;</c> 与 <c>--page=&lt;键&gt;</c> 两种写法都被识别。</summary>
    [Theory]
    [InlineData("--page", "settings")]
    [InlineData("--page=settings", null)]
    public void 启动页两种写法被识别(string first, string? second)
    {
        var args = second is null ? new[] { first } : [first, second];
        var options = StartupOptions.Parse(args);

        Assert.Equal("settings", options.PageKey);
    }

    /// <summary>
    /// 取值为空（或漏写）时**不吞掉后面的开关**——否则 <c>--page --play song.ust</c>
    /// 会把 <c>--play</c> 当成页面键，播放模式静默失效。
    /// </summary>
    [Fact]
    public void 取值为空时不吞掉后续开关()
    {
        var options = StartupOptions.Parse(["--page", "--play", "test.ust"]);

        Assert.Null(options.PageKey);
        Assert.Equal("test.ust", options.PlayUstPath);

        var trailing = StartupOptions.Parse(["--play", "test.ust", "--page"]);

        Assert.Null(trailing.PageKey);
        Assert.Equal("test.ust", trailing.PlayUstPath);
    }

    /// <summary>两个开关可以同时出现（<c>--page</c> 用于脚本把窗口开到指定页）。</summary>
    [Fact]
    public void 启动页与播放开关可同时识别()
    {
        var leadingPage = StartupOptions.Parse(["--page", "lyric", "--play", "test.ust"]);
        Assert.Equal("lyric", leadingPage.PageKey);
        Assert.Equal("test.ust", leadingPage.PlayUstPath);

        var trailingPage = StartupOptions.Parse(["--play", "test.ust", "--page", "file"]);
        Assert.Equal("file", trailingPage.PageKey);
        Assert.Equal("test.ust", trailingPage.PlayUstPath);
    }
}
