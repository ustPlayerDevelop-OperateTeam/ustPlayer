using System;
using System.IO;

using UstPlayer.Diagnostics;

namespace UstPlayer.Platform;

/// <summary>
/// 伴奏音频后端工厂 — 对应 1.1.x <c>core/audio_backend.py</c> 的 <c>create_audio_backend</c>。
/// </summary>
/// <remarks>
/// <para>
/// 把「什么时候需要后端、后端起不来怎么办」收敛在一处：播放器只拿到一个
/// <see cref="IAudioBackend"/> 或 <see langword="null"/>，不需要知道 libvlc 的存在。
/// </para>
/// <para>
/// 返回 <see langword="null"/> 是**正常路径**（没有伴奏，或后端不可用）：
/// 此时播放时序按墙钟走，画面与歌词完全正常，只是没有伴奏声。
/// </para>
/// </remarks>
internal static class AudioBackendFactory
{
    /// <summary>
    /// 按伴奏路径创建后端。
    /// </summary>
    /// <param name="musicPath">伴奏文件路径；为空表示不配伴奏。</param>
    /// <returns>已开始加载的后端；不需要或不可用时返回 <see langword="null"/>。</returns>
    internal static IAudioBackend? Create(string? musicPath)
    {
        if (string.IsNullOrWhiteSpace(musicPath))
        {
            // 没配伴奏：不创建后端，播放器直接走墙钟计时
            return null;
        }

        if (!File.Exists(musicPath))
        {
            // 文件不存在时不必拉起 libvlc——直接当「没有伴奏」处理并留一条明确的日志
            AppLogger.Warning($"伴奏文件不存在，本次播放无伴奏声：{musicPath}");
            return null;
        }

        try
        {
            var backend = new LibVlcAudioBackend();
            backend.Load(musicPath);

            AppLogger.Info($"伴奏后端已创建（libvlc）：{musicPath}");
            return backend;
        }
        catch (Exception exception)
        {
            // 原生库缺失（Linux 未装 libvlc）、解码器不可用等：降级为无伴奏，
            // 而不是让整个播放失败——画面与歌词时序仍然可用
            AppLogger.Error(
                $"伴奏后端创建失败，本次播放将按墙钟计时且无伴奏声：{musicPath}",
                exception);

            return null;
        }
    }
}
