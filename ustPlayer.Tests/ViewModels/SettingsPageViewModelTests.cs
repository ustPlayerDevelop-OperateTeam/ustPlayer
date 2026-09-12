using System;
using System.IO;

using UstPlayer.Settings;
using UstPlayer.Settings.Domains;
using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 设置页 ViewModel 的测试。
/// </summary>
public class SettingsPageViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public SettingsPageViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"settingsvm-{Guid.NewGuid():N}");
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

    /// <summary>字节数格式化：单位换算与小数位的边界。</summary>
    /// <remarks>边界（0、刚好 1024、超过最大单位）最容易写错，因此逐条钉住。</remarks>
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void 字节数格式化(long bytes, string expected) =>
        Assert.Equal(expected, SettingsPageViewModel.FormatBytes(bytes));

    /// <summary>负数按 0 处理（缓存统计失败时不应显示负数）。</summary>
    [Fact]
    public void 负数字节按零处理() =>
        Assert.Equal("0 B", SettingsPageViewModel.FormatBytes(-1));

    /// <summary>四个下拉都必须把设置里的 key 投影成选中项，而不是把显示文案写回设置。</summary>
    [Fact]
    public void 下拉选中项来自设置的_Key()
    {
        using var services = CreateServices();
        services.Settings.Theme.ThemeMode = "dark";
        services.Settings.Theme.AccentColorMode = "custom";
        services.Settings.Theme.WindowEffect = "acrylic";
        services.Settings.Language.Language = "en_US";

        var viewModel = new SettingsPageViewModel(services);

        Assert.Equal("dark", viewModel.ThemeModeChoice.Selected.Key);
        Assert.Equal("custom", viewModel.AccentModeChoice.Selected.Key);
        Assert.Equal("acrylic", viewModel.WindowEffectChoice.Selected.Key);
        Assert.Equal("en_US", viewModel.LanguageChoice.Selected.Key);

        // 选中「自定义」时颜色选择器才显示
        Assert.True(viewModel.IsCustomAccent);
    }

    /// <summary>
    /// 非法 key 根本到不了下拉：设置层的 setter 就把它规范化为默认值。
    /// </summary>
    /// <remarks>
    /// 这条断言的是**真正的防线**。我原先假设 ChoiceGroup 会兜住非法 key，
    /// 实测发现 <c>ThemeSettings</c> 的 setter 已把它换成 <c>DefaultWindowEffect</c>（mica），
    /// 因此下拉永远拿到合法 key。两道防线都在，但起作用的是设置层这一道。
    /// </remarks>
    [Fact]
    public void 非法_key_在设置层就被规范化()
    {
        using var services = CreateServices();
        services.Settings.Theme.WindowEffect = "不存在的效果";

        Assert.Equal(SettingsEnums.DefaultWindowEffect, services.Settings.Theme.WindowEffect);

        var viewModel = new SettingsPageViewModel(services);

        // 规范化后的值必须是候选之一，否则下拉会没有选中项
        Assert.Contains(
            viewModel.WindowEffectChoice.Options,
            option => option.Key == services.Settings.Theme.WindowEffect);
    }

    /// <summary>切换强调色模式会更新「是否自定义」，供界面显示/隐藏颜色选择器。</summary>
    [Fact]
    public void 切换强调色模式刷新自定义标志()
    {
        using var services = CreateServices();
        var viewModel = new SettingsPageViewModel(services);

        services.Settings.Theme.AccentColorMode = "custom";
        viewModel.NotifyAccentModeChanged();
        Assert.True(viewModel.IsCustomAccent);

        services.Settings.Theme.AccentColorMode = "auto";
        viewModel.NotifyAccentModeChanged();
        Assert.False(viewModel.IsCustomAccent);
    }

    /// <summary>缓存占用文案非空且包含格式化后的用量。</summary>
    [Fact]
    public void 缓存占用文案已生成()
    {
        using var services = CreateServices();

        var viewModel = new SettingsPageViewModel(services);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.CacheUsageText));
        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UstPlayer.I18n.Translator.Tr("缓存占用：{0}"),
                SettingsPageViewModel.FormatBytes(services.ProjectIo.CacheUsage())),
            viewModel.CacheUsageText);
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
