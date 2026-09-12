using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 播放器样式页 ViewModel 的测试。
/// </summary>
/// <remarks>
/// 覆盖两类**纯逻辑**：① 四个枚举设置的 key ⇄ 中文原文映射（存储层契约）；
/// ② 字体槽位的取值规则（候选集合、哨兵项、导入路径）。界面是否同步不在这里测——
/// 颜色与自定义文字直接绑定设置子域，字体/下拉的“设置 → 界面”方向由无头页面测试覆盖。
/// </remarks>
public class PlayerStylePageViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public PlayerStylePageViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"stylevm-{Guid.NewGuid():N}");
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

    // ===================== 枚举映射 =====================

    /// <summary>四个下拉的 key 与中文原文必须与 1.1.x 逐项一致。</summary>
    [Fact]
    public void 下拉选项映射与_1_1_一致()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        Assert.Equal(
            new[] { ("top", "上"), ("bottom", "下") },
            KeysOf(viewModel.LyricPositionChoice));

        Assert.Equal(
            new[] { ("none", "无"), ("dash", "-"), ("custom", "自定义文字") },
            KeysOf(viewModel.PitchPlaceholderChoice));

        Assert.Equal(
            new[] { ("r", "R"), ("dash", "-"), ("custom", "自定义文字"), ("none", "什么都不显示") },
            KeysOf(viewModel.SilentDisplayChoice));

        Assert.Equal(
            new[] { ("end", "END"), ("dash", "-"), ("custom", "自定义文字"), ("none", "什么都不显示") },
            KeysOf(viewModel.EndDisplayChoice));
    }

    /// <summary>界面选中的 key 会写进设置（存的是英文 key，不是显示文案）。</summary>
    [Fact]
    public void 选中写回设置()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        viewModel.LyricPositionChoice.Selected = Option(viewModel.LyricPositionChoice, "bottom");
        viewModel.SilentDisplayChoice.Selected = Option(viewModel.SilentDisplayChoice, "none");
        viewModel.EndDisplayChoice.Selected = Option(viewModel.EndDisplayChoice, "dash");
        viewModel.PitchPlaceholderChoice.Selected = Option(viewModel.PitchPlaceholderChoice, "custom");

        Assert.Equal("bottom", services.Settings.Player.LyricPosition);
        Assert.Equal("none", services.Settings.Player.SilentDisplay);
        Assert.Equal("dash", services.Settings.Player.EndDisplay);
        Assert.Equal("custom", services.Settings.Player.PitchPlaceholder);
    }

    /// <summary>设置被外部改动（导入工程）时，下拉要能重读选中项并刷新「自定义」可见性。</summary>
    [Fact]
    public void 设置变化会通知下拉重读()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        AssertSelectionNotified(viewModel.LyricPositionChoice, key => services.Settings.Player.LyricPosition = key);
        AssertSelectionNotified(viewModel.SilentDisplayChoice, key => services.Settings.Player.SilentDisplay = key);
        AssertSelectionNotified(viewModel.EndDisplayChoice, key => services.Settings.Player.EndDisplay = key);
        AssertSelectionNotified(viewModel.PitchPlaceholderChoice, key => services.Settings.Player.PitchPlaceholder = key);
    }

    // ===================== 字体槽位 =====================

    /// <summary>默认候选 = 五个内置字体 + 「自定义…」入口，且入口在最后。</summary>
    [Fact]
    public void 默认字体候选()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        var expected = new List<string>(PlayerStylePageViewModel.BuiltinFontFamilies)
        {
            viewModel.CustomFontEntry,
        };

        Assert.Equal(expected, viewModel.FontFamilies);
    }

    /// <summary>合法的内置字体能写进设置，四个槽位彼此独立。</summary>
    [Fact]
    public void 四个字体槽位互相独立()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        viewModel.FontNote = "等线";
        viewModel.FontUstLyric = "黑体";
        viewModel.FontLrc = "楷体";
        viewModel.FontOther = "宋体";

        Assert.Equal("等线", services.Settings.Display.FontNote);
        Assert.Equal("黑体", services.Settings.Display.FontUstLyric);
        Assert.Equal("楷体", services.Settings.Display.FontLrc);
        Assert.Equal("宋体", services.Settings.Display.FontOther);
    }

    /// <summary>
    /// 空值、「自定义…」入口、不在候选里的字体族都不能写回设置。
    /// </summary>
    /// <remarks>
    /// 下拉框在候选集合变化时会短暂给出 <see langword="null"/>，而设置层的
    /// <c>CleanFont</c> 会把 null 变成空串——若放行，用户选的字体就被静默清掉了。
    /// </remarks>
    [Fact]
    public void 非法字体值不写回设置()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        services.Settings.Display.FontNote = "微软雅黑";

        viewModel.FontNote = string.Empty;
        viewModel.FontNote = "   ";
        viewModel.FontNote = null!;
        viewModel.FontNote = viewModel.CustomFontEntry;
        viewModel.FontNote = "本机没有的字体";

        Assert.Equal("微软雅黑", services.Settings.Display.FontNote);

        viewModel.FontNote = "黑体";

        Assert.Equal("黑体", services.Settings.Display.FontNote);
    }

    /// <summary>设置里的字体族不在候选里时，下拉回退到第一个内置字体，但设置值保持不变。</summary>
    [Fact]
    public void 候选外的字体族回退显示()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        services.Settings.Display.FontLrc = "Arial";

        Assert.Equal(PlayerStylePageViewModel.BuiltinFontFamilies[0], viewModel.FontLrc);
        Assert.Equal("Arial", services.Settings.Display.FontLrc);
    }

    /// <summary>导入字体：路径记入设置，族名（从文件读出）成为可选值并可写入槽位。</summary>
    [Fact]
    public void 导入字体文件后可选中该族名()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "custom.ttf", (1, "我的字体"));

        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        viewModel.RememberCustomFontPath(path);

        Assert.Equal([path], services.Settings.Display.CustomFontPaths);
        Assert.Contains("我的字体", viewModel.FontFamilies);

        // 「自定义…」入口必须仍在最后
        Assert.Equal(viewModel.CustomFontEntry, viewModel.FontFamilies[^1]);

        viewModel.FontNote = "我的字体";

        Assert.Equal("我的字体", services.Settings.Display.FontNote);
        Assert.Equal("我的字体", viewModel.FontNote);
    }

    /// <summary>同一个路径重复导入只记一次。</summary>
    [Fact]
    public void 重复导入同一路径只记一次()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "same.ttf", (1, "同名"));

        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        viewModel.RememberCustomFontPath(path);
        viewModel.RememberCustomFontPath(path);
        viewModel.RememberCustomFontPath(path);

        Assert.Equal([path], services.Settings.Display.CustomFontPaths);
        Assert.Single(viewModel.FontFamilies, family => family == "同名");
    }

    /// <summary>空路径不记入设置。</summary>
    [Fact]
    public void 空路径不记入设置()
    {
        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        viewModel.RememberCustomFontPath(string.Empty);
        viewModel.RememberCustomFontPath("   ");

        Assert.Empty(services.Settings.Display.CustomFontPaths);
    }

    /// <summary>
    /// 候选集合是**稳定实例**（不整体替换）：整体替换会让下拉选中项变 null，
    /// 双向绑定随即把 null 写回设置。这里锁住这个不变量。
    /// </summary>
    [Fact]
    public void 重建候选时不替换集合实例()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "stable.ttf", (1, "稳定字体"));

        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        var collection = viewModel.FontFamilies;

        viewModel.RememberCustomFontPath(path);
        viewModel.RefreshFontFamilies();

        Assert.Same(collection, viewModel.FontFamilies);
        Assert.Contains("稳定字体", collection);
    }

    /// <summary>导入工程（先写字体族、后写字体路径）后，槽位仍指向工程里的字体。</summary>
    [Fact]
    public void 先写族名后写路径也能选中()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "order.ttf", (1, "顺序字体"));

        using var services = CreateServices();
        var viewModel = new PlayerStylePageViewModel(services);

        services.Settings.Display.FontNote = "顺序字体";
        services.Settings.Display.CustomFontPaths = [path];

        Assert.Equal("顺序字体", viewModel.FontNote);
    }

    // ===================== 辅助 =====================

    /// <summary>改动设置后，该下拉应重读选中项并发出两条通知。</summary>
    /// <param name="group">下拉选项组。</param>
    /// <param name="write">写入设置的动作用（参数为要写入的 key）。</param>
    private static void AssertSelectionNotified(ChoiceGroup group, Action<string> write)
    {
        // 挑一个与当前值不同的候选，确保设置层真的会发出变更通知
        var target = group.Options.First(option => option.Key != group.Selected.Key).Key;
        var raised = new List<string?>();
        group.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        write(target);

        Assert.Contains(nameof(ChoiceGroup.Selected), raised);
        Assert.Contains(nameof(ChoiceGroup.IsCustom), raised);
        Assert.Equal(target, group.Selected.Key);
    }

    private static IEnumerable<(string Key, string SourceText)> KeysOf(ChoiceGroup group) =>
        group.Options.Select(option => (option.Key, option.SourceText));

    private static ChoiceOption Option(ChoiceGroup group, string key) =>
        group.Options.Single(option => option.Key == key);

    /// <summary>创建指向临时目录的组合根。</summary>
    /// <returns>组合根（调用方负责释放）。</returns>
    private AppServices CreateServices()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new AppServices(Path.Combine(root, "Settings.json"));
    }
}
