namespace UstPlayer.Interop;

/// <summary>
/// 原生互操作层占位类型。Phase 3 在此落地 uPlRender 渲染器的 P/Invoke 封装：
/// <c>UplRenderInterop</c>（<c>[LibraryImport]</c> + <c>SafeHandle</c> 管理 u64 句柄）、
/// <c>UplRenderContext</c>、<c>UplRenderLoader</c>（按平台解析
/// <c>ustplayer_renderer.{dll,dylib,so}</c>）与 <c>RendererError</c>。
/// </summary>
internal static class LayerMarker
{
}
