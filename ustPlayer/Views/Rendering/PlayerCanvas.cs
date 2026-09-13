using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UstPlayer.Views.Rendering;

/// <summary>
/// 播放器画布 —— 按 <see cref="PlayerCanvasRenderer"/> 的版式把一帧直接画到控件上。
/// </summary>
/// <remarks>
/// <para>
/// 用「控件自绘」而不是「渲染器出帧 → 位图 → Image」：
/// 后者的版式由 uPlRender 决定（它是为**视频导出**设计的），
/// 播出来的观感与 1.1.x 播放器不一致；播放器画面属于播放器自己的职责。
/// </para>
/// <para>
/// 依赖方向：本控件只做「把快照画出来」，不含任何时序或设置读取逻辑——
/// 它们由 <c>PlayerWindow</c> 组装成 <see cref="PlayerCanvasSnapshot"/> 传进来。
/// 因此版式可以脱离窗口与时序单独测试。
/// </para>
/// </remarks>
internal sealed class PlayerCanvas : Control
{
    /// <summary><see cref="Snapshot"/> 的属性定义。</summary>
    public static readonly StyledProperty<PlayerCanvasSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<PlayerCanvas, PlayerCanvasSnapshot?>(nameof(Snapshot));

    /// <summary><see cref="FontFamilyName"/> 的属性定义。</summary>
    public static readonly StyledProperty<string?> FontFamilyNameProperty =
        AvaloniaProperty.Register<PlayerCanvas, string?>(nameof(FontFamilyName));

    static PlayerCanvas()
    {
        // 快照或字体变化都要重画（AffectsRender 让 Avalonia 自动安排重绘）
        AffectsRender<PlayerCanvas>(SnapshotProperty, FontFamilyNameProperty);
    }

    /// <summary>要绘制的一帧；为 <see langword="null"/> 时只铺背景色。</summary>
    public PlayerCanvasSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    /// <summary>字体族名（空则用 Avalonia 默认字体）。</summary>
    public string? FontFamilyName
    {
        get => GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (Snapshot is not { } snapshot)
        {
            return;
        }

        PlayerCanvasRenderer.Render(context, bounds.Width, bounds.Height, snapshot, FontFamilyName);
    }
}
