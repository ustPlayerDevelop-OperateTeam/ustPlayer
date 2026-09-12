using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using FluentAvalonia.Styling;

using UstPlayer.Diagnostics;
using UstPlayer.Settings.Domains;

namespace UstPlayer.Views;

/// <summary>
/// 外观应用：把主题 / 强调色 / 窗口效果三项设置真正作用到界面上。
/// </summary>
/// <remarks>
/// <para>
/// 单独抽出来是因为它有**两个调用点**：应用启动时（把上次保存的设置应用出来）
/// 与用户在设置页改动时。只写在设置页里会导致「保存过的强调色 / 窗口效果
/// 下次启动不生效，非得动一下下拉才行」——这正是最初的实现漏掉的一环。
/// </para>
/// <para>
/// 主题变体另有 <c>MainWindowViewModel.ApplyTheme</c> 在启动时调用；
/// 这里再提供一份是为了让三者的映射集中在一处，谁调用都得到同样的结果。
/// </para>
/// </remarks>
internal static class AppearanceController
{
    /// <summary>把主题模式应用到应用级主题变体。</summary>
    /// <param name="theme">主题子域。</param>
    /// <remarks>
    /// <c>auto</c> 映射为 <see cref="ThemeVariant.Default"/>（由平台给系统主题）——
    /// 自己判断一次系统主题会在用户改系统设置后失效。
    /// </remarks>
    internal static void ApplyThemeMode(ThemeSettings theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var variant = theme.ThemeMode switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = variant;
        }

        AppLogger.Info($"主题已应用：{theme.ThemeMode}");
    }

    /// <summary>
    /// 应用强调色。
    /// </summary>
    /// <param name="theme">主题子域。</param>
    /// <remarks>
    /// 走 <see cref="FluentAvaloniaTheme.CustomAccentColor"/>，**不要**自己去覆盖
    /// <c>SystemAccentColor</c> 资源：主题会由强调色派生出一整套明暗变体
    /// （Light1..3 / Dark1..3），只换基础色会让按钮、选中态等处的强调色对不上。
    /// 传 <see langword="null"/> 即恢复跟随系统。
    /// </remarks>
    internal static void ApplyAccentColor(ThemeSettings theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (FindFluentTheme() is not { } fluentTheme)
        {
            AppLogger.Warning("未找到 FluentAvaloniaTheme，强调色未应用");
            return;
        }

        if (theme.AccentColorMode != "custom")
        {
            // 两步缺一不可：CustomAccentColor 为 null 只是「不再覆盖」，
            // 还要把 PreferUserAccentColor 打开，系统强调色才会真的被采用
            //（该属性即「是否用当前用户的强调色作为 SystemAccentColor」）。
            fluentTheme.PreferUserAccentColor = true;
            fluentTheme.CustomAccentColor = null;

            AppLogger.Info("强调色跟随系统");
            return;
        }

        if (!Color.TryParse(theme.CustomAccentColor, out var color))
        {
            AppLogger.Warning($"强调色格式非法，忽略：{theme.CustomAccentColor}");
            return;
        }

        fluentTheme.CustomAccentColor = color;
        AppLogger.Info($"强调色已应用（自定义）：{theme.CustomAccentColor}");
    }

    /// <summary>
    /// 应用窗口效果（Mica / 亚克力 / 关闭）。
    /// </summary>
    /// <param name="theme">主题子域。</param>
    /// <param name="window">目标窗口。</param>
    /// <remarks>
    /// <para>
    /// 用 Avalonia 的 <see cref="Window.TransparencyLevelHint"/>。平台支持程度不一
    /// （Linux 上常常完全不支持），因此只记录**实际生效**的级别。
    /// </para>
    /// <para>
    /// 背景要等提示生效后再决定：无条件设为透明，在不支持模糊的平台上会得到
    /// 「全透明、看不清内容」的窗口。因此设置提示后等一帧，只在实际拿到非
    /// <c>None</c> 级别时才透明，否则用 <c>ClearValue</c> **交回主题默认背景**
    /// （注意赋 <see langword="null"/> 是设了一个本地空值，不等于恢复主题背景）。
    /// </para>
    /// </remarks>
    internal static void ApplyWindowEffect(ThemeSettings theme, Window window)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(window);

        var mode = theme.WindowEffect;

        window.TransparencyLevelHint = mode switch
        {
            "mica" => [WindowTransparencyLevel.Mica],
            "acrylic" => [WindowTransparencyLevel.AcrylicBlur],
            _ => [WindowTransparencyLevel.None],
        };

        Dispatcher.UIThread.Post(
            () =>
            {
                var effective = window.ActualTransparencyLevel != WindowTransparencyLevel.None;

                if (effective)
                {
                    window.Background = Brushes.Transparent;
                }
                else
                {
                    // 恢复主题背景，而不是留一个本地 null
                    window.ClearValue(Window.BackgroundProperty);
                }

                AppLogger.Info(
                    $"窗口效果：请求={mode} 实际={window.ActualTransparencyLevel}"
                    + (effective ? string.Empty : "（平台不支持，已回退不透明背景）"));
            },
            DispatcherPriority.Background);
    }

    /// <summary>取应用里的 <see cref="FluentAvaloniaTheme"/> 实例。</summary>
    /// <returns>主题；未找到时返回 <see langword="null"/>。</returns>
    private static FluentAvaloniaTheme? FindFluentTheme() =>
        Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
}
