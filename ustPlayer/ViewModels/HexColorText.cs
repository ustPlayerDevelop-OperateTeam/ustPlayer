using Avalonia.Media;

using UstPlayer.Settings;

namespace UstPlayer.ViewModels;

/// <summary>
/// 存储层的 <c>#RRGGBB</c> 颜色文本与 Avalonia 颜色之间的转换。
/// </summary>
/// <remarks>
/// <para>
/// 颜色在设置里**始终是字符串**（<c>#RRGGBB</c>，见 <c>ColorSettings</c>），
/// 合法性由设置层 setter 兜底（非法值回退默认）。这里只做「文本 ⇄ 颜色」的纯计算，
/// 供界面上的色块预览与取色器复用。
/// </para>
/// <para>
/// 只认 <c>#RRGGBB</c>：带 alpha 的 <c>#AARRGGBB</c>、颜色名（<c>red</c>）都不接受——
/// 它们写回设置层会被判为非法并回退默认，与其静默丢值，不如在这里就判定为不合法。
/// </para>
/// </remarks>
internal static class HexColorText
{
    /// <summary>把 <c>#RRGGBB</c> 文本解析为颜色。</summary>
    /// <param name="text">待解析的文本。</param>
    /// <param name="color">解析结果。</param>
    /// <returns>合法返回 <see langword="true"/>。</returns>
    internal static bool TryParse(string? text, out Color color)
    {
        color = default;

        if (!SettingsValueConverter.IsValidHexColor(text))
        {
            return false;
        }

        return Color.TryParse(text!.Trim(), out color);
    }

    /// <summary>把颜色格式化为存储层使用的 <c>#RRGGBB</c> 文本。</summary>
    /// <param name="color">颜色。</param>
    /// <returns>大写十六进制文本。</returns>
    internal static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
