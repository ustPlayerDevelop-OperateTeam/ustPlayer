using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.ViewModels;

namespace UstPlayer.Views.Pages;

/// <summary>
/// 文件页（对应 1.1.x <c>file_page.py</c>）：UST 选择、编码切换与内容预览。
/// </summary>
/// <remarks>
/// UST 路径 / 音高线开关 / 编码在 XAML 里直接绑定，因此本类不做「设置 → 控件」回填。
/// 这里只做三件事：选择文件、把设置变更转成「刷新预览」、以及显示提示条。
/// </remarks>
internal sealed partial class FilePage : UserControl
{
    private readonly FilePageViewModel _viewModel;
    private readonly INotificationHost _notifications;

    /// <summary>创建文件页。</summary>
    /// <param name="viewModel">页面 ViewModel。</param>
    /// <param name="notifications">提示条宿主。</param>
    internal FilePage(FilePageViewModel viewModel, INotificationHost notifications)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notifications);

        _viewModel = viewModel;
        _notifications = notifications;

        InitializeComponent();
        DataContext = viewModel;
        ApplyTexts();
    }

    /// <summary>
    /// 挂到可视树时开始监听设置变更。
    /// </summary>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 用「挂载时订阅、卸载时退订」而不是在构造里订阅：
    /// 切换导航页会卸载/重新挂载页面，只在构造里订阅会在卸载后继续持有引用，
    /// 而只在构造订阅又会在卸载后失去刷新能力。
    /// </remarks>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel.File.PropertyChanged += OnFileSettingsChanged;
        _viewModel.RefreshPreview();
    }

    /// <summary>离开可视树时退订。</summary>
    /// <param name="e">事件参数。</param>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _viewModel.File.PropertyChanged -= OnFileSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>切换语言后刷新本页文案。</summary>
    internal void Retranslate() => ApplyTexts();

    /// <summary>设置界面文案。</summary>
    private void ApplyTexts()
    {
        FileCard.Title = Translator.Tr("文件");
        UstLabel.Text = Translator.Tr("ust:");
        UstPathBox.Watermark = Translator.Tr("请选择或拖入 .ust 文件路径...");
        SelectUstButton.Content = Translator.Tr("选择ust文件");
        CurveShowBox.Content = Translator.Tr("显示音高线变化");
        EncodingLabel.Text = Translator.Tr("编码方式:");
        CheckEncodingButton.Content = Translator.Tr("编码检查");
        PreviewCard.Title = Translator.Tr("内容预览");
        PreviewBox.Watermark = Translator.Tr("选择 UST 文件后在此预览...");
    }

    /// <summary>UST 路径或编码变化后刷新预览。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnFileSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Settings.Domains.FileSettings.UstPath)
            or nameof(Settings.Domains.FileSettings.Encoding))
        {
            _viewModel.RefreshPreview();
        }
    }

    /// <summary>选择 UST 文件。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnSelectUstClick(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;

        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Translator.Tr("选择ust文件"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Translator.Tr("UST文件"))
                {
                    Patterns = ["*.ust"],
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

        // 写设置即可：绑定与 PropertyChanged 会负责刷新输入框与预览
        _viewModel.File.UstPath = path;
    }

    /// <summary>编码检查：以当前编码严格试读并报告结论。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnCheckEncodingClick(object? sender, RoutedEventArgs e)
    {
        var path = _viewModel.File.UstPath.Trim();

        if (path.Length == 0)
        {
            _notifications.Notify(
                NotificationSeverity.Warning,
                Translator.Tr("提示"),
                Translator.Tr("请先选择 UST 文件"));

            return;
        }

        if (!File.Exists(path))
        {
            _notifications.Notify(
                NotificationSeverity.Error,
                "ERcode001",
                Translator.Tr("UST 文件不存在"));

            return;
        }

        if (_viewModel.CheckEncoding())
        {
            _notifications.Notify(
                NotificationSeverity.Success,
                Translator.Tr("编码正确"),
                string.Format(Translator.Tr("使用 {0} 可以正常读取该文件"), _viewModel.Encoding));
        }
        else
        {
            _notifications.Notify(
                NotificationSeverity.Error,
                "ERcode004",
                string.Format(
                    Translator.Tr("当前编码 {0} 无法读取该文件，请尝试其他编码"),
                    _viewModel.Encoding));
        }
    }

    /// <summary>用当前 UST 路径所在目录作为选择框的起始位置。</summary>
    /// <param name="storage">存储提供者。</param>
    /// <returns>文件夹；无法解析时返回 <see langword="null"/>。</returns>
    private async Task<IStorageFolder?> TryGetFolderAsync(IStorageProvider storage)
    {
        var path = _viewModel.File.UstPath.Trim();

        if (path.Length == 0)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return await storage.TryGetFolderFromPathAsync(directory);
        }
        catch (Exception exception)
        {
            // 定位失败不该阻断对话框
            AppLogger.Warning($"解析起始目录失败：{directory}（{exception.Message}）");
            return null;
        }
    }
}
