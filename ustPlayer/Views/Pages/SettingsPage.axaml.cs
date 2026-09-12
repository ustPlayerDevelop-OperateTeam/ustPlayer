using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using FluentAvalonia.Styling;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.Models;
using UstPlayer.ViewModels;

namespace UstPlayer.Views.Pages;

/// <summary>
/// 设置页（对应 1.1.x <c>other_page.py</c>）：主题 / 强调色 / 窗口效果 / 语言 / 缓存 / 日志 / 关于 / 协议。
/// </summary>
/// <remarks>
/// 行控件用的是 FluentAvalonia 自带的 <c>SettingsExpander</c>，页面只负责：
/// 文案、把设置变更**真的应用出去**（主题变体、强调色资源、窗口透明级别）、以及打开文件 / 网页。
/// </remarks>
internal sealed partial class SettingsPage : UserControl
{
    /// <summary>Bilibili 主页（与 1.1.x 同一个地址）。</summary>
    private const string BilibiliUrl = "https://space.bilibili.com/661930756";

    /// <summary>UtaFormatix 项目地址。</summary>
    private const string UtaFormatixUrl = "https://utaformatix.tk/";

    /// <summary>仓库地址。</summary>
    private const string GitHubUrl = "https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer";

    private readonly SettingsPageViewModel _viewModel;
    private readonly INotificationHost _notifications;

