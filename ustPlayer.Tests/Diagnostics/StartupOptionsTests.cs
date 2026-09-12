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
}
