namespace UstPlayer.Diagnostics;

/// <summary>
/// 诊断层占位类型。Phase 3 在此落地 <c>AppLogger</c> 与 <c>ProgramPaths</c>：
/// 日志与用户数据目录解析（程序目录优先，不可写时回退平台标准用户目录，
/// 并保留读取 1.1.x 的 <c>%LOCALAPPDATA%\ustPlayer</c> 的能力）。
/// </summary>
internal static class LayerMarker
{
}
