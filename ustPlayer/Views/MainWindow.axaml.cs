using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

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
    /// <summary>导航键：基础页（拖放时接受工程文件）。</summary>
    internal const string BasicNavKey = "basic";

    /// <summary>导航键：文件页（拖放时接受 <c>.ust</c>）。</summary>
    internal const string FileNavKey = "file";

    /// <summary>导航键：播放器页（不接受拖放）。</summary>
    internal const string PlayerStyleNavKey = "player_style";

    /// <summary>导航键：歌词页（拖放时接受 <c>.lrc</c>）。</summary>
    internal const string LyricNavKey = "lyric";

    /// <summary>导航键：设置页（不接受拖放）。</summary>
    internal const string SettingsNavKey = "settings";

    private readonly AppServices _services;
    private readonly MainWindowViewModel _viewModel;

    /// <summary>导航键 → 导航项（语言切换后重设标题用）。</summary>
    private readonly Dictionary<string, NavigationViewItem> _navItems = [];

    /// <summary>导航键 → 标题的中文原文。</summary>
    private readonly Dictionary<string, string> _navTitleSources = [];

    /// <summary>导航键 → 页面实例（尚未迁移的页面不在其中）。</summary>
    private readonly Dictionary<string, Control> _pages = [];

    /// <summary>当前显示的页面键；文件拖放按它路由（对应 1.1.x 的 <c>_current_interface</c>）。</summary>
    private string _currentNavKey = BasicNavKey;

    private BasicPage? _basicPage;
    private FilePage? _filePage;
    private PlayerStylePage? _playerStylePage;
    private LyricPage? _lyricPage;
    private SettingsPage? _settingsPage;

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

        // 强调色与窗口效果也要在**启动时**应用一次：
        // 只在设置页的 PropertyChanged 里应用的话，上次保存的非默认值
        // 要等用户动一下下拉才生效（1.1.x 是在主窗口启动时应用的）。
        AppearanceController.ApplyAccentColor(_services.Settings.Theme);
        AppearanceController.ApplyWindowEffect(_services.Settings.Theme, this);

        BuildPages();
        BuildNavigation();
        SetupDragDrop();

        // 语言偏好变更后立即重译整个外壳（1.1.x 走 language_changed 信号）
        _services.Settings.Language.PropertyChanged += OnLanguageSettingsChanged;

        NavView.SelectedItem = _navItems[BasicNavKey];

        AppLogger.Info($"主窗口就绪（设置文件：{_services.Settings.SettingsPath}）");
    }

    // ===================== 提示条 =====================

    /// <summary>通知停留时长（毫秒）：成功短、错误长（与 1.1.x 的 3000 / 5000 一致）。</summary>
    private static readonly TimeSpan SuccessLifetime = TimeSpan.FromSeconds(3);

    /// <summary>错误通知停留时长。</summary>
    private static readonly TimeSpan ErrorLifetime = TimeSpan.FromSeconds(5);

    /// <summary>同时最多显示的通知条数，超出时移除最旧的一条。</summary>
    private const int MaxNotifications = 3;

    /// <summary>通知滑入 / 滑出的位移（像素）。</summary>
    private const double NotificationSlideOffset = 40;

    /// <summary>通知动画时长。</summary>
    private static readonly TimeSpan NotificationAnimationDuration = TimeSpan.FromMilliseconds(220);

    /// <inheritdoc />
    public void Notify(NotificationSeverity severity, string title, string message)
    {
        var bar = new InfoBar
        {
            Severity = severity switch
            {
                NotificationSeverity.Success => InfoBarSeverity.Success,
                NotificationSeverity.Warning => InfoBarSeverity.Warning,
                NotificationSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            },
            Title = title,
            Message = message,
            IsOpen = true,
            IsClosable = true,
        };

        // 容器负责动画与定位；InfoBar 自身只画内容
        var container = new Border
        {
            Child = bar,
            Opacity = 0,
            RenderTransform = new TranslateTransform(NotificationSlideOffset, 0),
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = NotificationAnimationDuration,
                },
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = NotificationAnimationDuration,
                },
            },
        };

        // 超出上限时先挤掉最旧的一条，避免通知堆满整个窗口
        while (NotificationStack.Children.Count >= MaxNotifications)
        {
            NotificationStack.Children.RemoveAt(0);
        }

        NotificationStack.Children.Add(container);

        // 入场：必须等一帧，否则启动值与目标值在同一帧内会被合并、看不到动画
        Dispatcher.UIThread.Post(() => container.RenderTransform = new TranslateTransform(0, 0));
        Dispatcher.UIThread.Post(() => container.Opacity = 1);

        var lifetime = severity == NotificationSeverity.Error ? ErrorLifetime : SuccessLifetime;

        // 到时滑出并移除
        DispatcherTimer.RunOnce(
            () =>
            {
                container.Opacity = 0;
                container.RenderTransform = new TranslateTransform(NotificationSlideOffset, 0);

                // 等动画结束再真正移除，否则元素会瞬间消失
                DispatcherTimer.RunOnce(
                    () => NotificationStack.Children.Remove(container),
                    NotificationAnimationDuration);
            },
            lifetime);
    }

    // ===================== 装配 =====================

    /// <summary>创建全部页面。</summary>
    private void BuildPages()
    {
        _basicPage = new BasicPage(new BasicPageViewModel(_services), this);
        _basicPage.SetPlayHandler(PlayAsync);
        _pages[BasicNavKey] = _basicPage;

        _filePage = new FilePage(new FilePageViewModel(_services), this);
        _pages[FileNavKey] = _filePage;

        _playerStylePage = new PlayerStylePage(new PlayerStylePageViewModel(_services), this);
        _pages[PlayerStyleNavKey] = _playerStylePage;

        _lyricPage = new LyricPage(new LyricPageViewModel(_services), this);
        _pages[LyricNavKey] = _lyricPage;

        _settingsPage = new SettingsPage(new SettingsPageViewModel(_services), this);
        _pages[SettingsNavKey] = _settingsPage;
    }

    /// <summary>
    /// 创建导航项并接线切换。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 四个内容页在上，「设置」用 <see cref="NavigationView.FooterMenuItems"/> 放在**底部**
    /// （对应 1.1.x 把「其他」放底部的布局）。
    /// </para>
    /// <para>
    /// 不用 <see cref="NavigationView.IsSettingsVisible"/> 的内建设置项：
    /// 它的外观由 FluentAvalonia 模板决定，而这里看不到界面、无法目视核对——
    /// 自定义 footer 项与上面四项走完全相同的构造路径，样式必然一致。
    /// </para>
    /// <para>
    /// 图标只能用 FluentAvalonia <c>Symbol</c> 枚举里真实存在的成员：该枚举没有
    /// <c>Palette</c> / <c>Music</c> / <c>Info</c>（已实测转储确认），用不存在的名字会编译失败。
    /// </para>
    /// </remarks>
    private void BuildNavigation()
    {
        AddNavItem(BasicNavKey, "基础", Symbol.Home, footer: false);
        AddNavItem(FileNavKey, "文件", Symbol.Document, footer: false);
        AddNavItem(PlayerStyleNavKey, "播放器", Symbol.ColorFill, footer: false);
        AddNavItem(LyricNavKey, "歌词", Symbol.Audio, footer: false);
        AddNavItem(SettingsNavKey, "设置", Symbol.Setting, footer: true);

        NavView.SelectionChanged += OnNavigationSelectionChanged;
    }

    /// <summary>添加一个导航项。</summary>
    /// <param name="key">稳定键。</param>
    /// <param name="titleSource">标题的中文原文。</param>
    /// <param name="symbol">图标。</param>
    /// <param name="footer">是否放到导航栏底部的 footer 区域。</param>
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
    /// <remarks>
    /// 设置项不在 <c>MenuItems</c> 里，因此它的选中不走 <c>Tag</c> 这条路，
    /// 需要单独看 <see cref="NavigationViewSelectionChangedEventArgs.IsSettingsSelected"/>。
    /// </remarks>
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
    /// 顺带记住当前页面键：文件拖放要按它决定接受哪些扩展名（对应 1.1.x 的
    /// <c>switchTo</c> 里记下 <c>_current_interface</c>）。
    /// </remarks>
    private void ShowPage(string key)
    {
        _currentNavKey = key;

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

    // ===================== 文件拖放 =====================

    /// <summary>
    /// 打开窗口级文件拖放（对应 1.1.x 主窗口的 <c>setAcceptDrops(True)</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DragDrop.AllowDropProperty"/> 是**继承**的附加属性
    /// （<c>RegisterAttached(..., inherits: true)</c>），所以只在窗口上设一次即可：
    /// 页面里所有子控件（输入框、滚动视图、按钮…）都继承到 <see langword="true"/>，
    /// 指针落在哪个子控件上都算「允许放置」。1.1.x 需要逐个
    /// <c>widget.setAcceptDrops(False)</c> 才能防止子控件吞掉事件，**Avalonia 不需要**。
    /// </para>
    /// <para>
    /// 拖放四个事件只有**冒泡**阶段（对 11.3.12 实测 <c>RoutingStrategies</c> 均为
    /// <c>Bubble</c>），子控件不订阅就会一路冒到窗口；这里注册在窗口上并带
    /// <c>handledEventsToo: true</c>，即使将来某个子控件把事件标记为已处理，外壳仍能收到。
    /// </para>
    /// <para>
    /// 用 <c>AddHandler</c> 而不是 XAML 的 <c>DragDrop.Drop="…"</c>：需要
    /// <c>handledEventsToo</c>，XAML 的附加事件写法给不了这个开关。
    /// </para>
    /// </remarks>
    private void SetupDragDrop()
    {
        DragDrop.SetAllowDrop(this, true);

        AddHandler(DragDrop.DragOverEvent, OnFileDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnFileDrop, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// 拖动经过窗口：只按扩展名判断当前页面是否接受（与 1.1.x 的 <c>dragEnterEvent</c> 一致）。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 这里只看扩展名、不看文件是否存在：拖动过程中文件还没被「交」过来，
    /// 存在性检查留给真正的放下动作。
    /// </remarks>
    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (FileDropRouting.AcceptsAny(_currentNavKey, DroppedPaths(e)))
        {
            e.DragEffects = DragDropEffects.Copy;

            // 只有真的接受才算「处理掉」，让系统显示可放置的光标
            e.Handled = true;
            return;
        }

        // 不接受：把效果设成 None 让系统显示「禁止」，但不置 Handled——
        // 事件继续冒泡，别的控件（若有）仍有机会接受
        e.DragEffects = DragDropEffects.None;
    }

    /// <summary>
    /// 放下文件：按当前页面执行（与 1.1.x 的 <c>dropEvent</c> 一致）。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 载荷为空、不是文件、扩展名不匹配、文件已被删除时一律**静默忽略**且不抛异常。
    /// </remarks>
    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        var path = FileDropRouting.Select(_currentNavKey, DroppedPaths(e));

        if (path is null || !ApplyDroppedFile(_currentNavKey, path))
        {
            return;
        }

        e.Handled = true;
    }

    /// <summary>把拖放载荷里的文件取成本地路径。</summary>
    /// <param name="e">事件参数。</param>
    /// <returns>本地路径序列（非文件载荷为空序列；非本地项为 <see langword="null"/>）。</returns>
    /// <remarks>
    /// 用 11.3 的 <c>DataTransfer</c> 接口：旧的 <c>e.Data</c>（<c>IDataObject</c>）在本版本
    /// 已标记 <c>[Obsolete]</c>，在 <c>TreatWarningsAsErrors</c> 下会直接编译失败。
    /// </remarks>
    private static IEnumerable<string?> DroppedPaths(DragEventArgs e)
    {
        var files = e.DataTransfer?.TryGetFiles();

        return files is null ? [] : files.Select(StorageProviderExtensions.TryGetLocalPath);
    }

    /// <summary>执行放置动作：按页面把文件写进对应设置，或走工程导入。</summary>
    /// <param name="navKey">当前页面键。</param>
    /// <param name="path">已确认存在且扩展名匹配的本地路径。</param>
    /// <returns>真的处理了这个文件时为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 写设置就够了——控件直接绑定设置子域，界面会自己刷新；
    /// 基础页则复用「导入项目」按钮的同一条路径。
    /// </remarks>
    private bool ApplyDroppedFile(string navKey, string path)
    {
        switch (navKey)
        {
            case BasicNavKey when _basicPage is not null:
                AppLogger.Info($"拖放导入工程：{path}");
                _basicPage.ImportProject(path);
                return true;

            case FileNavKey:
                _services.Settings.File.UstPath = path;
                AppLogger.Info($"拖放设置 UST 路径：{path}");
                return true;

            case LyricNavKey:
                _services.Settings.Player.LrcPath = path;
                AppLogger.Info($"拖放设置 LRC 路径：{path}");
                return true;

            default:
                // 兜底：FileDropRouting 已经按页面过滤过扩展名，正常到不了这里
                return false;
        }
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
        _settingsPage?.Retranslate();

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

/// <summary>
/// 文件拖放路由：把「当前页面 + 拖入的文件」映射为「接受还是忽略」。
/// </summary>
/// <remarks>
/// <para>
/// 与 1.1.x <c>main_window.py</c> 的 <c>_accepts_drag</c> / <c>dropEvent</c> 一一对应：
/// 每个页面**只**接受自己那一类扩展名——往基础页拖 <c>.ust</c> 什么也不会发生。
/// </para>
/// <list type="bullet">
///   <item>基础页：<c>.uplr</c> / <c>.uprd</c> —— 走工程导入；</item>
///   <item>文件页：<c>.ust</c> —— 写入 <c>FileSettings.UstPath</c>；</item>
///   <item>歌词页：<c>.lrc</c> —— 写入 <c>PlayerSettings.LrcPath</c>；</item>
///   <item>其余页面：一概忽略。</item>
/// </list>
/// <para>
/// 抽成不依赖 Avalonia 的纯函数是为了能单测：真实拖放由 OLE 驱动，
/// 脚本里造不出来，能自动验证的只有这层判断。
/// </para>
/// </remarks>
internal static class FileDropRouting
{
    /// <summary>基础页接受的扩展名：1.1.x 的工程文件。</summary>
    private static readonly string[] ProjectExtensions = [".uplr", ".uprd"];

    /// <summary>文件页接受的扩展名。</summary>
    private static readonly string[] UstExtensions = [".ust"];

    /// <summary>歌词页接受的扩展名。</summary>
    private static readonly string[] LrcExtensions = [".lrc"];

    /// <summary>该页面接受的扩展名（含点号；空数组表示不接受任何文件）。</summary>
    /// <param name="navKey">导航键。</param>
    /// <returns>扩展名列表（内部共享实例，调用方不得修改）。</returns>
    private static string[] AcceptedExtensions(string? navKey) => navKey switch
    {
        MainWindow.BasicNavKey => ProjectExtensions,
        MainWindow.FileNavKey => UstExtensions,
        MainWindow.LyricNavKey => LrcExtensions,
        _ => [],
    };

    /// <summary>当前页面是否接受这个文件（只看扩展名，与 1.1.x 的拖入判断一致）。</summary>
    /// <param name="navKey">导航键。</param>
    /// <param name="path">文件名或完整路径；可为 <see langword="null"/>。</param>
    /// <returns>扩展名匹配时为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 用「后缀比较」而不是 <c>Path.GetExtension</c>：与 1.1.x 的
    /// <c>path.lower().endswith(...)</c> 逐字等价，也不受非法路径字符影响。
    /// </remarks>
    internal static bool AcceptsExtension(string? navKey, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var extension in AcceptedExtensions(navKey))
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>文件列表里有没有当前页面接受的扩展名（拖动经过时的判断）。</summary>
    /// <param name="navKey">导航键。</param>
    /// <param name="paths">拖入的文件路径。</param>
    /// <returns>至少有一个匹配时为 <see langword="true"/>。</returns>
    internal static bool AcceptsAny(string? navKey, IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var path in paths)
        {
            if (AcceptsExtension(navKey, path))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>在拖入的文件里挑出第一个可处理的（扩展名匹配**且文件真实存在**）。</summary>
    /// <param name="navKey">导航键。</param>
    /// <param name="paths">拖入的文件路径（可能含 <see langword="null"/>、不存在或不匹配的项）。</param>
    /// <returns>可处理的本地路径；一个都没有时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 只认**真实存在的本地文件**：非本地 URI 与拖放中途被删掉的文件都会被跳过，
    /// 且整个过程不抛异常。目录即使名字以 <c>.uplr</c> 结尾也会被跳过
    /// （<see cref="File.Exists(string?)"/> 对目录返回 <see langword="false"/>），
    /// 这一点比 1.1.x 的 <c>os.path.exists</c> 更严格——那里会把目录交给导入逻辑再报错。
    /// </remarks>
    internal static string? Select(string? navKey, IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var path in paths)
        {
            if (AcceptsExtension(navKey, path) && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
