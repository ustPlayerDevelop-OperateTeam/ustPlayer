using System;
using System.IO;
using System.Text;

using UstPlayer.Settings.Domains;
using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 文件页 ViewModel 的测试：编码规范化、预览与编码检查。
/// </summary>
public class FilePageViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public FilePageViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"filevm-{Guid.NewGuid():N}");
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

    // ===================== 编码规范化 =====================

    /// <summary>
    /// 各种编码写法都映射到候选之一。
    /// </summary>
    /// <remarks>
    /// 这条最关键：设置里的取值直接绑到 <c>ComboBox.SelectedItem</c>，
    /// 若不在候选列表里，SelectedItem 会变 null 并被双向绑定**写回设置**，
    /// 于是编码被清空、之后解析 UST 必然失败。
    /// </remarks>
    [Theory]
    [InlineData("UTF-8", "UTF-8")]
    [InlineData("utf-8", "UTF-8")]
    [InlineData("utf8", "UTF-8")]
    [InlineData("UTF_8", "UTF-8")]
    [InlineData("GBK", "GBK")]
    [InlineData("gbk", "GBK")]
    [InlineData("gb2312", "GBK")]
    [InlineData("GB18030", "GBK")]
    [InlineData("Shift-JIS", "Shift-JIS")]
    [InlineData("shift_jis", "Shift-JIS")]
    [InlineData("SHIFT-JIS", "Shift-JIS")]
    [InlineData("sjis", "Shift-JIS")]
    public void 编码写法被规范化(string input, string expected) =>
        Assert.Equal(expected, FilePageViewModel.Canonical(input));

    /// <summary>空值与无法识别的值回退默认编码（而不是留空）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cp932")]
    [InlineData("乱码编码")]
    public void 无法识别的编码回退默认(string? input) =>
        Assert.Equal(FileSettings.DefaultEncoding, FilePageViewModel.Canonical(input));

    /// <summary>构造时把设置里的编码就地规范化，保证下拉框一定有选中项。</summary>
    [Fact]
    public void 构造时规范化设置里的编码()
    {
        using var services = CreateServices();
        services.Settings.File.Encoding = "utf-8";

        var viewModel = new FilePageViewModel(services);

        Assert.Equal("UTF-8", viewModel.Encoding);
        Assert.Equal("UTF-8", services.Settings.File.Encoding);
    }

    /// <summary>写入编码也走规范化。</summary>
    [Theory]
    [InlineData("gb2312", "GBK")]
    [InlineData("sjis", "Shift-JIS")]
    [InlineData("nonsense", "Shift-JIS")]
    public void 写入编码也被规范化(string input, string expected)
    {
        using var services = CreateServices();
        var viewModel = new FilePageViewModel(services);

        viewModel.Encoding = input;

        Assert.Equal(expected, services.Settings.File.Encoding);
        Assert.Contains(services.Settings.File.Encoding, viewModel.SupportedEncodings);
    }

    // ===================== 预览 =====================

    /// <summary>路径为空时预览为空，且不抛异常。</summary>
    [Fact]
    public void 路径为空时预览为空()
    {
        using var services = CreateServices();
        var viewModel = new FilePageViewModel(services);

        Assert.Equal(string.Empty, viewModel.PreviewText);
    }

    /// <summary>文件不存在时清空预览——展示旧内容会误导用户。</summary>
    [Fact]
    public void 文件不存在时清空预览()
    {
        using var services = CreateServices();
        var viewModel = new FilePageViewModel(services);

        services.Settings.File.UstPath = Path.Combine(_tempDirectory, "不存在.ust");
        viewModel.RefreshPreview();

        Assert.Equal(string.Empty, viewModel.PreviewText);
    }

    /// <summary>路径有效时预览出文件内容。</summary>
    [Fact]
    public void 预览显示文件内容()
    {
        using var services = CreateServices();
        var viewModel = new FilePageViewModel(services);

        var path = WriteFile("a.ust", "[#VERSION]\nUST Version1.2\n", new UTF8Encoding(false));

        services.Settings.File.UstPath = path;
        services.Settings.File.Encoding = "UTF-8";
        viewModel.RefreshPreview();

        Assert.Contains("UST Version1.2", viewModel.PreviewText);
    }

    /// <summary>
    /// 编码不对时仍能预览（坏字节显示为替换符），但「编码检查」必须判为失败。
    /// </summary>
    /// <remarks>
    /// 这两条行为是配套的：预览要能**看到**内容，检查要给出**明确结论**。
    /// 若检查也走宽松读取，编码错误就永远发现不了。
    /// </remarks>
    [Fact]
    public void 编码不匹配时预览可用但检查失败()
    {
        using var services = CreateServices();
        var viewModel = new FilePageViewModel(services);

        // 用 Shift-JIS 写日文，再按 UTF-8 严格解码必然失败
        var path = WriteFile("jp.ust", "[#0000]\nLyric=あいう\n", Encoding.GetEncoding("Shift-JIS"));

        services.Settings.File.UstPath = path;
        services.Settings.File.Encoding = "UTF-8";

        viewModel.RefreshPreview();
        Assert.NotEqual(string.Empty, viewModel.PreviewText);
        Assert.False(viewModel.CheckEncoding());

        services.Settings.File.Encoding = "Shift-JIS";
        viewModel.RefreshPreview();

        Assert.Contains("あいう", viewModel.PreviewText);
        Assert.True(viewModel.CheckEncoding());
    }

    /// <summary>没选文件时「编码检查」判为失败（由 View 提示「请先选择」）。</summary>
    [Fact]
    public void 未选文件时检查失败()
    {
        using var services = CreateServices();
        var viewModel = new FilePageViewModel(services);

        Assert.False(viewModel.CheckEncoding());
    }

    // ===================== 辅助 =====================

    /// <summary>创建指向临时目录的组合根。</summary>
    /// <returns>组合根（调用方负责释放）。</returns>
    private AppServices CreateServices()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new AppServices(Path.Combine(root, "Settings.json"));
    }

    /// <summary>写入一个测试文件。</summary>
    /// <param name="name">文件名。</param>
    /// <param name="content">内容。</param>
    /// <param name="encoding">编码。</param>
    /// <returns>文件路径。</returns>
    private string WriteFile(string name, string content, Encoding encoding)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, content, encoding);

        return path;
    }
}
