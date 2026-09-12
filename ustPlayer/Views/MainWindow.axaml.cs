using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;

using FluentAvalonia.UI.Controls;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.Interop;
using UstPlayer.ViewModels;
using UstPlayer.Views.Pages;

namespace UstPlayer.Views;

/// <summary>
/// 主窗口 — FluentAvalonia 的 <see cref="NavigationView"/> 外壳（对应 1.1.x 的 <c>FluentWindow</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 本类只负责外壳：导航、页面装配、提示条、播放器启动（需要窗口所有权）与语言切换。
/// 各页面的状态与逻辑在其 ViewModel 里。
/// </para>
/// <para>
/// 与 1.1.x 的差异：1.1.x 每个页面都要在切换时调用 <c>sync_all_from_settings()</c> 手工回填控件；
/// 2.0 的控件直接绑定设置子域（它们会通知变更），因此切换页面**不需要**任何同步动作。
/// </para>
/// </remarks>
internal sealed partial class MainWindow : ShellWindow, INotificationHost
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _viewModel;

    /// <summary>导航键 → 导航项（语言切换后重设标题用）。</summary>
    private readonly Dictionary<string, NavigationViewItem> _navItems = [];

    /// <summary>导航键 → 标题的中文原文。</summary>
    private readonly Dictionary<string, string> _navTitleSources = [];

    /// <summary>导航键 → 页面实例（尚未迁移的页面不在其中）。</summary>
    private readonly Dictionary<string, Control> _pages = [];

    private BasicPage? _basicPage;
    private FilePage? _filePage;
    private PlayerStylePage? _playerStylePage;
    private LyricPage? _lyricPage;

    /// <summary>当前打开的播放窗口；用于避免同时打开多个全屏播放器。</summary>
    private PlayerWindow? _playerWindow;

    /// <summary>构造主窗口。</summary>
    /// <param name="services">组合根。</param>
    public MainWindow(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _viewModel = new MainWindowViewModel(services);

        InitializeComponent();

        _viewModel.ApplyTheme();
        _viewModel.ApplyLanguage();

        BuildPages();
        BuildNavigation();

        // 语言偏好变更后立即重译整个外壳（1.1.x 走 language_changed 信号）
        _services.Settings.Language.PropertyChanged += OnLanguageSettingsChanged;

        NavView.SelectedItem = _navItems["basic"];

        AppLogger.Info($"主窗口就绪（设置文件：{_services.Settings.SettingsPath}）");
    }

    // ===================== 提示条 =====================

    /// <inheritdoc />
    public void Notify(NotificationSeverity severity, string title, string message)
    {
        NotificationBar.Severity = severity switch
        {
            NotificationSeverity.Success => InfoBarSeverity.Success,
            NotificationSeverity.Warning => InfoBarSeverity.Warning,
            NotificationSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };

        NotificationBar.Title = title;
        NotificationBar.Message = message;
        NotificationBar.IsOpen = true;
    }

    // ===================== 装配 =====================

    /// <summary>创建全部页面。</summary>
    private void BuildPages()
    {
        _basicPage = new BasicPage(new BasicPageViewModel(_services), this);
        _basicPage.SetPlayHandler(PlayAsync);
        _pages["basic"] = _basicPage;

        _filePage = new FilePage(new FilePageViewModel(_services), this);
        _pages["file"] = _filePage;

        _playerStylePage = new PlayerStylePage(new PlayerStylePageViewModel(_services), this);
        _pages["player_style"] = _playerStylePage;

        _lyricPage = new LyricPage(new LyricPageViewModel(_services), this);
        _pages["lyric"] = _lyricPage;
    }

    /// <summary>
    /// 创建导航项并接线切换。
    /// </summary>
    /// <remarks>
    /// 顺序与 1.1.x 一致：「其他」固定在底部（<see cref="NavigationView.FooterMenuItems"/>）。
    /// 图标只能用 FluentAvalonia <c>Symbol</c> 枚举里真实存在的成员——
    /// 该枚举没有 <c>Palette</c> / <c>Music</c> / <c>Info</c>（已实测转储确认），
    /// 用不存在的名字会直接编译失败。
    /// </remarks>
    private void BuildNavigation()
    {
        AddNavItem("basic", "基础", Symbol.Home, footer: false);
        AddNavItem("file", "文件", Symbol.Document, footer: false);
        AddNavItem("player_style", "播放器", Symbol.ColorFill, footer: false);
        AddNavItem("lyric", "歌词", Symbol.Audio, footer: false);
        AddNavItem("other", "其他", Symbol.Important, footer: true);

        NavView.SelectionChanged += OnNavigationSelectionChanged;
    }

    /// <summary>添加一个导航项。</summary>
    /// <param name="key">稳定键。</param>
    /// <param name="titleSource">标题的中文原文。</param>
    /// <param name="symbol">图标。</param>
    /// <param name="footer">是否放在底部。</param>
    private void AddNavItem(string key, string titleSource, Symbol symbol, bool footer)
    {
        var item = new NavigationViewItem
        {
            Content = Translator.Tr(titleSource),
            Tag = key,
            IconSource = new SymbolIconSource { Symbol = symbol },
            SelectsOnInvoked = true,
        };

        _navItems[key] = item;
        _navTitleSources[key] = titleSource;

        if (footer)
        {
            NavView.FooterMenuItems.Add(item);
        }
        else
        {
            NavView.MenuItems.Add(item);
        }
    }

    /// <summary>导航选中变化 → 切换页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem { Tag: string key })
        {
            ShowPage(key);
        }
    }

    /// <summary>把指定页面设为导航区内容。</summary>
    /// <param name="key">导航键。</param>
    /// <remarks>
    /// 尚未迁移的页面给一个占位提示，避免点进去是空白让人以为坏了。
    /// </remarks>
    private void ShowPage(string key)
    {
        if (_pages.TryGetValue(key, out var page))
        {
            NavView.Content = page;
            return;
        }

        NavView.Content = new TextBlock
        {
            Text = Translator.Tr("该页面尚未迁移（Phase 5 进行中）"),
            Margin = new Thickness(24),
            Opacity = 0.6,
        };
    }

    // ===================== 语言 =====================

    /// <summary>语言设置变更 → 重装翻译并重译界面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnLanguageSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Settings.Domains.LanguageSettings.Language))
        {
            return;
        }

        _viewModel.ApplyLanguage();
        Retranslate();
    }

    /// <summary>重译外壳与全部页面文案。</summary>
    private void Retranslate()
    {
        foreach (var (key, item) in _navItems)
        {
            item.Content = Translator.Tr(_navTitleSources[key]);
        }

        _basicPage?.Retranslate();
        _filePage?.Retranslate();
        _playerStylePage?.Retranslate();
        _lyricPage?.Retranslate();

        if (NavView.Content is TextBlock placeholder)
        {
            placeholder.Text = Translator.Tr("该页面尚未迁移（Phase 5 进行中）");
        }
    }

    // ===================== 播放 =====================

    /// <summary>
    /// 校验并启动全屏播放器（对应 1.1.x 主窗口的 <c>_on_play</c>）。
    /// </summary>
    /// <returns>任务。</returns>
    private Task PlayAsync()
    {
        var ustPath = _services.Settings.File.UstPath.Trim();

        if (ustPath.Length == 0 || !File.Exists(ustPath))
        {
            AppLogger.Warning($"UST 文件无效：{ustPath}");
            Notify(NotificationSeverity.Error, "ERcode001", Translator.Tr("请选择有效的UST文件！"));

            return Task.CompletedTask;
        }

        // 全屏播放器是置顶窗口，叠开两个会互相抢焦点且各自跑一条帧循环
        if (_playerWindow is { IsVisible: true })
        {
            Notify(
                NotificationSeverity.Warning,
                Translator.Tr("提示"),
                Translator.Tr("播放器已打开，请先按 Esc 退出当前播放"));

            return Task.CompletedTask;
        }

        try
        {
            var ustInfo = _services.Ust.Parse(ustPath, _services.Settings.File.Encoding);

            if (ustInfo.Notes.Count == 0)
            {
                Notify(
                    NotificationSeverity.Error,
                    "ERcode001",
                    Translator.Tr("该 UST 中没有音符，无法播放"));

                return Task.CompletedTask;
            }

            AppLogger.Info($"开始播放：{ustPath}（音符 {ustInfo.Notes.Count} 个）");
            _playerWindow = PlayerLauncher.Launch(_services.Settings, ustInfo);
        }
        catch (RendererException exception)
        {
            AppLogger.Error("播放器启动失败：渲染器不可用", exception);
            Notify(
                NotificationSeverity.Error,
                "ERcode005",
                string.Format(Translator.Tr("播放器启动失败：{0}"), exception.Message));
        }
        catch (Exception exception)
        {
            AppLogger.Error("播放准备失败", exception);
            Notify(
                NotificationSeverity.Error,
                "ERcode999",
                string.Format(Translator.Tr("播放准备失败：{0}"), exception.Message));
        }

        return Task.CompletedTask;
    }

    /// <summary>关闭时退订事件。</summary>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 设置写盘统一由 <see cref="AppServices.Dispose"/> 在应用退出时执行
    /// （1.1.x 的「退出时保存」），这里不重复写。
    /// </remarks>
    protected override void OnClosed(EventArgs e)
    {
        NavView.SelectionChanged -= OnNavigationSelectionChanged;
        _services.Settings.Language.PropertyChanged -= OnLanguageSettingsChanged;

        base.OnClosed(e);
    }
}
