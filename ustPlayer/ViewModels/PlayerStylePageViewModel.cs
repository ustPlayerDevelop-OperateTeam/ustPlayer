using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

using Avalonia.Media;

using UstPlayer.I18n;
using UstPlayer.Settings.Domains;

namespace UstPlayer.ViewModels;

/// <summary>
/// 播放器样式页 ViewModel（对应 1.1.x <c>player_style_page.py</c>）：
/// 颜色、歌词位置、四类字体与其他显示设置。
/// </summary>
/// <remarks>
/// <para>
/// <b>颜色与自定义文字直接双向绑定设置子域</b>（XAML 里 <c>{Binding Color.BackgroundColor, Mode=TwoWay}</c>），
/// 本类不提供任何「把设置填回控件」的同步方法——设置子域自身会通知变更，
/// 导入工程后界面自动刷新（见 <c>docs/plan-deviations.md</c> D8）。
/// </para>
/// <para>
/// 唯独两类控件需要一层绑定投影：**下拉框**（选中项是 key + 译文的对象，见
/// <see cref="ChoiceGroup"/>）与**字体下拉框**（候选集合会随「导入字体」变化）。
/// 两者都只在「设置 ⇄ 界面值」之间做翻译，设置子域仍是唯一事实源。
/// </para>
/// </remarks>
internal sealed class PlayerStylePageViewModel : ViewModelBase
{
    /// <summary>
    /// 内置字体族（与 1.1.x 的 <c>_BUILTIN_FONTS</c> 逐字一致）。
    /// </summary>
    /// <remarks>
    /// <b>字体族名不是用户文案，不翻译</b>：它要原样传给渲染器与操作系统字体管理器。
    /// </remarks>
    internal static readonly IReadOnlyList<string> BuiltinFontFamilies =
        ["等线", "微软雅黑", "黑体", "楷体", "宋体"];

    private readonly ColorSettings _color;
    private readonly DisplaySettings _display;
    private readonly PlayerSettings _player;

    /// <summary>创建播放器样式页 ViewModel。</summary>
    /// <param name="services">组合根。</param>
    internal PlayerStylePageViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _color = services.Settings.Color;
        _display = services.Settings.Display;
        _player = services.Settings.Player;

        LyricPositionChoice = new ChoiceGroup(
            () => _player.LyricPosition,
            value => _player.LyricPosition = value,
            ("top", "上"),
            ("bottom", "下"));

        PitchPlaceholderChoice = new ChoiceGroup(
            () => _player.PitchPlaceholder,
            value => _player.PitchPlaceholder = value,
            ("none", "无"),
            ("dash", "-"),
            ("custom", "自定义文字"));

        SilentDisplayChoice = new ChoiceGroup(
            () => _player.SilentDisplay,
            value => _player.SilentDisplay = value,
            ("r", "R"),
            ("dash", "-"),
            ("custom", "自定义文字"),
            ("none", "什么都不显示"));

        EndDisplayChoice = new ChoiceGroup(
            () => _player.EndDisplay,
            value => _player.EndDisplay = value,
            ("end", "END"),
            ("dash", "-"),
            ("custom", "自定义文字"),
            ("none", "什么都不显示"));

        _player.PropertyChanged += OnPlayerSettingsChanged;
        _display.PropertyChanged += OnDisplaySettingsChanged;

