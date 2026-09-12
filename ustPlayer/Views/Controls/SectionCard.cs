using Avalonia;
using Avalonia.Controls;

namespace UstPlayer.Views.Controls;

/// <summary>
/// 卡片式分区控件 — 主题色竖线 + 分区标题 + 内容区（移植自 1.1.x 的 <c>SectionCard</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 外观（圆角实底、柔和投影、标题前竖线）定义在
/// <c>Styles/SectionCardTheme.axaml</c> 的 <see cref="Avalonia.Controls.ControlTheme"/> 里，
/// 本类只暴露 <see cref="Title"/> 一个属性。
/// </para>
/// <para>
/// 用 <see cref="ContentControl"/> 而不是自绘：内容照常参与布局与数据绑定，
/// 主题切换（亮/暗）由主题资源自动处理，不需要像 Qt 那样手写
/// <c>_normalBackgroundColor</c> 之类的重绘逻辑。
/// </para>
/// </remarks>
internal class SectionCard : ContentControl
{
    /// <summary><see cref="Title"/> 的属性定义。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SectionCard, string?>(nameof(Title));

    /// <summary>分区标题。</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
