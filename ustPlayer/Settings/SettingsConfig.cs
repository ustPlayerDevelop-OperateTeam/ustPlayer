using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace UstPlayer.Settings;

/// <summary>
/// 一个设置分组（对应 <c>Settings.json</c> 里的一个段）。
/// </summary>
/// <remarks>
/// 直接以 <see cref="JsonObject"/> 为底层存储，好处是读写都能**原样保留**
/// 未知键与原始类型：1.1.x 写的文件可能含有 2.0 尚不认识的键，
/// 若读取时把值都强转成字符串再写回，那些键与类型会被静默改写。
/// </remarks>
internal sealed class SettingsGroup
{
    private readonly JsonObject _values;

    /// <summary>包装一个 JSON 对象为设置分组。</summary>
    /// <param name="values">底层对象。</param>
    internal SettingsGroup(JsonObject values)
    {
        _values = values;
    }

    /// <summary>本分组的键值对数量。</summary>
    internal int Count => _values.Count;

    /// <summary>取原始 JSON 值。</summary>
    /// <param name="key">键。</param>
    /// <returns>值节点；不存在返回 <see langword="null"/>。</returns>
    internal JsonNode? GetNode(string key) =>
        _values.TryGetPropertyValue(key, out var node) ? node : null;

    /// <summary>尝试取原始 JSON 值。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">值节点。</param>
    /// <returns>存在则返回 <see langword="true"/>。</returns>
    internal bool TryGetValue(string key, out JsonNode? value) =>
        _values.TryGetPropertyValue(key, out value);

    /// <summary>尝试取字符串值（非字符串返回 <see langword="false"/>）。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">字符串值。</param>
    /// <returns>读到字符串则返回 <see langword="true"/>。</returns>
    internal bool TryGetString(string key, out string value)
    {
        if (_values.TryGetPropertyValue(key, out var node) &&
            node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>尝试取字符串数组（非数组返回 <see langword="false"/>）。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">字符串列表。</param>
    /// <returns>读到数组则返回 <see langword="true"/>。</returns>
    internal bool TryGetStringList(string key, out List<string> value)
    {
        value = [];

        if (!_values.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
        {
            return false;
        }

        foreach (var item in array)
        {
            if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            {
                value.Add(text);
            }
        }

        return true;
    }

    /// <summary>写入字符串值。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    internal void SetString(string key, string value) => _values[key] = value;

    /// <summary>写入布尔值。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    /// <returns>发生了变更返回 <see langword="true"/>。</returns>
    internal bool SetBool(string key, bool value)
    {
        // **写成字符串 "1"/"0" 而不是 JSON 布尔**：1.1.x 的 [FileSettings] 与
        // [DisplaySettings] 就是这么存的（`"1" if self._x else "0"`），
        // 读取端用宽松布尔解析。写成 true/false 虽仍能被 2.0 读回，
        // 但会让「两个版本写出的文件一致」这条承诺失效，也会让用户 diff 出满屏差异。
        var text = value ? "1" : "0";
        var changed = !(_values.TryGetPropertyValue(key, out var existing) &&
                        existing is JsonValue jsonValue &&
                        jsonValue.TryGetValue<string>(out var current) &&
                        string.Equals(current, text, StringComparison.Ordinal));

        _values[key] = text;
        return changed;
    }

    /// <summary>写入字符串数组。</summary>
    /// <param name="key">键。</param>
    /// <param name="values">值。</param>
    internal void SetStringList(string key, IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        _values[key] = array;
    }
}

/// <summary>
/// <c>Settings.json</c> 的完整配置（分组 → 键值），与 1.1.x 的
/// <c>Dict[str, Dict[str, str]]</c> 结构对应。
/// </summary>
/// <remarks>
/// 只负责承载与查找，不做文件 I/O（那是 <c>SettingsStore</c> 的职责），
/// 也不含业务规则（那是各子域的职责）。
/// </remarks>
internal sealed class SettingsConfig
{
    private readonly JsonObject _root;

    /// <summary>包装一个 JSON 对象。</summary>
    /// <param name="root">根对象。</param>
    internal SettingsConfig(JsonObject root)
    {
        _root = root;
    }

    /// <summary>创建一个空配置。</summary>
    /// <returns>空配置。</returns>
    internal static SettingsConfig Empty() => new(new JsonObject());

    /// <summary>底层 JSON 对象（供存储层序列化）。</summary>
    internal JsonObject Root => _root;

    /// <summary>分组数量。</summary>
    internal int SectionCount => _root.Count;

    /// <summary>配置是否为空。</summary>
    internal bool IsEmpty => _root.Count == 0;

    /// <summary>
    /// 取一个分组；不存在时按需创建。
    /// </summary>
    /// <param name="section">分组名。</param>
    /// <param name="createIfMissing">不存在时是否创建。</param>
    /// <returns>分组；未创建且不存在时返回 <see langword="null"/>。</returns>
    internal SettingsGroup? GetSection(string section, bool createIfMissing = false)
    {
        if (_root.TryGetPropertyValue(section, out var node) && node is JsonObject existing)
        {
            return new SettingsGroup(existing);
        }

        if (!createIfMissing)
        {
            return null;
        }

        var created = new JsonObject();
        _root[section] = created;
        return new SettingsGroup(created);
    }

    /// <summary>是否含指定分组。</summary>
    /// <param name="section">分组名。</param>
    /// <returns>含则返回 <see langword="true"/>。</returns>
    internal bool HasSection(string section) =>
        _root.TryGetPropertyValue(section, out var node) && node is JsonObject;

    /// <summary>移除指定分组。</summary>
    /// <param name="section">分组名。</param>
    /// <returns>确实移除则返回 <see langword="true"/>。</returns>
    internal bool RemoveSection(string section) => _root.Remove(section);
}
