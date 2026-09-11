namespace UstPlayer.Platform;

/// <summary>
/// 平台适配层占位类型。Phase 3/4 在此落地 <c>IPlatformServices</c>、
/// <c>IAudioBackend</c>、<c>IThemeEffects</c>、<c>IAppPaths</c>、<c>IClock</c>、
/// <c>IVideoMuxer</c> 以及各平台实现与运行时装配。
///
/// 注意：本命名空间允许引用 Avalonia（窗口效果需要），因此不在分层约束
/// 测试的「逻辑层」名单内；但它同样不得反向依赖 Views / ViewModels。
/// </summary>
internal static class LayerMarker
{
}
