using System;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

using FluentAvalonia.Styling;

using UstPlayer.Settings.Domains;
using UstPlayer.Views;

using Xunit;

namespace UstPlayer.Tests.Views;

/// <summary>
/// 外观应用（主题 / 强调色 / 窗口效果）的测试。
/// </summary>
/// <remarks>
/// 强调色是「点了看起来没反应」的高发区：设置层改对了、主题却没收到，
/// 界面上什么都不会发生。因此这里断言的是**主题里的真实资源**，
/// 而不是「我把值写进了设置」。
/// </remarks>
public class AppearanceControllerTests
{
    /// <summary>自定义强调色要真的落到主题上，并派生整套变体资源。</summary>
    [AvaloniaFact]
    public void 自定义强调色写进主题资源()
    {
        var settings = new ThemeSettings
        {
            AccentColorMode = "custom",
            CustomAccentColor = "#FF3B30",
        };

        AppearanceController.ApplyAccentColor(settings);

        var theme = FindTheme();

        Assert.Equal(Color.Parse("#FF3B30"), theme.CustomAccentColor);

        // 主题会把基础色派生到 SystemAccentColor 资源上（界面颜色最终读它）
        Assert.True(
            Application.Current!.TryGetResource("SystemAccentColor", null, out var accent),
            "主题里没有 SystemAccentColor 资源");

        Assert.Equal(Color.Parse("#FF3B30"), Assert.IsType<Color>(accent));
    }

    /// <summary>
    /// 「跟随系统」必须做两件事：解除自定义覆盖 + 打开「采用用户强调色」。
    /// </summary>
    /// <remarks>
    /// 只把 <c>CustomAccentColor</c> 置空是**不够的**：那只是「不再覆盖」，
    /// 若 <c>PreferUserAccentColor</c> 为假，系统强调色依然不会被采用，
    /// 表现为「选了跟随系统，颜色却没变回系统色」。
    /// </remarks>
    [AvaloniaFact]
    public void 跟随系统时同时打开用户强调色开关()
    {
        // 先设成自定义，确保这一步是在「解除覆盖」
        AppearanceController.ApplyAccentColor(new ThemeSettings
        {
            AccentColorMode = "custom",
            CustomAccentColor = "#123456",
        });

        var theme = FindTheme();
        Assert.NotNull(theme.CustomAccentColor);

        AppearanceController.ApplyAccentColor(new ThemeSettings { AccentColorMode = "auto" });

        Assert.Null(theme.CustomAccentColor);
        Assert.True(theme.PreferUserAccentColor);
    }

    /// <summary>非法颜色不该抛异常，也不该把主题改成乱七八糟的值。</summary>
    [AvaloniaFact]
    public void 非法强调色被忽略()
    {
        AppearanceController.ApplyAccentColor(new ThemeSettings
        {
            AccentColorMode = "custom",
            CustomAccentColor = "不是颜色",
        });

        // 走到这里就算通过：不能抛
        Assert.NotNull(FindTheme());
    }

    /// <summary>找应用里的 FluentAvaloniaTheme 实例。</summary>
    /// <returns>主题。</returns>
    private static FluentAvaloniaTheme FindTheme()
    {
        Assert.NotNull(Application.Current);

        foreach (var style in Application.Current!.Styles)
        {
            if (style is FluentAvaloniaTheme theme)
            {
                return theme;
            }
        }

        throw new InvalidOperationException("未找到 FluentAvaloniaTheme");
    }
}
