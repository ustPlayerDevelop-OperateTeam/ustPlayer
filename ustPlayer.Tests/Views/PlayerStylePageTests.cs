using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

using UstPlayer.Settings.Domains;
using UstPlayer.Tests.ViewModels;
using UstPlayer.ViewModels;
using UstPlayer.Views;
using UstPlayer.Views.Pages;

using Xunit;

namespace UstPlayer.Tests.Views;

/// <summary>
/// 播放器样式页的无头界面测试：验证 XAML 能真实加载、绑定方向正确。
/// </summary>
/// <remarks>
/// <para>
/// 页面**不放进窗口、也不 Show**：FluentAvalonia 的控件模板（下拉箭头等）用内置的
/// <c>Symbols</c> 图标字体渲染，而 Avalonia 的无头字体管理器解析不了该字体，
/// 一旦触发渲染就会抛「Could not create glyphTypeface」。本测试只关心绑定语义，
/// 属性与绑定在构造 + 设置 DataContext 后即生效，不需要渲染。
/// </para>
/// <para>
/// 覆盖三条最容易写错的地方：颜色双向绑定、颜色的「输入框失焦才提交」、
/// 下拉选中写回稳定 key（而不是显示文案）。
/// </para>
/// </remarks>
public class PlayerStylePageTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public PlayerStylePageTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"stylepage-{Guid.NewGuid():N}");
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

    /// <summary>页面能构造，且颜色设置变化会推到输入框。</summary>
    [AvaloniaFact]
    public void 颜色设置变化推到界面()
    {
        using var services = CreateServices();
        var page = CreatePage(services, out _);

        var box = page.FindControl<TextBox>("BackgroundColorBox");

        Assert.NotNull(box);
        Assert.Equal(ColorSettings.DefaultBackgroundColor, box!.Text);

        services.Settings.Color.BackgroundColor = "#123456";

        Assert.Equal("#123456", box.Text);
    }

    /// <summary>
    /// 颜色输入框只在失焦时提交：中途输入（如「#12」）非法，
    /// 若每敲一键就写回设置，设置层会回退默认值并把用户正在输入的内容冲掉。
    /// </summary>
    [AvaloniaFact]
    public void 颜色输入框失焦才提交()
    {
        using var services = CreateServices();
        var page = CreatePage(services, out _);

        var box = page.FindControl<TextBox>("BackgroundColorBox");

        Assert.NotNull(box);

        // 非法中途值与合法值都不应该在失焦前写回设置
        box!.Text = "#12";
        Assert.Equal(ColorSettings.DefaultBackgroundColor, services.Settings.Color.BackgroundColor);

        box.Text = "#ABC123";
        Assert.Equal(ColorSettings.DefaultBackgroundColor, services.Settings.Color.BackgroundColor);

        box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

        Assert.Equal("#ABC123", services.Settings.Color.BackgroundColor);
    }

    /// <summary>下拉选中的是「key + 译文」的对象，写回设置的必须是 key。</summary>
    [AvaloniaFact]
    public void 歌词位置下拉写回稳定_key()
    {
        using var services = CreateServices();
        var page = CreatePage(services, out var viewModel);

        var combo = page.FindControl<ComboBox>("LyricPositionCombo");

        Assert.NotNull(combo);
        Assert.Equal("top", combo!.SelectedItem is ChoiceOption option ? option.Key : null);

        combo.SelectedItem = viewModel.LyricPositionChoice.Options.Single(item => item.Key == "bottom");

        Assert.Equal("bottom", services.Settings.Player.LyricPosition);

        // 反向：设置被外部改动（导入工程）后，界面跟着变
        services.Settings.Player.LyricPosition = "top";

        Assert.Equal("top", combo.SelectedItem is ChoiceOption updated ? updated.Key : null);
    }

    /// <summary>只有「自定义文字」时才显示配套输入框，且文本框双向绑定自定义文字。</summary>
    [AvaloniaFact]
    public void 自定义文本框随选项显示()
    {
        using var services = CreateServices();
        var page = CreatePage(services, out var viewModel);

        var combo = page.FindControl<ComboBox>("SilentDisplayCombo");
        var box = page.FindControl<TextBox>("SilentCustomBox");

        Assert.NotNull(combo);
        Assert.NotNull(box);
        Assert.False(box!.IsVisible);

        combo!.SelectedItem = viewModel.SilentDisplayChoice.Options.Single(item => item.Key == "custom");

        Assert.Equal("custom", services.Settings.Player.SilentDisplay);
        Assert.True(box.IsVisible);

        box.Text = "静音";

        Assert.Equal("静音", services.Settings.Player.SilentCustomText);
    }

    /// <summary>字体下拉的候选来自设置里的导入字体路径（族名由合成字体文件提供）。</summary>
    [AvaloniaFact]
    public void 字体下拉包含导入字体()
    {
        var path = SyntheticFontFile.Write(_tempDirectory, "page.ttf", (1, "页面字体"));

        using var services = CreateServices();
        services.Settings.Display.CustomFontPaths = [path];

        var page = CreatePage(services, out var viewModel);
        var combo = page.FindControl<ComboBox>("FontNoteCombo");

        Assert.NotNull(combo);
        Assert.Contains("页面字体", combo!.ItemsSource!.Cast<string>());
        Assert.EndsWith(
            viewModel.CustomFontEntry,
            combo.ItemsSource!.Cast<string>().Last(),
            StringComparison.Ordinal);

        combo.SelectedItem = "页面字体";

        Assert.Equal("页面字体", services.Settings.Display.FontNote);
    }

    /// <summary>
    /// 取色器与设置里的颜色字符串双向同步（只验证绑定 + 转换器，不渲染）。
    /// </summary>
    /// <remarks>
    /// 无头环境渲染不了 FluentAvalonia 的图标字体，因此这里不 Show 窗口；
    /// 绑定与转换器在构造后即生效，足以覆盖「取色器 → 设置」这条真实链路。
    /// </remarks>
    [AvaloniaFact]
    public void 取色器与设置双向同步()
    {
        using var services = CreateServices();
        var page = CreatePage(services, out _);

        var picker = page.FindControl<ColorPicker>("NoteColorPicker");

        Assert.NotNull(picker);
        Assert.Equal(Color.FromRgb(0x6C, 0x6C, 0x6C), picker!.Color);

        // 设置 → 取色器
        services.Settings.Color.NoteColor = "#123456";

        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), picker.Color);

        // 取色器 → 设置（写回的是 #RRGGBB 文本，而不是 Color）
        picker.Color = Color.FromRgb(0xAB, 0xCD, 0xEF);

        Assert.Equal("#ABCDEF", services.Settings.Color.NoteColor);
    }

    /// <summary>
    /// 取色器控件必须有 FluentAvalonia 提供的控件主题。
    /// </summary>
    /// <remarks>
    /// Avalonia 的取色器不在核心程序集里，而由 FluentAvalonia 依赖的
    /// <c>Avalonia.Controls.ColorPicker</c> 提供；主题一旦缺失，控件会静默显示成空白，
    /// 没有任何异常。无头环境无法真的渲染，因此这里验证**主题资源确实存在**。
    /// </remarks>
    [AvaloniaFact]
    public void 取色器有控件主题()
    {
        var application = Application.Current;

        Assert.NotNull(application);

        Assert.True(
            application!.TryFindResource(typeof(ColorPicker), out var pickerTheme),
            "主题里没有 ColorPicker 的 ControlTheme（取色器会渲染成空白）");

        Assert.IsType<ControlTheme>(pickerTheme);
    }

    /// <summary>
    /// 重译（切换语言）不能打断字体槽位的选中项。
    /// </summary>
    /// <remarks>
    /// 「自定义…」入口的文案随语言变化，重建候选集合时若把选中项一起清掉，
    /// 双向绑定会立刻把 <see langword="null"/> 写回设置，用户选的字体就被静默清空了。
    /// </remarks>
    [AvaloniaFact]
    public void 重译不打断字体选中项()
    {
        using var services = CreateServices();
        var page = CreatePage(services, out _);

        var combo = page.FindControl<ComboBox>("FontNoteCombo");

        Assert.NotNull(combo);

        combo!.SelectedItem = "黑体";

        Assert.Equal("黑体", services.Settings.Display.FontNote);

        page.Retranslate();

        Assert.Equal("黑体", combo.SelectedItem);
        Assert.Equal("黑体", services.Settings.Display.FontNote);
    }

    // ===================== 辅助 =====================

    private static PlayerStylePage CreatePage(AppServices services, out PlayerStylePageViewModel viewModel)
    {
        viewModel = new PlayerStylePageViewModel(services);

        return new PlayerStylePage(viewModel, new RecordingNotificationHost());
    }

    /// <summary>创建指向临时目录的组合根。</summary>
    /// <returns>组合根（调用方负责释放）。</returns>
    private AppServices CreateServices()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        return new AppServices(Path.Combine(root, "Settings.json"));
    }

    /// <summary>记录调用而不显示提示的宿主（页面因此无需真实窗口）。</summary>
    private sealed class RecordingNotificationHost : INotificationHost
    {
        /// <summary>收到的提示。</summary>
        internal List<(NotificationSeverity Severity, string Title, string Message)> Messages { get; } = [];

        /// <inheritdoc />
        public void Notify(NotificationSeverity severity, string title, string message) =>
            Messages.Add((severity, title, message));
    }
}
