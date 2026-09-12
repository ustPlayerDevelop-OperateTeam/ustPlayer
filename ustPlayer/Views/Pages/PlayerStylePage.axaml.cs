using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using UstPlayer.Diagnostics;
using UstPlayer.I18n;
using UstPlayer.ViewModels;

namespace UstPlayer.Views.Pages;

/// <summary>
/// 播放器样式页（对应 1.1.x <c>player_style_page.py</c>）：颜色、歌词位置、字体与其他显示设置。
/// </summary>
/// <remarks>
/// <para>
/// 颜色、自定义文字与下拉选中值都在 XAML 里绑定（前两者直接绑设置子域），
/// 因此本类**没有**「把设置填回控件」的逻辑。这里只处理必须与用户交互的部分：
/// 字体文件选择框、字体缺失提示、取色器提示文案与提示条。
/// </para>
/// <para>
/// <b>自定义字体</b>：Avalonia 没有「把磁盘上的字体文件注册进字体管理器」的公开 API
/// （<c>FontManager.AddFontCollection</c> 只接受 <c>IFontCollection</c>，其初始化路径不对外开放），
/// 因此导入只做两件真实的事——从文件里读出字体族名（<see cref="FontFileInspector"/>）
/// 写进对应槽位，并把路径记入 <c>DisplaySettings.CustomFontPaths</c>（随工程往返）。
/// </para>
/// <para>
/// <b>由此带来的限制</b>：字体下拉里**不会**用该字体渲染预览（1.1.x 靠
/// <c>QFontDatabase.addApplicationFont</c> 做到了这一点），也无法判断某个导入的字体在本机是否真的可用。
/// 真正绘制文字的是渲染器原生库，它同时拿到族名与 <c>CustomFontPaths</c> 里的路径，
/// 因此功能本身（播放时用导入的字体显示）不受影响；这里绝不假装注册成功。
/// </para>
/// </remarks>
internal sealed partial class PlayerStylePage : UserControl
{
    private readonly PlayerStylePageViewModel _viewModel;
    private readonly INotificationHost _notifications;

    /// <summary>颜色行（重译时要同时更新标签与取色器提示）。</summary>
    private readonly List<ColorRow> _colorRows = [];

    /// <summary>四个字体槽位（「自定义…」被选中时要切回当前值并弹出文件框）。</summary>
    private readonly List<FontSlot> _fontSlots = [];

