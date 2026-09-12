using Avalonia;
using Avalonia.Controls;

namespace UstPlayer.Views.Controls;

/// <summary>
/// 设置分组 — 卡片**外**的分组标题 + 一张容纳若干 <see cref="SettingsRow"/> 的卡片。
/// </summary>
/// <remarks>
/// <para>
/// 与 Windows 11 设置应用一致：分组标题在卡片之外（不是卡片内的标题栏），
/// 卡片本身是扁平浅底 + 1px 描边，行与行之间用细分隔线。
/// </para>
/// <para>
/// <see cref="Header"/> 为空时只渲染卡片，不占标题高度。
/// </para>
/// </remarks>
internal class SettingsSection : ContentControl
{
    /// <summary><see cref="Header"/> 的属性定义。</summary>
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SettingsSection, string?>(nameof(Header));

    /// <summary>分组标题（显示在卡片上方）。</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
