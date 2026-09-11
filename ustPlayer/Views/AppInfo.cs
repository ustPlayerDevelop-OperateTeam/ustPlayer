using System;
using System.Reflection;

namespace UstPlayer.Views;

/// <summary>
/// 应用信息（名称、版本号）。
/// </summary>
/// <remarks>
/// 版本号取自程序集信息版本：SDK 会自动在 <c>AssemblyInformationalVersion</c> 后
/// 附加 SourceLink 的 <c>+{git-sha}</c> 构建元数据，此处剥掉以保证展示稳定。
/// Phase 3 起 <c>UstPlayer.Models.AppInfo</c> 会扩展为完整契约
/// （含 APP_NAME / APP_AUTHOR / APP_COPYRIGHT）。
/// </remarks>
internal static class AppInfo
{
    /// <summary>展示用版本号（如 <c>2.0.0</c>）。</summary>
    internal static string Version { get; } = ResolveVersion();

    /// <summary>解析展示用版本号。</summary>
    /// <returns>语义化版本字符串；无法解析时回退 <c>0.0.0</c>。</returns>
    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
        return plusIndex > 0 ? informational[..plusIndex] : informational;
    }
}
