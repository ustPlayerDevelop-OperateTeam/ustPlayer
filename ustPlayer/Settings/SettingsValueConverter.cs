using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UstPlayer.Settings;

/// <summary>
/// 设置值与领域规则之间的转换 — 从 1.1.x <c>contracts.py</c> 的 <c>as_bool</c> /
/// <c>validate_hex_color</c> 与 <c>settings/player.py</c> 的 <c>migrate_value</c> 移植。
/// </summary>
internal static partial class SettingsValueConverter
{
    /// <summary>合法的 <c>#RRGGBB</c> 颜色。</summary>
    [GeneratedRegex("^#([0-9A-Fa-f]{6})$")]
    private static partial Regex HexColorPattern();

    /// <summary>
    /// 宽松布尔转换：支持 <c>0/1</c>、布尔、<c>true/yes/on</c>（大小写不敏感）。
    /// </summary>
    /// <param name="node">JSON 值节点。</param>
    /// <param name="fallback">无法解析时的默认值。</param>
    /// <returns>解析结果。</returns>
    /// <remarks>
    /// 与 1.1.x 的 <c>as_bool</c> 一致：数字按「非零即真」，
    /// 字符串只认列出的几个真值（其余一律为假，而不是回退默认值）。
    /// </remarks>
    internal static bool ToBool(JsonNode? node, bool fallback = false)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }

        // 按 JsonValueKind 分派，而不是直接 TryGetValue<T>：
        // JsonValue.Create(1) 内部保存的是 **int**，而 TryGetValue<double>() 不做数值转换，
        // 会返回 false —— 于是所有整数标志都会落到 fallback 分支，表现为「值被取反」。
        // 这类错误不会抛异常，只会让布尔设置静默用错默认值，非常难查。
        switch (value.GetValueKind())
        {
            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Number:
                // 数字：非零即真（与 1.1.x 的 as_bool 一致）
                return TryReadNumber(value) is { } number && number != 0;

            case JsonValueKind.String:
                var normalized = value.GetValue<string>().Trim();
                return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Equals("on", StringComparison.OrdinalIgnoreCase);

            default:
                // Null / Object / Array：与 1.1.x 的 as_bool 一样回退默认值
                return fallback;
        }
    }

    /// <summary>
    /// 从 JSON 数字按可能的后备类型逐个尝试读取。
    /// </summary>
    /// <param name="value">JSON 值（须为数字）。</param>
    /// <returns>数值；无法读取时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// <c>JsonValue</c> 会保留创建时的具体数值类型（<c>int</c> / <c>long</c> / <c>double</c> …），
    /// 且 <c>TryGetValue&lt;T&gt;</c> 不做隐式数值转换，因此必须逐个类型尝试。
    /// 也可用 <c>value.GetValue&lt;JsonElement&gt;().GetDouble()</c>，但那依赖 JsonElement 后备存储，
    /// 对程序内构造的 <c>JsonValue</c> 不成立。
    /// </remarks>
    private static double? TryReadNumber(JsonValue value)
    {
        if (value.TryGetValue<double>(out var asDouble))
        {
            return asDouble;
        }

        if (value.TryGetValue<int>(out var asInt))
        {
            return asInt;
        }

        if (value.TryGetValue<long>(out var asLong))
        {
            return asLong;
        }

        if (value.TryGetValue<decimal>(out var asDecimal))
        {
            return (double)asDecimal;
        }

        if (value.TryGetValue<float>(out var asFloat))
        {
            return asFloat;
        }

        return null;
    }

    /// <summary>判断是否为合法的 <c>#RRGGBB</c> 颜色。</summary>
    /// <param name="value">待判断的文本。</param>
    /// <returns>合法返回 <see langword="true"/>。</returns>
    internal static bool IsValidHexColor(string? value) =>
        !string.IsNullOrEmpty(value) && HexColorPattern().IsMatch(value.Trim());

    /// <summary>校验颜色，非法时回退默认值（对应 1.1.x <c>validate_hex_color</c>）。</summary>
    /// <param name="value">颜色文本。</param>
    /// <param name="fallback">默认颜色。</param>
    /// <returns>合法颜色或默认值。</returns>
    internal static string ValidateHexColor(string? value, string fallback) =>
        IsValidHexColor(value) ? value!.Trim() : fallback;

    /// <summary>
    /// 把旧版中文枚举值迁移为英文稳定 key。
    /// </summary>
    /// <param name="value">存储层读到的原始值。</param>
    /// <param name="validValues">合法英文 key 集合。</param>
    /// <param name="legacyMap">旧中文值 → 英文 key 的映射表。</param>
    /// <param name="fallback">既不在合法集合也不在映射表时的默认值。</param>
    /// <returns>英文稳定 key。</returns>
    /// <remarks>
    /// 与 1.1.x <c>PlayerSettings.migrate_value</c> 完全一致：先看是否已是合法 key，
    /// 再查旧值映射，最后回退默认。改动此处会让旧设置文件的枚举值读不出来。
    /// </remarks>
    internal static string MigrateEnumValue(
        string? value,
        string[] validValues,
        IReadOnlyDictionary<string, string> legacyMap,
        string fallback)
    {
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        foreach (var valid in validValues)
        {
            if (string.Equals(valid, value, StringComparison.Ordinal))
            {
                return valid;
            }
        }

        return legacyMap.TryGetValue(value, out var migrated) ? migrated : fallback;
    }
}
