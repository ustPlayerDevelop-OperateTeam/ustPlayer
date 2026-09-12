using System;
using System.Collections.Generic;
using System.Linq;

using UstPlayer.I18n;

namespace UstPlayer.ViewModels;

/// <summary>
/// 下拉选项 — 存储层稳定 key + 当前语言的显示文案。
/// </summary>
/// <remarks>
/// <para>
/// 设置层只允许存英文 key（<c>top</c> / <c>r</c> / <c>custom</c> …，见
/// <see cref="Settings.SettingsEnums"/>），而界面必须显示译文。因此下拉项的
/// <see cref="Text"/> 是**可观察**的：切换语言时原地换文案，选中项（对象本身）不受影响，
/// 不会像「重建列表」那样把选中项清空、再被双向绑定写回存储层。
/// </para>
/// </remarks>
internal sealed class ChoiceOption : ViewModelBase
{
    private string _text;

    /// <summary>创建选项。</summary>
    /// <param name="key">存储层稳定 key。</param>
    /// <param name="sourceText">中文原文（翻译查表用）。</param>
    internal ChoiceOption(string key, string sourceText)
    {
        Key = key;
        SourceText = sourceText;
        _text = Translator.Tr(sourceText);
    }

    /// <summary>存储层稳定 key。</summary>
    internal string Key { get; }

    /// <summary>中文原文（<see cref="Retranslate"/> 重新查表用）。</summary>
    internal string SourceText { get; }

    /// <summary>当前语言的显示文案。</summary>
    internal string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    /// <summary>按当前语言重设显示文案。</summary>
    internal void Retranslate() => Text = Translator.Tr(SourceText);
}

/// <summary>
/// 把一个「存储层 key」型设置投影成一组可绑定下拉项。
/// </summary>
/// <remarks>
/// <para>
/// 这是**绑定投影**，不是 1.1.x 的 <c>sync_all_from_settings()</c>：
/// 设置子域仍是唯一事实源，本类只在「设置里的 key ↔ 下拉项对象」之间做翻译，
/// 且没有任何「把设置填回控件」的批量同步方法。之所以需要它，是因为
/// <c>ComboBox.SelectedItem</c> 无法既绑定 key 又显示译文。
/// </para>
/// <para>
/// <see cref="Options"/> 是**稳定实例**（不随语言重建），所以切换语言不会打断选中项。
/// 设置被外部改动（例如导入工程）时，由页面 ViewModel 转发
/// <see cref="NotifySelectionChanged"/>，绑定随之重新读取 <see cref="Selected"/>。
/// </para>
/// </remarks>
internal sealed class ChoiceGroup : ViewModelBase
{
    /// <summary>「自定义文字」的存储 key（与 <see cref="Settings.SettingsEnums"/> 一致）。</summary>
    internal const string CustomKey = "custom";

    private readonly Func<string> _read;
    private readonly Action<string> _write;

    /// <summary>创建选项组。</summary>
    /// <param name="read">读取设置里的当前 key。</param>
    /// <param name="write">把用户选中的 key 写回设置。</param>
    /// <param name="options">选项（key, 中文原文），顺序即界面顺序。</param>
    internal ChoiceGroup(
        Func<string> read,
        Action<string> write,
        params (string Key, string SourceText)[] options)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(options);

        _read = read;
        _write = write;
        Options = [.. options.Select(option => new ChoiceOption(option.Key, option.SourceText))];
    }

    /// <summary>下拉候选项（稳定实例，语言切换时只换文案）。</summary>
    internal IReadOnlyList<ChoiceOption> Options { get; }

    /// <summary>当前选中项（由设置里的 key 投影而来；key 不合法时回退第一项）。</summary>
    internal ChoiceOption Selected
    {
        get => Find(_read()) ?? Options[0];
        set
        {
            if (value is not null)
            {
                _write(value.Key);
            }
        }
    }

    /// <summary>是否选中了「自定义文字」项（用于显示配套的自定义文本框）。</summary>
    internal bool IsCustom => Selected.Key == CustomKey;

    /// <summary>按当前语言重设全部选项文案。</summary>
    internal void Retranslate()
    {
        foreach (var option in Options)
        {
            option.Retranslate();
        }

        NotifySelectionChanged();
    }

    /// <summary>通知界面重读 <see cref="Selected"/> / <see cref="IsCustom"/>（设置被外部改动时用）。</summary>
    internal void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsCustom));
    }

    /// <summary>按键查找选项。</summary>
    /// <param name="key">存储层 key。</param>
    /// <returns>选项；未收录时返回 <see langword="null"/>。</returns>
    private ChoiceOption? Find(string? key) =>
        Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.Ordinal));
}
