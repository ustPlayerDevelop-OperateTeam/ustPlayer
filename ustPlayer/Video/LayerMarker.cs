namespace UstPlayer.Video;

/// <summary>
/// 视频导出层占位类型。Phase 3/4 在此落地 <c>VideoExporter</c>、
/// <c>RenderConfigBuilder</c>（组装 up_set_config 的 RenderConfig JSON）与
/// <c>IVideoMuxer</c> / <c>FfmpegMuxer</c>（混流；Android 端无子进程，用空实现）。
/// </summary>
internal static class LayerMarker
{
}