    /// <summary>创建播放器样式页。</summary>
    /// <param name="viewModel">页面 ViewModel。</param>
    /// <param name="notifications">提示条宿主。</param>
    internal PlayerStylePage(PlayerStylePageViewModel viewModel, INotificationHost notifications)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notifications);

        _viewModel = viewModel;
        _notifications = notifications;

        InitializeComponent();
        DataContext = viewModel;

        _colorRows.AddRange(
        [
            new ColorRow(BackgroundColorLabel, BackgroundColorPicker, "背景色:"),
            new ColorRow(NoteColorLabel, NoteColorPicker, "音名色:"),
            new ColorRow(LyricColorLabel, LyricColorPicker, "歌字色:"),
            new ColorRow(LyricTextColorLabel, LyricTextColorPicker, "歌词色:"),
            new ColorRow(PitchCurveColorLabel, PitchCurveColorPicker, "音高线颜色:"),
            new ColorRow(OtherTextColorLabel, OtherTextColorPicker, "其他文字色:"),
        ]);

        _fontSlots.AddRange(
        [
            new FontSlot(FontNoteCombo, () => viewModel.FontNote, value => viewModel.FontNote = value),
            new FontSlot(FontUstLyricCombo, () => viewModel.FontUstLyric, value => viewModel.FontUstLyric = value),
            new FontSlot(FontLrcCombo, () => viewModel.FontLrc, value => viewModel.FontLrc = value),
            new FontSlot(FontOtherCombo, () => viewModel.FontOther, value => viewModel.FontOther = value),
        ]);

        ApplyTexts();
    }

    /// <summary>切换语言后刷新本页文案与下拉项。</summary>
    internal void Retranslate()
    {
        _viewModel.Retranslate();
        ApplyTexts();
    }

    // ===================== 文案 =====================

    /// <summary>设置界面文案（UI 字符串一律经 <see cref="Translator.Tr"/>）。</summary>
    private void ApplyTexts()
    {
        StyleCard.Title = Translator.Tr("播放器样式");

        foreach (var row in _colorRows)
        {
            row.Label.Text = Translator.Tr(row.LabelSource);
            ToolTip.SetTip(row.Picker, string.Format(Translator.Tr("选择{0}"), row.Label.Text));
        }

        LyricPositionLabel.Text = Translator.Tr("歌词位置:");

        FontCard.Title = Translator.Tr("字体");
        FontNoteLabel.Text = Translator.Tr("音名字体:");
        FontUstLyricLabel.Text = Translator.Tr("歌字字体:");
        FontLrcLabel.Text = Translator.Tr("歌词字体:");
        FontOtherLabel.Text = Translator.Tr("其他文字字体:");

        OtherCard.Title = Translator.Tr("其他显示设置");
        PitchPlaceholderLabel.Text = Translator.Tr("音高间占位符:");
        SilentDisplayLabel.Text = Translator.Tr("静默时显示:");
        EndDisplayLabel.Text = Translator.Tr("结束时显示:");

        var customWatermark = Translator.Tr("自定义文字...");
        PitchCustomBox.Watermark = customWatermark;
        SilentCustomBox.Watermark = customWatermark;
        EndCustomBox.Watermark = customWatermark;
    }

    // ===================== 字体 =====================

    /// <summary>
    /// 字体下拉选中变化。
    /// </summary>
    /// <param name="sender">事件源（字体下拉框）。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 普通字体族由双向绑定写回设置；只有「自定义…」入口需要拦下来弹文件框
    /// （对应 1.1.x 的 <c>textActivated</c> 处理）。字体缺失的提示只在页面已加载后给出，
    /// 避免页面初始化时的回填触发一次无意义的提示。
    /// </remarks>
    private async void OnFontSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo ||
            e.AddedItems.Count == 0 ||
            e.AddedItems[0] is not string family)
        {
            return;
        }

        var slot = _fontSlots.Find(candidate => ReferenceEquals(candidate.Combo, combo));

        if (slot is null)
        {
            return;
        }

        if (string.Equals(family, _viewModel.CustomFontEntry, StringComparison.Ordinal))
        {
            // 「自定义…」不是合法字体族：先把下拉切回该槽位的当前值，
            // 再弹文件框；否则下拉会停在入口项上误导用户
            combo.SelectedItem = slot.Read();
            await ImportFontAsync(slot.Write);
            return;
        }

        if (IsLoaded && !PlayerStylePageViewModel.IsFontFamilyInstalled(family))
        {
            AppLogger.Warning($"字体未安装：{family}");

            _notifications.Notify(
                NotificationSeverity.Warning,
                Translator.Tr("提示"),
                string.Format(Translator.Tr("字体「{0}」未在本机安装，将使用回退字体显示"), family));
        }
    }

    /// <summary>选择字体文件并把族名写进指定槽位。</summary>
    /// <param name="applyFamily">把族名写入该槽位的动作。</param>
    /// <returns>任务。</returns>
    private async Task ImportFontAsync(Action<string> applyFamily)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;

        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Translator.Tr("选择字体文件"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Translator.Tr("字体文件 (*.ttf *.otf);;所有文件 (*.*)"))
                {
                    Patterns = ["*.ttf", "*.otf"],
                },
            ],
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

        var family = FontFileInspector.ResolveFamilyName(path);

        if (family is null)
        {
            AppLogger.Warning($"无法加载字体文件：{path}");

            _notifications.Notify(
                NotificationSeverity.Warning,
                Translator.Tr("提示"),
                string.Format(Translator.Tr("无法加载字体文件：{0}"), path));

            return;
        }

        // 先记路径（会重建候选列表），再写族名——否则族名还不在候选里，选中项会落空
        _viewModel.RememberCustomFontPath(path);
        applyFamily(family);
        _viewModel.RefreshFontFamilies();

        AppLogger.Info($"已导入字体文件：{path}（族名：{family}）");
    }

    // ===================== 内部类型 =====================

    /// <summary>一个颜色行：标签 + 取色器（重译用）。</summary>
    /// <param name="Label">颜色名标签。</param>
    /// <param name="Picker">取色器。</param>
    /// <param name="LabelSource">标签的中文原文。</param>
    private sealed record ColorRow(TextBlock Label, ColorPicker Picker, string LabelSource);

    /// <summary>一个字体槽位：下拉框 + 读写该槽位设置的动作。</summary>
    /// <param name="Combo">字体下拉框。</param>
    /// <param name="Read">读取当前槽位的字体族。</param>
    /// <param name="Write">把字体族写入当前槽位。</param>
    private sealed record FontSlot(ComboBox Combo, Func<string> Read, Action<string> Write);
}
