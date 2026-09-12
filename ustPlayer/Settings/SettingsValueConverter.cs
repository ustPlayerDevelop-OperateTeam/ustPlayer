using System;
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
        switch (node)
        {
            case null:
                return fallback;

            case JsonValue value when value.TryGetValue<bool>(out var boolean):
                return boolean;

            case JsonValue value when value.TryGetValue<double>(out var number):
                return number != 0;

            case JsonValue value when value.TryGetValue<string>(out var text):
                var normalized = text.Trim();
                return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Equals("on", StringComparison.OrdinalIgnoreCase);

            default:
                return fallback;
        }
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
