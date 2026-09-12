using Avalonia.Platform;

namespace UstPlayer.Views;

/// <summary>
/// 渲染器输出与 Avalonia 位图之间的像素格式约定。
/// </summary>
/// <remarks>
/// <para>
/// uPlRender 的 <c>up_render_to_buffer</c> 输出 **RGBA8888、预乘 alpha**
/// （见根 <c>API_Docs.md</c>）。Avalonia 侧对应的格式是
/// <see cref="PixelFormat.Rgba8888"/>，配 <see cref="AlphaFormat.Premul"/>。
/// </para>
/// <para>
/// 两者匹配时，把渲染器输出**直接**拷进 <c>WriteableBitmap.Lock()</c> 的缓冲区即可显示，
/// 不需要任何逐像素转换——这是 Spike 0b 要验证的前提。
/// 若格式不匹配（例如误用 <see cref="AlphaFormat.Unpremul"/>），画面会发白或发暗，
/// 因此这里显式声明并在测试中核对。
/// </para>
/// </remarks>
internal static class RendererPixelFormat
{
    /// <summary>Avalonia 位图格式（RGBA8888 + 预乘 alpha）。</summary>
    internal static readonly PixelFormat BitmapFormat = PixelFormat.Rgba8888;

    /// <summary>Avalonia 位图 alpha 格式。</summary>
    internal static readonly AlphaFormat BitmapAlphaFormat = AlphaFormat.Premul;

    /// <summary>每像素字节数（RGBA 4 通道）。</summary>
    internal const int BytesPerPixel = 4;

    /// <summary>计算指定尺寸所需的缓冲字节数。</summary>
    /// <param name="width">宽（像素）。</param>
    /// <param name="height">高（像素）。</param>
    /// <returns>字节数。</returns>
    internal static int BufferSize(int width, int height) => width * height * BytesPerPixel;
}
