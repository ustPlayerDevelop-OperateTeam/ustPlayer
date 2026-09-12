using System;

using Avalonia;
using Avalonia.Styling;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.Models;
using UstPlayer.Settings;

namespace UstPlayer.ViewModels;

/// <summary>
/// 主窗口 ViewModel — 外壳关注点：导航项、主题、强调色、语言。
/// </summary>
/// <remarks>
/// 页面自己的状态与命令不属于这里，见各页面的 ViewModel。
/// </remarks>
internal sealed class MainWindowViewModel : ViewModelBase
{
    /// <summary>主题模式：跟随系统。</summary>
    internal const string ThemeAuto = "auto";

    /// <summary>主题模式：亮色。</summary>
    internal const string ThemeLight = "light";

    /// <summary>主题模式：暗色。</summary>
    internal const string ThemeDark = "dark";

    private readonly AppServices _services;

    /// <summary>创建主窗口 ViewModel。</summary>
    /// <param name="services">组合根。</param>
    internal MainWindowViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>设置门面（页面直接绑定到各子域）。</summary>
    internal SettingsManager Settings => _services.Settings;

    /// <summary>窗口标题。</summary>
    internal static string Title => AppInfo.Name;

    /// <summary>
    /// 把设置里的主题模式应用到应用级主题变体。
    /// </summary>
    /// <remarks>
    /// <c>auto</c> 映射为 <see cref="ThemeVariant.Default"/>（由平台给系统主题），
    /// 而不是自己判断一次——跟随系统必须是持续的，用户改系统主题应立刻生效。
    /// </remarks>
    internal void ApplyTheme()
    {
        var mode = Settings.Theme.ThemeMode;

        var variant = mode switch
        {
            ThemeLight => ThemeVariant.Light,
            ThemeDark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = variant;
        }

        AppLogger.Info($"主题已应用：{mode}");
    }

    /// <summary>
    /// 按设置安装翻译（切换语言后调用）。
    /// </summary>
    /// <remarks>
    /// 未安装对应目录时 <see cref="Translator.Install"/> 内部回退，
    /// 因此这里不需要额外的错误处理。
    /// </remarks>
    internal void ApplyLanguage() =>
        Translator.Install(Settings.Language.EffectiveLanguage, Settings.ProgramRoot);
}

/// <summary>一个导航项。</summary>
/// <param name="Key">稳定键（用于调试与测试）。</param>
/// <param name="TitleSource">标题的中文原文（交给 <c>tr()</c> 翻译）。</param>
/// <param name="Page">页面控件。</param>
internal sealed record NavigationEntry(string Key, string TitleSource, object Page);
