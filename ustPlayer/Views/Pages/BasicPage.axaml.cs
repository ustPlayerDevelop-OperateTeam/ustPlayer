using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.Projects;
using UstPlayer.ViewModels;

namespace UstPlayer.Views.Pages;

/// <summary>
/// 基础页（对应 1.1.x <c>basic_page.py</c>）：项目信息、显示选项与播放。
/// </summary>
/// <remarks>
/// 输入控件在 XAML 里直接双向绑定设置子域，因此本类**没有**「把设置填回控件」的逻辑。
/// 这里只处理必须与用户交互的部分：文件选择框、提示条、按钮点击。
/// </remarks>
internal sealed partial class BasicPage : UserControl
{
    private readonly BasicPageViewModel _viewModel;
    private readonly INotificationHost _notifications;

    /// <summary>播放回调（由主窗口提供，因为启动播放器需要窗口所有权）。</summary>
    private Func<Task>? _playHandler;
    private bool _playInProgress;

    /// <summary>创建基础页。</summary>
    /// <param name="viewModel">页面 ViewModel。</param>
    /// <param name="notifications">提示条宿主。</param>
    internal BasicPage(BasicPageViewModel viewModel, INotificationHost notifications)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notifications);

        _viewModel = viewModel;
        _notifications = notifications;

        InitializeComponent();
        DataContext = viewModel;
        ApplyTexts();
    }

    /// <summary>设置播放回调。</summary>
    /// <param name="handler">点击「播放」时执行。</param>
    internal void SetPlayHandler(Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _playHandler = handler;
    }

    /// <summary>切换语言后刷新本页文案。</summary>
    internal void Retranslate() => ApplyTexts();

    // ===================== 文案 =====================

    /// <summary>设置界面文案（UI 字符串一律经 <see cref="Translator.Tr"/>）。</summary>
    private void ApplyTexts()
    {
        ProjectCard.Title = Translator.Tr("项目");
        ProjectNameLabel.Text = Translator.Tr("项目名：");
        SongNameLabel.Text = Translator.Tr("曲名&曲师：");
        SongAuthorLabel.Text = Translator.Tr("MIDI作者：");
        UstAuthorLabel.Text = Translator.Tr("调音师：");
        MusicLabel.Text = Translator.Tr("音乐：");
        MusicButton.Content = Translator.Tr("选择");

        ProjectNameBox.Watermark = Translator.Tr("请输入项目名");
        SongNameBox.Watermark = Translator.Tr("请输入曲名&曲师");
        SongAuthorBox.Watermark = Translator.Tr("请输入MIDI作者");
        UstAuthorBox.Watermark = Translator.Tr("请输入调音师");
        MusicPathBox.Watermark = Translator.Tr("请选择音频（可选）");

        DisplayCard.Title = Translator.Tr("显示选项");
        ShowBpmLabel.Text = Translator.Tr("显示BPM");
        ShowPlayTimeLabel.Text = Translator.Tr("显示播放时间");
        ShowSongNameLabel.Text = Translator.Tr("显示曲目信息");
        ShowSongAuthorLabel.Text = Translator.Tr("显示MIDI作者");
        ShowUstAuthorLabel.Text = Translator.Tr("显示调音师");
        ShowNoteNameLabel.Text = Translator.Tr("显示音名");
        ShowUstLyricLabel.Text = Translator.Tr("显示歌字");
        ShowCopyrightLabel.Text = Translator.Tr("显示版权");

        ActionsCard.Title = Translator.Tr("工程");
        ImportButton.Content = Translator.Tr("导入项目");
        ExportButton.Content = Translator.Tr("保存项目");
        PlayButton.Content = Translator.Tr("播放 Play");
    }

    // ===================== 交互 =====================

    /// <summary>选择伴奏音频。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnSelectMusicClick(object? sender, RoutedEventArgs e)
    {
        var start = _viewModel.Project.MusicPath;

        var path = await PickFileAsync(
            Translator.Tr("选择伴奏音乐"),
            [new FilePickerFileType(Translator.Tr("音频文件"))
            {
                Patterns = ["*.flac", "*.mp3", "*.wav", "*.ogg", "*.m4a"],
            }],
            string.IsNullOrWhiteSpace(start) ? null : Path.GetDirectoryName(start));

        if (path is not null)
        {
            _viewModel.Project.MusicPath = path;
        }
    }

    /// <summary>导入工程。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(
            Translator.Tr("打开工程文件"),
            [new FilePickerFileType(Translator.Tr("ustPlayer工程文件"))
            {
                Patterns = ["*.uplr", "*.uprd"],
            }],
            _viewModel.LastOpenDirectory);

        if (path is null)
        {
            return;
        }

        try
        {
            _viewModel.ImportProject(path);
            _notifications.Notify(
                NotificationSeverity.Success,
                Translator.Tr("成功"),
                string.Format(Translator.Tr("已加载工程：{0}"), path));
        }
        catch (ProjectFormatException exception)
        {
            AppLogger.Error($"导入工程失败（格式）：{path}", exception);
            _notifications.Notify(
                NotificationSeverity.Error,
                "ERcode006",
                string.Format(Translator.Tr("加载工程文件失败：{0}"), exception.Message));
        }
        catch (Exception exception)
        {
            AppLogger.Error($"导入工程失败：{path}", exception);
            _notifications.Notify(
                NotificationSeverity.Error,
                "ERcode006",
                string.Format(Translator.Tr("加载工程文件失败：{0}"), exception.Message));
        }
    }

    /// <summary>保存工程为 <c>.uplr</c>。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();

        if (storage is null)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Translator.Tr("导出你的工程文件"),
            SuggestedFileName = _viewModel.SuggestProjectFileName(),
            DefaultExtension = "uplr",
            SuggestedStartLocation = await TryGetFolderAsync(storage, _viewModel.LastExportDirectory),
            FileTypeChoices =
            [
                new FilePickerFileType(Translator.Tr("ustPlayer工程文件"))
                {
                    Patterns = ["*.uplr"],
                },
            ],
        });

        var path = file?.TryGetLocalPath();

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // 有些平台不会按 DefaultExtension 补后缀
        if (!path.EndsWith(".uplr", StringComparison.OrdinalIgnoreCase))
        {
            path += ".uplr";
        }

        try
        {
            _viewModel.ExportProject(path);
            _notifications.Notify(
                NotificationSeverity.Success,
                Translator.Tr("成功"),
                string.Format(Translator.Tr("工程已导出到：{0}"), path));
        }
        catch (Exception exception)
        {
            AppLogger.Error($"导出工程失败：{path}", exception);
            _notifications.Notify(
                NotificationSeverity.Error,
                "ERcode010",
                string.Format(Translator.Tr("导出失败：{0}"), exception.Message));
        }
    }

    /// <summary>播放。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (_playHandler is null || _playInProgress)
        {
            return;
        }

        // 播放器是全屏置顶窗口；解析大 UST 也有耗时，期间重复点击会开出多个窗口
        _playInProgress = true;
        PlayButton.IsEnabled = false;

        try
        {
            await _playHandler();
        }
        finally
        {
            _playInProgress = false;
            PlayButton.IsEnabled = true;
        }
    }

    // ===================== 辅助 =====================

    /// <summary>弹出打开文件对话框。</summary>
    /// <param name="title">对话框标题（已翻译）。</param>
    /// <param name="types">文件类型过滤。</param>
    /// <param name="startDirectory">起始目录；为空用系统默认。</param>
    /// <returns>选中的本地路径；取消或非本地路径返回 <see langword="null"/>。</returns>
    private async Task<string?> PickFileAsync(
        string title,
        FilePickerFileType[] types,
        string? startDirectory)
    {
        var storage = GetStorageProvider();

        if (storage is null)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types,
            SuggestedStartLocation = await TryGetFolderAsync(storage, startDirectory),
        });

        if (files.Count == 0)
        {
            return null;
        }

        var path = files[0].TryGetLocalPath();

        if (string.IsNullOrEmpty(path))
        {
            _notifications.Notify(
                NotificationSeverity.Warning,
                Translator.Tr("提示"),
                Translator.Tr("无法获取该文件的本地路径"));

            return null;
        }

        return path;
    }

    /// <summary>取当前页所在窗口的存储提供者。</summary>
    /// <returns>存储提供者；不在窗口中时返回 <see langword="null"/>。</returns>
    private IStorageProvider? GetStorageProvider() => TopLevel.GetTopLevel(this)?.StorageProvider;

    /// <summary>把目录路径解析为可用于对话框定位的文件夹。</summary>
    /// <param name="storage">存储提供者。</param>
    /// <param name="directory">目录路径；可能不存在。</param>
    /// <returns>文件夹；无法解析时返回 <see langword="null"/>。</returns>
    private static async Task<IStorageFolder?> TryGetFolderAsync(
        IStorageProvider storage,
        string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
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
