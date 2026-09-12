using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.ViewModels;

namespace UstPlayer.Views.Pages;

/// <summary>
/// 歌词页（对应 1.1.x <c>lyric_page.py</c>）：展示歌词开关与 LRC 文件选择。
/// </summary>
internal sealed partial class LyricPage : UserControl
{
    private readonly LyricPageViewModel _viewModel;
    private readonly INotificationHost _notifications;

    /// <summary>创建歌词页。</summary>
    /// <param name="viewModel">页面 ViewModel。</param>
    /// <param name="notifications">提示条宿主。</param>
    internal LyricPage(LyricPageViewModel viewModel, INotificationHost notifications)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notifications);

        _viewModel = viewModel;
        _notifications = notifications;

        InitializeComponent();
        DataContext = viewModel;
        ApplyTexts();
    }

    /// <summary>切换语言后刷新本页文案。</summary>
    internal void Retranslate() => ApplyTexts();

    /// <summary>设置界面文案。</summary>
    private void ApplyTexts()
    {
        LyricCard.Title = Translator.Tr("歌词");
        ShowLyricBox.Content = Translator.Tr("展示歌词");
        LrcLabel.Text = Translator.Tr("歌词文件（.lrc）:");
        LrcPathBox.Watermark = Translator.Tr("请选择 .lrc 歌词文件...");
        SelectLrcButton.Content = Translator.Tr("选择文件");
    }

    /// <summary>选择 LRC 歌词文件。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnSelectLrcClick(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;

        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Translator.Tr("选择LRC歌词文件"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Translator.Tr("LRC歌词文件"))
                {
                    Patterns = ["*.lrc"],
                },
            ],
            SuggestedStartLocation = await TryGetFolderAsync(storage),
        });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();

        if (string.IsNullOrEmpty(path))
        {
            _notifications.Notify(
                NotificationSeverity.Warning,
                Translator.Tr("提示"),
                Translator.Tr("无法获取该文件的本地路径"));

            return;
        }

        // 写设置即可：输入框通过绑定自动更新
        _viewModel.Player.LrcPath = path;
    }

    /// <summary>把起始目录解析为可用于对话框定位的文件夹。</summary>
    /// <param name="storage">存储提供者。</param>
    /// <returns>文件夹；无法解析时返回 <see langword="null"/>。</returns>
    private async Task<IStorageFolder?> TryGetFolderAsync(IStorageProvider storage)
    {
        var directory = _viewModel.ResolveStartDirectory();

        if (directory is null)
        {
            return null;
        }

        try
        {
            return await storage.TryGetFolderFromPathAsync(directory);
        }
        catch (Exception exception)
        {
            // 定位失败不该阻断对话框：退回系统默认位置
            AppLogger.Warning($"解析起始目录失败：{directory}（{exception.Message}）");
            return null;
        }
    }
}
