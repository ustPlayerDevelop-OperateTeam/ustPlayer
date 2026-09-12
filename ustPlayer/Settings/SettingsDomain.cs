using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UstPlayer.Settings;

/// <summary>
/// 设置子域基类 — 从 1.1.x <c>core/settings/*.py</c> 的共有形态移植。
/// </summary>
/// <remarks>
/// <para>
/// 1.1.x 每个子域持有「属性 + <c>Signal</c> + <c>read_from</c> / <c>write_to</c> / <c>validate</c>」。
/// C# 侧对应「属性 + <see cref="INotifyPropertyChanged"/> + <see cref="ReadFrom"/> /
/// <see cref="WriteTo"/>」。
/// </para>
/// <para>
/// <b>没有 <c>Validate()</c></b>：1.1.x 需要它，是因为读取时是整批赋值、越界的枚举值
/// 只能在读取后再修一遍。C# 侧把校验放进属性 setter（非法值当场回退默认），
/// 因此不可能出现「已越界但尚未修正」的中间状态——少一个必须记得调用的步骤。
/// 1.1.x 的「旧中文枚举值 → 英文 key」迁移仍在 <see cref="ReadFrom"/> 中显式执行。
/// </para>
/// <para>
/// 属性 setter 一律「值未变化则不通知」，与 1.1.x 的
/// <c>if self._x != v:</c> 守卫一致——UI 依赖这一语义避免无谓刷新。
/// </para>
/// </remarks>
internal abstract class SettingsDomain<TSelf> : INotifyPropertyChanged
    where TSelf : SettingsDomain<TSelf>
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>从配置分组读取本子域的全部属性。</summary>
    /// <param name="config">分组 → 键值 的完整配置。</param>
    public abstract void ReadFrom(SettingsConfig config);

    /// <summary>把本子域的全部属性写回配置分组。</summary>
    /// <param name="config">分组 → 键值 的完整配置。</param>
    public abstract void WriteTo(SettingsConfig config);

    /// <summary>触发属性变更通知。</summary>
    /// <param name="propertyName">属性名（由编译器自动填充）。</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// 赋值并在值确实变化时通知。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="field">字段引用。</param>
    /// <param name="value">新值。</param>
    /// <param name="propertyName">属性名（由编译器自动填充）。</param>
    /// <returns>发生了变更返回 <see langword="true"/>。</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// 读取字符串键：非字符串类型回退默认值（对应 1.1.x 的 <c>_clean_str</c>）。
    /// </summary>
    /// <param name="group">分组。</param>
    /// <param name="key">键。</param>
    /// <param name="fallback">默认值。</param>
    /// <returns>读取到的字符串或默认值。</returns>
    protected static string ReadString(SettingsGroup? group, string key, string fallback)
    {
        if (group is null || !group.TryGetString(key, out var value))
        {
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// 读取布尔键：支持 <c>0/1</c>、<c>true/false</c>、<c>yes/no</c>、<c>on/off</c>
    /// （对应 1.1.x 的宽松布尔解析 <c>as_bool</c>）。
    /// </summary>
    /// <param name="group">分组。</param>
    /// <param name="key">键。</param>
    /// <param name="fallback">默认值。</param>
    /// <returns>解析出的布尔值或默认值。</returns>
    protected static bool ReadBool(SettingsGroup? group, string key, bool fallback)
    {
        if (group is null || !group.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        return SettingsValueConverter.ToBool(raw, fallback);
    }
}