        RefreshFontFamilies();
    }

    /// <summary>颜色子域（六个颜色直接绑到它）。</summary>
    internal ColorSettings Color => _color;

    /// <summary>显示子域（四个字体槽位）。</summary>
    internal DisplaySettings Display => _display;

    /// <summary>播放器子域（歌词位置与其他显示方式）。</summary>
    internal PlayerSettings Player => _player;

    /// <summary>歌词位置下拉（<c>top</c> / <c>bottom</c>）。</summary>
    internal ChoiceGroup LyricPositionChoice { get; }

    /// <summary>音高间占位符下拉。</summary>
    internal ChoiceGroup PitchPlaceholderChoice { get; }

    /// <summary>静默时显示下拉。</summary>
    internal ChoiceGroup SilentDisplayChoice { get; }

    /// <summary>结束时显示下拉。</summary>
    internal ChoiceGroup EndDisplayChoice { get; }

    /// <summary>
    /// 字体候选集合（内置字体 + 已导入字体 + 「自定义…」入口）。
    /// </summary>
    /// <remarks>
    /// 必须是**稳定实例**：整体替换集合会让下拉选中项变成 <see langword="null"/>，
    /// 而双向绑定会把这个 <see langword="null"/> 写回设置，把用户选的字体清掉
    /// （与文件页编码下拉是同一个坑）。因此这里只做原地增删。
    /// </remarks>
    internal ObservableCollection<string> FontFamilies { get; } = [];

    /// <summary>「自定义…」入口的当前文案（既是下拉项，也是哨兵值）。</summary>
    internal string CustomFontEntry => Translator.Tr("自定义…");

    /// <summary>音名字体族。</summary>
    internal string FontNote
    {
        get => NormalizeFontFamily(_display.FontNote);
        set => ApplyFontFamily(value, family => _display.FontNote = family);
    }

    /// <summary>歌字字体族。</summary>
    internal string FontUstLyric
    {
        get => NormalizeFontFamily(_display.FontUstLyric);
        set => ApplyFontFamily(value, family => _display.FontUstLyric = family);
    }

    /// <summary>歌词字体族。</summary>
    internal string FontLrc
    {
        get => NormalizeFontFamily(_display.FontLrc);
        set => ApplyFontFamily(value, family => _display.FontLrc = family);
    }

    /// <summary>其他文字字体族。</summary>
    internal string FontOther
    {
        get => NormalizeFontFamily(_display.FontOther);
        set => ApplyFontFamily(value, family => _display.FontOther = family);
    }

    /// <summary>
    /// 判断字体族在本机是否已安装。
    /// </summary>
    /// <param name="family">字体族名。</param>
    /// <returns>可解析出字形返回 <see langword="true"/>。</returns>
    /// <remarks>
    /// 缺失字体会被静默回退成默认字体（用户只看到「字体没生效」），因此界面要显式提示。
    /// </remarks>
    internal static bool IsFontFamilyInstalled(string? family) =>
        !string.IsNullOrWhiteSpace(family) &&
        FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out _);

    /// <summary>把导入的字体文件路径记入设置（已存在则不重复添加）。</summary>
    /// <param name="path">字体文件路径。</param>
    internal void RememberCustomFontPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _display.CustomFontPaths.Contains(path))
        {
            return;
        }

        // 整体赋值而不是原地 Add：CustomFontPaths 的 setter 才会发出变更通知
        _display.CustomFontPaths = [.. _display.CustomFontPaths, path];

        // setter 的变更通知会经 OnDisplaySettingsChanged 重建字体候选，
        // 调用方可紧接着把族名写进槽位
    }

    /// <summary>按当前语言重设下拉项文案（选中项本身不受影响）。</summary>
    internal void Retranslate()
    {
        LyricPositionChoice.Retranslate();
        PitchPlaceholderChoice.Retranslate();
        SilentDisplayChoice.Retranslate();
        EndDisplayChoice.Retranslate();

        // 「自定义…」入口的文案也随语言变化
        RefreshFontFamilies();
    }

    /// <summary>
    /// 重建字体候选：内置字体 + 已导入字体（按路径读族名）+「自定义…」。
    /// </summary>
    /// <remarks>
    /// 原地增删，不整体替换 <see cref="FontFamilies"/>；完成后通知四个槽位重读选中值，
    /// 这样导入工程（先写字体族、后写字体路径）也不会出现「界面停在默认字体」。
    /// </remarks>
    internal void RefreshFontFamilies()
    {
        var desired = new List<string>(BuiltinFontFamilies);

        foreach (var path in _display.CustomFontPaths)
        {
            var family = FontFileInspector.ResolveFamilyName(path);

            if (family is not null && !desired.Contains(family))
            {
                desired.Add(family);
            }
        }

        desired.Add(CustomFontEntry);

        for (var index = FontFamilies.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(FontFamilies[index]))
            {
                FontFamilies.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            if (!FontFamilies.Contains(desired[index]))
            {
                FontFamilies.Insert(Math.Min(index, FontFamilies.Count), desired[index]);
            }
        }

        OnPropertyChanged(nameof(FontNote));
        OnPropertyChanged(nameof(FontUstLyric));
        OnPropertyChanged(nameof(FontLrc));
        OnPropertyChanged(nameof(FontOther));
    }

    /// <summary>把设置里的字体族投影为下拉框能选中的值。</summary>
    /// <param name="family">设置里的字体族（可能为空或不在候选里）。</param>
    /// <returns>候选之一（不在候选里时回退第一个内置字体）。</returns>
    private string NormalizeFontFamily(string? family) =>
        !string.IsNullOrWhiteSpace(family) && FontFamilies.Contains(family)
            ? family
            : BuiltinFontFamilies[0];

    /// <summary>把下拉框选中的字体族写回设置（过滤哨兵项与非法值）。</summary>
    /// <param name="family">下拉框给出的值。</param>
    /// <param name="write">实际写入动作。</param>
    private void ApplyFontFamily(string? family, Action<string> write)
    {
        // 下拉框在重建候选时可能短暂给出 null；「自定义…」是入口而不是字体族。
        // 两者都不能写回设置：设置层的 CleanFont 会把 null 变成空串（字体被清掉），
        // 而「自定义…」会被当成一个不存在的字体名。
        if (string.IsNullOrWhiteSpace(family) ||
            string.Equals(family, CustomFontEntry, StringComparison.Ordinal) ||
            !FontFamilies.Contains(family))
        {
            return;
        }

        write(family);
    }

    /// <summary>播放器设置变化 → 通知对应下拉框重读选中项（导入工程时用）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnPlayerSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerSettings.LyricPosition):
                LyricPositionChoice.NotifySelectionChanged();
                break;

            case nameof(PlayerSettings.PitchPlaceholder):
                PitchPlaceholderChoice.NotifySelectionChanged();
                break;

            case nameof(PlayerSettings.SilentDisplay):
                SilentDisplayChoice.NotifySelectionChanged();
                break;

            case nameof(PlayerSettings.EndDisplay):
                EndDisplayChoice.NotifySelectionChanged();
                break;

            default:
                break;
        }
    }

    /// <summary>显示设置变化 → 重建字体候选或通知字体下拉框重读。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnDisplaySettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.CustomFontPaths):
                RefreshFontFamilies();
                break;

            case nameof(DisplaySettings.FontNote):
                OnPropertyChanged(nameof(FontNote));
                break;

            case nameof(DisplaySettings.FontUstLyric):
                OnPropertyChanged(nameof(FontUstLyric));
                break;

            case nameof(DisplaySettings.FontLrc):
                OnPropertyChanged(nameof(FontLrc));
                break;

            case nameof(DisplaySettings.FontOther):
                OnPropertyChanged(nameof(FontOther));
                break;

            default:
                break;
        }
    }
}
