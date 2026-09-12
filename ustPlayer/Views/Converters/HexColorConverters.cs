using System;
using System.Globalization;

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

using UstPlayer.ViewModels;

namespace UstPlayer.Views.Converters;

/// <summary>
/// <c>#RRGGBB</c> 文本 ⇄ <see cref="Color"/> — 供取色器（<c>ColorPicker.Color</c>）双向绑定设置里的颜色字符串。
/// </summary>
/// <remarks>
/// Avalonia 的取色器控件只认 <see cref="Color"/>，而设置层按 1.1.x 的约定只存字符串，
/// 因此必须在这两者之间转换。非法文本返回 <see cref="BindingOperations.DoNothing"/>：
/// 宁可让控件保持原值，也不要把颜色静默改成白色。
/// </remarks>
internal sealed class HexToColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && HexColorText.TryParse(text, out var color)
            ? color
            : BindingOperations.DoNothing;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color color ? HexColorText.Format(color) : BindingOperations.DoNothing;
}

/// <summary>
/// <c>#RRGGBB</c> 文本 → 画刷 — 供输入框旁的颜色预览色块使用（单向）。
/// </summary>
internal sealed class HexToBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && HexColorText.TryParse(text, out var color)
            ? new SolidColorBrush(color)
            : BindingOperations.DoNothing;

    /// <summary>不支持反向转换：色块是只读预览，颜色经取色器与文本框写回。</summary>
    /// <param name="value">目标值。</param>
    /// <param name="targetType">源类型。</param>
    /// <param name="parameter">参数。</param>
    /// <param name="culture">区域。</param>
    /// <returns>永不返回。</returns>
    /// <exception cref="NotSupportedException">总是抛出。</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("颜色预览色块是只读的，请通过取色器或颜色文本框修改颜色");
}
