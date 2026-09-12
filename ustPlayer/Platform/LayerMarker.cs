using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UstPlayer.Platform;

/// <summary>
/// 平台适配层：承载各平台差异的抽象与实现，使共享工程的其余部分免于平台分支。
/// </summary>
/// <remarks>
/// <para>
/// 本命名空间**允许**引用 Avalonia（窗口效果、透明度提示等需要），
/// 因此不在分层约束测试的「逻辑层」名单内；但它同样不得反向依赖
/// Views / ViewModels（该约束由 <c>ustPlayer.Tests/Architecture</c> 覆盖）。
/// </para>
/// <para>
/// Phase 3/4 将在此陆续落地：<c>IPlatformServices</c>、<c>IThemeEffects</c>、
/// <c>IAppPaths</c>、<c>IClock</c>、<c>IVideoMuxer</c> 以及音频后端的各平台实现。
/// 目前已有：<see cref="IAudioBackend"/>（音频窄接口）。
/// </para>
/// </remarks>
internal static class PlatformLayer
{
    /// <summary>本层已定义的类型一览（供架构测试与诊断使用）。</summary>
    /// <returns>本命名空间下的类型。</returns>
    internal static Type[] Types() =>
        typeof(PlatformLayer).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(PlatformLayer).Namespace)
            .ToArray();
}
