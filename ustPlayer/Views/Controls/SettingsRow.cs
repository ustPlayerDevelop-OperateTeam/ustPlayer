using Avalonia;
using Avalonia.Controls;

using FluentAvalonia.UI.Controls;

namespace UstPlayer.Views.Controls;

/// <summary>
/// 设置行 — 「左侧标题（可带说明与图标）+ 右侧控件」的一行，对应 Windows 11 设置应用的
/// SettingsCard 行样式。
/// </summary>
/// <remarks>
/// <para>
/// 用法：把要放在右侧的控件直接作为内容。
/// <code>
/// &lt;controls:SettingsRow Header="显示BPM"&gt;
///   &lt;ToggleSwitch IsChecked="{Binding Display.ShowBpm, Mode=TwoWay}" /&gt;
/// &lt;/controls:SettingsRow&gt;
/// </code>
/// </para>
/// <para>
/// <b>一行就是一张独立卡片</b>（浅底 + 描边 + 圆角，卡片之间留空隙），
/// 卡片内部不画分割线。卡片外观在 <c>Styles/SettingsControlsTheme.axaml</c>。
/// </para>
/// </remarks>
internal class SettingsRow : ContentControl
{
    /// <summary><see cref="Header"/> 的属性定义。</summary>
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Header));

    /// <summary><see cref="Description"/> 的属性定义。</summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Description));

    /// <summary><see cref="Icon"/> 的属性定义。</summary>
    public static readonly StyledProperty<IconSource?> IconProperty =
        AvaloniaProperty.Register<SettingsRow, IconSource?>(nameof(Icon));

    /// <summary>左侧主标题。</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>主标题下方的说明文字（可空）。</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>左侧图标（可空）。</summary>
    public IconSource? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
