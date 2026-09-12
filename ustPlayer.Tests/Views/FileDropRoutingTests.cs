using System;
using System.IO;

using UstPlayer.Views;

using Xunit;

namespace UstPlayer.Tests.Views;

/// <summary>
/// 文件拖放路由的单元测试：验证「哪个页面接受哪类扩展名」与「一次拖放里挑哪个文件」。
/// </summary>
/// <remarks>
/// <para>
/// 真实拖放由操作系统的 OLE 通道驱动，**脚本里造不出来**，所以这里覆盖的是可判定的那一层：
/// 扩展名映射、存在性过滤、非法载荷容错。事件接线（写设置 / 导入工程 / 提示条）
/// 由 <see cref="MainWindowDropTests"/> 在无头窗口上用真实的 <c>DragEventArgs</c> 覆盖。
/// </para>
/// <para>
/// 对照基准是 1.1.x <c>main_window.py</c> 的 <c>_accepts_drag</c> / <c>dropEvent</c>。
/// </para>
/// </remarks>
public class FileDropRoutingTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public FileDropRoutingTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"droprouting-{Guid.NewGuid():N}");
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

    // ===================== 扩展名映射 =====================

    /// <summary>基础页只接受工程文件，且大小写不敏感。</summary>
    [Theory]
    [InlineData("工程.uplr")]
    [InlineData("工程.uprd")]
    [InlineData("工程.UPLR")]
    [InlineData(@"C:\某个目录\带 空格 的工程.uprd")]
    public void 基础页接受工程文件(string path) =>
        Assert.True(FileDropRouting.AcceptsExtension(MainWindow.BasicNavKey, path));

    /// <summary>基础页**不**接受别的页面的文件（往基础页拖 .ust 什么也不该发生）。</summary>
    [Theory]
    [InlineData("歌.ust")]
    [InlineData("词.lrc")]
    [InlineData("音频.wav")]
    [InlineData("")]
    public void 基础页拒绝其他文件(string? path) =>
        Assert.False(FileDropRouting.AcceptsExtension(MainWindow.BasicNavKey, path));

    /// <summary>文件页只接受 <c>.ust</c>。</summary>
    [Theory]
    [InlineData("歌.ust", true)]
    [InlineData("歌.UST", true)]
    [InlineData("歌.uplr", false)]
    [InlineData("歌.lrc", false)]
    [InlineData("歌.ustx", false)]
    public void 文件页只接受ust(string path, bool expected) =>
        Assert.Equal(expected, FileDropRouting.AcceptsExtension(MainWindow.FileNavKey, path));

    /// <summary>歌词页只接受 <c>.lrc</c>。</summary>
    [Theory]
    [InlineData("词.lrc", true)]
    [InlineData("词.LRC", true)]
    [InlineData("词.ust", false)]
    [InlineData("词.uplr", false)]
    public void 歌词页只接受lrc(string path, bool expected) =>
        Assert.Equal(expected, FileDropRouting.AcceptsExtension(MainWindow.LyricNavKey, path));

    /// <summary>播放器页 / 设置页 / 未知页面一概不接受拖放。</summary>
    [Theory]
    [InlineData(MainWindow.PlayerStyleNavKey)]
    [InlineData(MainWindow.SettingsNavKey)]
    [InlineData("还没有的页面")]
    [InlineData("")]
    [InlineData(null)]
    public void 其他页面不接受任何文件(string? navKey)
    {
        Assert.False(FileDropRouting.AcceptsExtension(navKey, "工程.uplr"));
        Assert.False(FileDropRouting.AcceptsExtension(navKey, "歌.ust"));
        Assert.False(FileDropRouting.AcceptsExtension(navKey, "词.lrc"));
    }

    /// <summary>必须整段后缀匹配：前缀相同或后缀里还有别的东西都不算。</summary>
    /// <remarks>
    /// 对应 1.1.x 的 <c>endswith</c> 语义——「a.uplrx」「a.uplr.bak」都不是工程文件，
    /// 而少了点号的「auplr」也不算。
    /// </remarks>
    [Theory]
    [InlineData("工程.uplrx")]
    [InlineData("工程.uplr.bak")]
    [InlineData("工程uplr")]
    [InlineData("uplr")]
    [InlineData(".uplr.txt")]
    public void 扩展名必须整段匹配(string path) =>
        Assert.False(FileDropRouting.AcceptsExtension(MainWindow.BasicNavKey, path));

    /// <summary>空文件名不会抛异常。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空文件名被拒绝(string? path) =>
        Assert.False(FileDropRouting.AcceptsExtension(MainWindow.BasicNavKey, path));

    // ===================== 拖动经过（只看扩展名） =====================

    /// <summary>
    /// 拖动经过时只看扩展名，**不看**文件是否存在——这时文件还没被交过来。
    /// </summary>
    [Fact]
    public void 拖动经过不看文件是否存在()
    {
        var missing = Path.Combine(_tempDirectory, "并不存在.uplr");

        Assert.True(FileDropRouting.AcceptsAny(MainWindow.BasicNavKey, [missing]));
        Assert.False(FileDropRouting.AcceptsAny(MainWindow.BasicNavKey, [Path.Combine(_tempDirectory, "并不存在.ust")]));
    }

    /// <summary>载荷里只要有一个匹配就算接受；空载荷 / 纯文本载荷一律不接受。</summary>
    [Fact]
    public void 拖动经过按任意一项判断()
    {
        Assert.True(FileDropRouting.AcceptsAny(MainWindow.BasicNavKey, [null, "笔记.txt", "工程.uprd"]));
        Assert.False(FileDropRouting.AcceptsAny(MainWindow.BasicNavKey, []));
        Assert.False(FileDropRouting.AcceptsAny(MainWindow.BasicNavKey, [null, "笔记.txt"]));
    }

    // ===================== 放下（还要真实存在） =====================

    /// <summary>放下时挑出**真实存在**的匹配文件。</summary>
    [Fact]
    public void 放下时挑出存在的文件()
    {
        var path = CreateFile("工程.uplr");

        Assert.Equal(path, FileDropRouting.Select(MainWindow.BasicNavKey, [path]));
    }

    /// <summary>文件在拖动期间被删掉：跳过而不是抛出。</summary>
    [Fact]
    public void 放下时跳过已不存在的文件()
    {
        var path = CreateFile("工程.uplr");
        File.Delete(path);

        Assert.Null(FileDropRouting.Select(MainWindow.BasicNavKey, [path]));
    }

    /// <summary>名字以 <c>.uplr</c> 结尾的**目录**不是文件，必须跳过。</summary>
    /// <remarks>
    /// 1.1.x 用的是 <c>os.path.exists</c>，目录会通过检查然后交给导入逻辑报错；
    /// 2.0 用 <c>File.Exists</c> 直接过滤掉，用户不会看到一个莫名其妙的失败提示。
    /// </remarks>
    [Fact]
    public void 放下时跳过目录()
    {
        var directory = Path.Combine(_tempDirectory, "伪装成工程.uplr");
        Directory.CreateDirectory(directory);

        Assert.Null(FileDropRouting.Select(MainWindow.BasicNavKey, [directory]));
    }

    /// <summary>返回第一个可处理的文件，跳过空项、不匹配项与不存在的项。</summary>
    [Fact]
    public void 放下时返回第一个可处理的文件()
    {
        var missing = Path.Combine(_tempDirectory, "不存在.uprd");
        var first = CreateFile("第一个.uplr");
        var second = CreateFile("第二个.uprd");

        Assert.Equal(
            first,
            FileDropRouting.Select(MainWindow.BasicNavKey, [null, "笔记.txt", missing, first, second]));
    }

    /// <summary>页面与扩展名不匹配时不挑任何文件（拖到基础页的 .ust 会被忽略）。</summary>
    [Fact]
    public void 放下时页面不匹配则不挑()
    {
        var ust = CreateFile("歌.ust");

        Assert.Null(FileDropRouting.Select(MainWindow.BasicNavKey, [ust]));
        Assert.Null(FileDropRouting.Select(MainWindow.PlayerStyleNavKey, [ust]));
    }

    /// <summary>空载荷 / 纯文本载荷不挑出任何文件，也不抛异常。</summary>
    [Fact]
    public void 放下时空载荷返回空()
    {
        Assert.Null(FileDropRouting.Select(MainWindow.BasicNavKey, []));
        Assert.Null(FileDropRouting.Select(MainWindow.BasicNavKey, [null]));
    }

    /// <summary>序列本身为 <see langword="null"/> 是编程错误，应显式抛出。</summary>
    [Fact]
    public void 载荷序列为空引用时抛出()
    {
        Assert.Throws<ArgumentNullException>(() => FileDropRouting.AcceptsAny(MainWindow.BasicNavKey, null!));
        Assert.Throws<ArgumentNullException>(() => FileDropRouting.Select(MainWindow.BasicNavKey, null!));
    }

    // ===================== 辅助 =====================

    /// <summary>在临时目录里造一个真实文件。</summary>
    /// <param name="name">文件名。</param>
    /// <returns>完整路径。</returns>
    private string CreateFile(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, "占位内容");

        return path;
    }
}
