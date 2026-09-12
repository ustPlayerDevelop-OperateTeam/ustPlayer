using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.Interop;

namespace UstPlayer.Views;

/// <summary>
/// 主窗口。窗口镶边策略继承自 <see cref="ShellWindow"/>（见
/// <c>docs/adr-0002-window-chrome.md</c>）。
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>本类目前是 Phase 4 的临时外壳</b>：只有一个「打开 UST 并播放」入口，
/// 用于打通「设置 → 播放参数 → 渲染器出帧 → 全屏显示」这条端到端链路。
/// Phase 5 会把它替换为 FluentAvalonia 的 NavigationView 外壳 + 五个设置页，
/// 届时本类的播放入口应当迁到对应页面（门面 <see cref="PlayerLauncher"/> 不变）。
/// </para>
/// <para>
/// 业务服务一律经构造注入的 <see cref="AppServices"/> 取得，本类**不 new** 任何具体实现。
/// </para>
/// </remarks>
internal sealed partial class MainWindow : ShellWindow
{
    private readonly AppServices _services;

    /// <summary>当前打开的播放窗口；用于避免同时打开多个全屏播放器。</summary>
    private PlayerWindow? _playerWindow;

    /// <summary>构造主窗口。</summary>
    /// <param name="services">组合根。</param>
    public MainWindow(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;

        InitializeComponent();
        ApplyTexts();

        SetStatus($"设置文件：{_services.Settings.SettingsPath}");
    }

    /// <summary>设置界面文案（i18n 约定：UI 字符串一律经 <see cref="Translator.Tr"/>）。</summary>
    private void ApplyTexts()
    {
        TitleText.Text = "ustPlayer";
        SubtitleText.Text = Translator.Tr("C# / Avalonia 2.0 迁移中：Phase 4 已打通播放链路，Phase 5 将替换本页");
        OpenUstButton.Content = Translator.Tr("打开 UST 并播放");
    }

    /// <summary>选择 UST 文件并启动全屏播放。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnOpenUstClick(object? sender, RoutedEventArgs e)
    {
        ClearError();

        // 全屏播放器是置顶窗口，叠开两个会互相抢焦点且各自跑一条帧循环
        if (_playerWindow is { IsVisible: true })
        {
            ShowError(Translator.Tr("播放器已打开，请先按 Esc 退出当前播放"));
            return;
        }

        try
        {
            var path = await PickUstFileAsync();

            if (path is null)
            {
                return;
            }

            var ustInfo = _services.Ust.Parse(path, _services.Settings.File.Encoding);

            if (ustInfo.Notes.Count == 0)
            {
                ShowError(Translator.Tr("该 UST 中没有音符，无法播放"));
                return;
            }

            _services.Settings.File.UstPath = path;
            _services.Settings.LastOpenDirectory =
                Path.GetDirectoryName(path) ?? _services.Settings.LastOpenDirectory;

            SetStatus(Translator.Tr("正在启动播放器…"));
            AppLogger.Info($"开始播放：{path}（音符 {ustInfo.Notes.Count} 个）");

            _playerWindow = PlayerLauncher.Launch(_services.Settings, ustInfo);
            SetStatus($"已打开：{path}");
        }
        catch (RendererException exception)
        {
            // 渲染器缺失是部署问题而非用户输入问题，单独提示
            var prefix = Translator.Tr("渲染器不可用");
            ShowError($"{prefix}：{exception.Message}");
            AppLogger.Error("启动播放器失败：渲染器不可用", exception);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            AppLogger.Error("启动播放器失败", exception);
        }
    }

    /// <summary>弹出 UST 文件选择框。</summary>
    /// <returns>选中的本地路径；用户取消或路径非本地时返回 <see langword="null"/>。</returns>
    private async Task<string?> PickUstFileAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title = Translator.Tr("选择 UST 工程文件"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Translator.Tr("UST 工程文件"))
                {
                    Patterns = ["*.ust"],
                    AppleUniformTypeIdentifiers = ["public.plain-text"],
                    MimeTypes = ["text/plain"],
                },
            ],
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);

        if (files.Count == 0)
        {
            return null;
        }

        // 云盘 / 虚拟位置的文件没有本地路径，此时无法交给解析器
        var path = files[0].TryGetLocalPath();

        if (string.IsNullOrEmpty(path))
        {
            ShowError(Translator.Tr("无法获取该文件的本地路径"));
            return null;
        }

        return path;
    }

    /// <summary>更新状态行。</summary>
    /// <param name="message">状态文本。</param>
    private void SetStatus(string message) => StatusText.Text = message;

    /// <summary>显示错误行。</summary>
    /// <param name="message">错误文本。</param>
    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    /// <summary>隐藏错误行。</summary>
    private void ClearError()
    {
        ErrorText.Text = null;
        ErrorText.IsVisible = false;
    }
}