    /// <summary>创建设置页。</summary>
    /// <param name="viewModel">页面 ViewModel。</param>
    /// <param name="notifications">提示条宿主。</param>
    internal SettingsPage(SettingsPageViewModel viewModel, INotificationHost notifications)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notifications);

        _viewModel = viewModel;
        _notifications = notifications;

        InitializeComponent();
        DataContext = viewModel;
        ApplyTexts();

        // 版式自检开关：置 1 时显示一张由 SettingsRow 渲染的参照卡片，
        // 便于用 build/capture-window.ps1 截图比对两种卡片的边框与间距。
        AppearanceProbeRow.IsVisible =
            Environment.GetEnvironmentVariable("USTPLAYER_UI_PROBE") == "1";

        // 设置被外部改动（例如导入工程、或另一个入口切换主题）时同步刷新本页
        _viewModel.Theme.PropertyChanged += OnThemeChanged;
        _viewModel.Language.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>切换语言后刷新本页文案。</summary>
    internal void Retranslate()
    {
        _viewModel.Retranslate();
        ApplyTexts();
    }

    // ===================== 文案 =====================

    /// <summary>设置界面文案（UI 字符串一律经 <see cref="Translator.Tr"/>）。</summary>
    private void ApplyTexts()
    {
        AboutHeader.Text = Translator.Tr("关于软件");
        AboutRow.Header = Translator.Tr("关于软件");
        CopyrightButton.Content = $"{AppInfo.Name} - {AppInfo.Version} by {AppInfo.Author}";
        ToolTip.SetTip(CopyrightButton, Translator.Tr("点击访问 Bilibili 主页"));

        ToolsHeader.Text = Translator.Tr("外部工具与纠错");
        ErcodeRow.Header = Translator.Tr("外部工具与纠错");
        ErcodeButton.Content = Translator.Tr("ERcodes纠错");

        CacheHeader.Text = Translator.Tr("工程缓存");
        CacheRow.Header = SettingsPageViewModel.CacheRowHeader;
        ClearCacheButton.Content = Translator.Tr("清除缓存");

        LogHeader.Text = Translator.Tr("日志");
        LogRow.Header = Translator.Tr("打开应用运行日志");
        OpenLogButton.Content = Translator.Tr("打开日志");

        ThemeHeader.Text = Translator.Tr("主题");
        ThemeModeRow.Header = Label("应用主题:");
        AccentModeRow.Header = Label("强调色:");
        WindowEffectRow.Header = Label("窗口效果:");

        LanguageHeader.Text = Translator.Tr("语言 / Language");
        LanguageRow.Header = Label("界面语言:");

        LicenseHeader.Text = Translator.Tr("协议与许可");
        LicenseRow.Header = Translator.Tr("协议与许可");
        TermsButton.Content = Translator.Tr("开源协议");
        GitHubButton.Content = Translator.Tr("GitHub仓库");

        EasterEggText.Text = Translator.Tr("你知道吗：alpha版本在提交至托管时曾被错误地命名为ustPlyaer。orz");
    }

    /// <summary>行标题：取译文的正文部分（去掉结尾冒号）。</summary>
    /// <param name="source">翻译条目的中文原文（1.1.x 的表单标签带冒号）。</param>
    /// <returns>去掉结尾冒号的译文。</returns>
    private static string Label(string source) => Translator.Tr(source).TrimEnd('：', ':', ' ');

    // ===================== 设置应用 =====================

    /// <summary>主题 / 强调色设置变化 → 立即应用到界面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 只处理「用户在本页改动」；启动时的应用由 <c>MainWindow</c> 调
    /// <see cref="AppearanceController"/> 完成（否则保存过的强调色 / 窗口效果
    /// 下次启动不会生效）。
    /// </remarks>
    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Settings.Domains.ThemeSettings.ThemeMode):
                AppearanceController.ApplyThemeMode(_viewModel.Theme);
                break;
            case nameof(Settings.Domains.ThemeSettings.AccentColorMode):
                _viewModel.NotifyAccentModeChanged();
                AppearanceController.ApplyAccentColor(_viewModel.Theme);
                break;
            case nameof(Settings.Domains.ThemeSettings.CustomAccentColor):
                AppearanceController.ApplyAccentColor(_viewModel.Theme);
                break;
            case nameof(Settings.Domains.ThemeSettings.WindowEffect):
                if (TopLevel.GetTopLevel(this) is Window window)
                {
                    AppearanceController.ApplyWindowEffect(_viewModel.Theme, window);
                }

                break;
        }
    }

    /// <summary>语言变化 → 重装翻译并重译整个外壳。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.Domains.LanguageSettings.Language))
        {
            // 外壳自己监听 LanguageSettings 并重译；这里只需要刷新本页的候选文案
            _viewModel.Retranslate();
            ApplyTexts();
        }
    }

    // ===================== 交互 =====================

    /// <summary>打开作者主页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnOpenBilibiliClick(object? sender, RoutedEventArgs e) => OpenUrl(BilibiliUrl);

    /// <summary>打开 UtaFormatix。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnOpenUtaFormatixClick(object? sender, RoutedEventArgs e) => OpenUrl(UtaFormatixUrl);

    /// <summary>打开仓库主页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnOpenGitHubClick(object? sender, RoutedEventArgs e) => OpenUrl(GitHubUrl);

    /// <summary>打开 ERcode.txt。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnOpenErcodeClick(object? sender, RoutedEventArgs e) =>
        OpenProgramFile("ERcode.txt", "ERcode008", "打开ERcode.txt失败：{0}");

    /// <summary>打开开源协议。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnOpenTermsClick(object? sender, RoutedEventArgs e) =>
        OpenProgramFile("Terms.txt", "ERcode009", "打开LICENSE失败：{0}");

    /// <summary>打开应用日志。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        var path = SettingsPageViewModel.LogFilePath();

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.Error($"打开日志文件失败：{path}", exception);
            NotifyError("ERcode012", Translator.Tr("打开日志文件失败：{0}"), exception.Message);
        }
    }

    /// <summary>清空工程缓存（先确认）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnClearCacheClick(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmAsync(
            Translator.Tr("提示"),
            Translator.Tr("确定要清空工程缓存吗？"),
            // 两个按钮都复用既有译文（「清除缓存」/「取消」）：
            // 1.1.x 的确认框按钮由库提供，目录里没有「确定」这一条
            Translator.Tr("清除缓存"),
            Translator.Tr("取消"));

        if (!confirmed)
        {
            return;
        }

        try
        {
            _viewModel.ClearCache();
            _notifications.Notify(
                NotificationSeverity.Success,
                Translator.Tr("成功"),
                Translator.Tr("工程缓存已清除"));
        }
        catch (Exception exception)
        {
            AppLogger.Error("清除工程缓存失败", exception);

            // 不用 ERcode010/「导出失败：{0}」——那是导出路径的错误码与文案，借来会误导用户。
            // 目录里没有「缓存清除失败」这类原文，新增一条会让英/文言界面重新回退，
            // 因此这里只给通用标题 + 原始错误信息（细节在日志里）。
            _notifications.Notify(
                NotificationSeverity.Error,
                Translator.Tr("提示"),
                exception.Message);
        }
    }

    // ===================== 辅助 =====================

    /// <summary>用系统默认方式打开网址。</summary>
    /// <param name="url">网址。</param>
    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.Error($"打开网页失败：{url}", exception);
            NotifyError("ERcode003", Translator.Tr("打开网页失败：{0}"), exception.Message);
        }
    }

    /// <summary>打开程序目录下的文件（不存在时给出明确提示，而不是让系统报错）。</summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="messageSource">错误信息的中文原文（含 <c>{0}</c>）。</param>
    private void OpenProgramFile(string fileName, string errorCode, string messageSource)
    {
        var path = Path.Combine(_viewModel.ProgramRoot, fileName);

        if (!File.Exists(path))
        {
            AppLogger.Warning($"文件不存在：{path}");
            NotifyError(errorCode, messageSource, path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLogger.Error($"打开文件失败：{path}", exception);
            NotifyError(errorCode, messageSource, exception.Message);
        }
    }

    /// <summary>显示错误提示。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="messageSource">错误信息的中文原文（含 <c>{0}</c>）。</param>
    /// <param name="detail">细节。</param>
    private void NotifyError(string errorCode, string messageSource, string detail) =>
        _notifications.Notify(
            NotificationSeverity.Error,
            errorCode,
            string.Format(Translator.Tr(messageSource), detail));

    /// <summary>弹确认框。</summary>
    /// <param name="title">标题。</param>
    /// <param name="message">正文。</param>
    /// <param name="primary">主按钮文案。</param>
    /// <param name="close">取消按钮文案。</param>
    /// <returns>用户是否确认。</returns>
    /// <remarks>
    /// 直接用 Avalonia 的 <see cref="Window"/> 承载两个按钮：内容极少，
    /// 为此引入一个对话框基类不划算（1.1.x 用的是 MessageBox）。
    /// </remarks>
    private async System.Threading.Tasks.Task<bool> ConfirmAsync(
        string title,
        string message,
        string primary,
        string close)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            // 拿不到窗口（理论上不会发生）时按「不执行」处理，宁可不动也不能误删缓存
            return false;
        }

        var result = false;

        var confirm = new Button { Content = primary, IsDefault = true };
        var cancel = new Button { Content = close, IsCancel = true };

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, confirm },
                    },
                },
            },
        };

        confirm.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner);

        return result;
    }
}
