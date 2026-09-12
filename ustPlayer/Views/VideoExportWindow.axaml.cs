using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.ViewModels;

namespace UstPlayer.Views;

/// <summary>
/// 「导出视频」对话框 — 从 1.1.x <c>ui/video_export_dialog.py</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 与 1.1.x 的 <c>MessageBoxBase</c> 对应的是普通 <see cref="Window"/> + <c>ShowDialog(owner)</c>：
/// 内容比 <c>SettingsPage.ConfirmAsync</c> 的确认框丰富得多，但用对话框基类仍不划算
/// （FluentAvalonia 的 <c>ContentDialog</c> 也可以，本仓库既有做法是手写 Window）。
/// </para>
/// <para>
/// 本类只处理**交互**：文件选择框、提示条、关窗拦截与按钮点击；
/// 分辨率 / 帧率 / 进度 / 取消等状态与流程在 <see cref="VideoExportViewModel"/>。
/// </para>
/// </remarks>
internal sealed partial class VideoExportWindow : Window
{
    private readonly VideoExportViewModel _viewModel;
    private readonly INotificationHost _notifications;

    /// <summary>创建对话框。</summary>
    /// <param name="viewModel">对话框 ViewModel。</param>
    /// <param name="notifications">提示条宿主（主窗口）。</param>
    /// <remarks>测试直接构造本对象；显示请用 <see cref="ShowAsync"/>。</remarks>
    internal VideoExportWindow(VideoExportViewModel viewModel, INotificationHost notifications)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notifications);

        _viewModel = viewModel;
        _notifications = notifications;

        InitializeComponent();
        DataContext = viewModel;
        ApplyTexts();

        // 导出期间禁止关窗：关掉窗口会让渲染继续跑，而进度与提示已经没有接收者
        Closing += OnClosing;
    }

    /// <summary>以模态方式显示对话框。</summary>
    /// <param name="owner">拥有者窗口（居中定位与模态都依赖它）。</param>
    /// <param name="viewModel">对话框 ViewModel。</param>
    /// <param name="notifications">提示条宿主。</param>
    /// <returns>对话框关闭后完成的任务。</returns>
    internal static async Task ShowAsync(
        Window owner,
        VideoExportViewModel viewModel,
        INotificationHost notifications)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var window = new VideoExportWindow(viewModel, notifications);
        await window.ShowDialog(owner);
    }

    // ===================== 文案 =====================

    /// <summary>设置界面文案（UI 字符串一律经 <see cref="Translator.Tr"/>）。</summary>
    /// <remarks>
    /// 这里是模态对话框，语言不可能在它打开期间变化，因此没有 <c>Retranslate()</c>——
    /// 文案在构造时定一次即可（标签保留 1.1.x 的结尾冒号，译文键因此不用改名）。
    /// </remarks>
    private void ApplyTexts()
    {
        Title = Translator.Tr("导出视频");
        TitleText.Text = Translator.Tr("导出视频");

        OutputLabel.Text = Translator.Tr("输出视频：");
        OutputPathBox.Watermark = Translator.Tr("选择 .mp4 保存路径");
        BrowseButton.Content = Translator.Tr("浏览");

        ResolutionLabel.Text = Translator.Tr("分辨率：");
        FpsLabel.Text = Translator.Tr("帧率：");
        MuxLabel.Text = Translator.Tr("混入伴奏音频：");

        StartButton.Content = Translator.Tr("开始导出");
        CancelButton.Content = Translator.Tr("取消");
    }

    // ===================== 交互 =====================

    /// <summary>选择输出视频路径。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var current = _viewModel.OutputPath;
        var directory = Path.GetDirectoryName(current);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Translator.Tr("导出视频"),
            SuggestedFileName = Path.GetFileName(current),
            DefaultExtension = "mp4",
            SuggestedStartLocation = await TryGetFolderAsync(directory),

            // 复用 1.1.x 的文件类型文案（目录里没有单独的「视频文件」条目，
            // 新增一条会让英 / 文言界面静默回退）
            FileTypeChoices =
            [
                new FilePickerFileType(Translator.Tr("视频文件 (*.mp4);;所有文件 (*.*)"))
                {
                    Patterns = ["*.mp4"],
                },
            ],
        });

        var path = file?.TryGetLocalPath();

        if (!string.IsNullOrEmpty(path))
        {
            _viewModel.OutputPath = path;
        }
    }

    /// <summary>开始导出。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 导出本身在 <see cref="VideoExportViewModel.ExportAsync"/> 里被挪到线程池上，
    /// 这里只 <c>await</c>，因此窗口全程可响应（不阻塞 UI 线程）。
    /// </remarks>
    private async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsExporting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.OutputPath))
        {
            _notifications.Notify(
                NotificationSeverity.Warning,
                Translator.Tr("提示"),
                Translator.Tr("请先选择输出视频路径"));

            return;
        }

        var outcome = await _viewModel.ExportAsync();

        switch (outcome.Kind)
        {
            case VideoExportOutcomeKind.Success:
                _notifications.Notify(
                    NotificationSeverity.Success,
                    Translator.Tr("成功"),
                    string.Format(
                        Translator.Tr("视频已导出：{0}\n已保存工程：{1}"),
                        _viewModel.OutputPath,
                        outcome.UprdPath.Length > 0 ? outcome.UprdPath : Translator.Tr("（无）")));

                // 成功后关闭对话框（1.1.x 的 accept()）；失败 / 取消都留在对话框里让用户重试
                Close();
                break;

            case VideoExportOutcomeKind.Cancelled:
                _notifications.Notify(
                    NotificationSeverity.Informational,
                    Translator.Tr("提示"),
                    Translator.Tr("视频导出已取消"));
                break;

            default:
                _notifications.Notify(
                    NotificationSeverity.Error,
                    "ERcode011",
                    string.Format(Translator.Tr("导出视频失败：{0}"), outcome.ErrorMessage));
                break;
        }
    }

    /// <summary>取消：导出中请求取消，否则关闭对话框。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsExporting)
        {
            _viewModel.RequestCancel();
            return;
        }

        Close();
    }

    /// <summary>导出期间拦截关窗（Esc / 标题栏关闭按钮都不能把导出丢掉）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_viewModel.IsExporting)
        {
            return;
        }

        e.Cancel = true;
        AppLogger.Info("导出进行中，已忽略关窗请求（请先点「取消」）");
    }

    // ===================== 辅助 =====================

    /// <summary>把目录路径解析为可用于对话框定位的文件夹。</summary>
    /// <param name="directory">目录路径；可能不存在。</param>
    /// <returns>文件夹；无法解析时返回 <see langword="null"/>。</returns>
    private async Task<IStorageFolder?> TryGetFolderAsync(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(directory);
        }
        catch (Exception exception)
        {
            // 定位失败不该阻断对话框：退回系统默认位置
            AppLogger.Warning($"解析起始目录失败：{directory}（{exception.Message}）");
            return null;
        }
    }
}
