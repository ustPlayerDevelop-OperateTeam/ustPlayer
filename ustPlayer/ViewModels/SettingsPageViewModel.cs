using System;
using System.Collections.Generic;
using System.Globalization;

using UstPlayer.Diagnostics;
using UstPlayer.Settings.Domains;

namespace UstPlayer.ViewModels;

/// <summary>
/// 设置页 ViewModel（对应 1.1.x <c>other_page.py</c>）：
/// 主题 / 强调色 / 窗口效果 / 语言 / 工程缓存 / 日志 / 关于 / 协议。
/// </summary>
/// <remarks>
/// <para>
/// 1.1.x 里这一页叫「其他」，2.0 起改名「设置」——它的内容本来就是应用设置，
/// 放在导航底部作为设置入口更符合直觉（与 Fluent 应用的惯例一致）。
/// </para>
/// <para>
/// 四个下拉都用 <see cref="ChoiceGroup"/> 做「显示译文、存稳定 key」的绑定投影，
/// 因此存储层里始终是英文 key（见 <c>SettingsEnums</c>），切语言只换文案。
/// </para>
/// </remarks>
internal sealed class SettingsPageViewModel : ViewModelBase
{
    /// <summary>缓存占用文案的格式（与 1.1.x 同一句原文）。</summary>
    private const string CacheUsageFormat = "缓存占用：{0}";

    private readonly AppServices _services;
    private readonly List<ChoiceGroup> _choices;

    private string _cacheUsageText = string.Empty;

    /// <summary>创建设置页 ViewModel。</summary>
    /// <param name="services">组合根。</param>
    internal SettingsPageViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;

        ThemeModeChoice = new ChoiceGroup(
            () => Theme.ThemeMode,
            value => Theme.ThemeMode = value,
            ("auto", "跟随系统"),
            ("light", "亮色"),
            ("dark", "暗色"));

        AccentModeChoice = new ChoiceGroup(
            () => Theme.AccentColorMode,
            value => Theme.AccentColorMode = value,
            ("auto", "跟随系统"),
            ("custom", "自定义"));

        WindowEffectChoice = new ChoiceGroup(
            () => Theme.WindowEffect,
            value => Theme.WindowEffect = value,
            ("none", "关闭"),
            ("acrylic", "亚克力"),
            ("mica", "Mica"));

        // 语言候选与 1.1.x 一致：跟随系统 + 三个可用语言。
        // 刻意不扫描 i18n 目录：目录缺失时列表会静默变空，反而更难排查。
        LanguageChoice = new ChoiceGroup(
            () => Language.Language,
            value => Language.Language = value,
            (LanguageSettings.ModeSystem, "跟随系统"),
            ("zh_CN", "简体中文"),
            ("zh_classic", "文言（华夏）"),
            ("en_US", "English"));

        _choices = [ThemeModeChoice, AccentModeChoice, WindowEffectChoice, LanguageChoice];

        RefreshCacheUsage();
    }

    /// <summary>主题子域。</summary>
    internal ThemeSettings Theme => _services.Settings.Theme;

    /// <summary>语言子域。</summary>
    internal LanguageSettings Language => _services.Settings.Language;

    /// <summary>程序根目录（ERcode.txt / Terms.txt 等随程序分发的文件都在这里）。</summary>
    internal string ProgramRoot => _services.Settings.ProgramRoot;

    /// <summary>
    /// 缓存行的标题。
    /// </summary>
    /// <remarks>
    /// 复用「缓存占用：{0}」这一条既有译文并去掉占位符，而不是新写一条「缓存占用」——
    /// 新原文不在 161 条译文里，英文 / 文言界面会静默退回中文。
    /// </remarks>
    internal static string CacheRowHeader =>
        TranslatorText(CacheUsageFormat).Replace("{0}", string.Empty).TrimEnd('：', ':', ' ');

    /// <summary>应用主题候选。</summary>
    internal ChoiceGroup ThemeModeChoice { get; }

    /// <summary>强调色模式候选。</summary>
    internal ChoiceGroup AccentModeChoice { get; }

    /// <summary>窗口效果候选。</summary>
    internal ChoiceGroup WindowEffectChoice { get; }

    /// <summary>界面语言候选。</summary>
    internal ChoiceGroup LanguageChoice { get; }

    /// <summary>是否处于「自定义强调色」，用于显示颜色选择器。</summary>
    internal bool IsCustomAccent => AccentModeChoice.IsCustom;

    /// <summary>自定义强调色（<c>#RRGGBB</c>）。</summary>
    internal string CustomAccentColor
    {
        get => Theme.CustomAccentColor;
        set => Theme.CustomAccentColor = value;
    }

    /// <summary>缓存占用文案（已翻译）。</summary>
    internal string CacheUsageText
    {
        get => _cacheUsageText;
        private set => SetProperty(ref _cacheUsageText, value);
    }

    /// <summary>重算并刷新缓存占用文案。</summary>
    internal void RefreshCacheUsage() =>
        CacheUsageText = string.Format(
            CultureInfo.CurrentCulture,
            TranslatorText(CacheUsageFormat),
            FormatBytes(_services.ProjectIo.CacheUsage()));

    /// <summary>清空工程缓存。</summary>
    internal void ClearCache()
    {
        _services.ProjectIo.ClearCache();
        RefreshCacheUsage();
    }

    /// <summary>打开应用日志文件。</summary>
    /// <returns>日志文件路径。</returns>
    internal static string LogFilePath() => AppLogger.LogFilePath;

    /// <summary>语言或文案变化后刷新候选与文案。</summary>
    internal void Retranslate()
    {
        foreach (var choice in _choices)
        {
            choice.Retranslate();
        }

        RefreshCacheUsage();
        OnPropertyChanged(nameof(IsCustomAccent));
    }

    /// <summary>强调色模式变化后刷新「是否自定义」。</summary>
    internal void NotifyAccentModeChanged()
    {
        AccentModeChoice.NotifySelectionChanged();
        OnPropertyChanged(nameof(IsCustomAccent));
    }

    /// <summary>
    /// 把字节数格式化为可读文本（对应 1.1.x 的 <c>_fmt_bytes</c>）。
    /// </summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>如 <c>1.5 MB</c>。</returns>
    /// <remarks>单独抽出来是为了能单测——边界（0、刚好 1024、超大）容易写错。</remarks>
    internal static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // 字节数不带小数（"512 B" 而不是 "512.0 B"）
        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }

    /// <summary>取译文（包一层便于本类静态方法使用）。</summary>
    /// <param name="source">中文原文。</param>
    /// <returns>译文。</returns>
    private static string TranslatorText(string source) => I18n.Translator.Tr(source);
}
