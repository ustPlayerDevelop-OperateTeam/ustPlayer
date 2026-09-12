using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using UstPlayer.Views.Controls;

using Xunit;

namespace UstPlayer.Tests.Views;

/// <summary>
/// 设置卡片版式（每一项设置各自一张独立卡片）的机制测试。
/// </summary>
/// <remarks>
/// 这些性质只有「看一眼界面」才能发现坏掉，因此用测试钉住：
/// 两个控件的控件主题都能被解析到（写错 x:Key 会导致界面**空白但不报错**），
/// 以及分组标题可为空。
/// </remarks>
public class SettingsControlsTests
{
    /// <summary>两个控件的 ControlTheme 都必须能在应用资源里解析到。</summary>
    /// <remarks>
    /// <c>x:Key="{x:Type ...}"</c> 或 TargetType 写错时，控件会退化成「没有模板」——
    /// 界面上什么都不显示，但不会抛异常。这条断言把那种静默失败挡住。
    /// 直接查应用资源（而不是挂到可视树上）是因为无头环境下窗口没有平台实现，
    /// 样式不会应用，只能查资源本身。
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(typeof(SettingsRow))]
    [InlineData(typeof(SettingsSection))]
    public void 控件主题可解析(Type controlType)
    {
        Assert.NotNull(Application.Current);

        var found = Application.Current!.TryGetResource(controlType, null, out var theme);

        Assert.True(found, $"{controlType.Name} 未找到 ControlTheme，控件会渲染成空白");
        Assert.IsType<ControlTheme>(theme);
    }

    /// <summary>分组标题为空时不占高度（模板里按 IsVisible 折叠）。</summary>
    [AvaloniaFact]
    public void 分组标题可空()
    {
        var section = new SettingsSection { Content = new StackPanel() };
        var window = new Window { Content = section };

        window.Measure(new Size(400, 300));

        Assert.Null(section.Header);
    }

    /// <summary>设置行可以承载任意右侧控件，且标题/说明可空。</summary>
    [AvaloniaFact]
    public void 设置行可承载控件()
    {
        var toggle = new ToggleSwitch();
        var row = new SettingsRow { Header = "显示BPM", Content = toggle };
        var section = new SettingsSection { Header = "显示选项", Content = new StackPanel() };

        ((StackPanel)section.Content!).Children.Add(row);

        var window = new Window { Content = section };
        window.Measure(new Size(400, 300));

        Assert.Equal("显示BPM", row.Header);
        Assert.Null(row.Description);
        Assert.Same(toggle, row.Content);
    }
}
