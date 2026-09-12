using System;
using System.Collections.Generic;
using System.IO;

using UstPlayer.Diagnostics;
using UstPlayer.Models;
using UstPlayer.Platform;
using UstPlayer.Settings;
using UstPlayer.Timing;

namespace UstPlayer.Views;

/// <summary>
/// 播放器启动门面 — 从 1.1.x <c>NotePlayerLauncher</c> 的「组装 + 启动」职责移植。
/// </summary>
/// <remarks>
/// <para>
/// 把「设置 → 播放参数 → 资源解析（歌词 / 伴奏）→ 显示窗口」这条编排收在一个地方，
/// 使 UI 只需一句 <see cref="Launch"/>；也让这条链路的关键决策（歌词取哪、伴奏怎么降级）
/// 可以被单独测试。
/// </para>
/// <para>
/// 画面与窗口的职责仍在 <see cref="PlayerWindow"/> / <see cref="PlayerFrameCompositor"/>，
/// 本类**不含**任何渲染或时序逻辑。
/// </para>
/// </remarks>
internal static class PlayerLauncher
{
    /// <summary>
    /// 按当前设置启动全屏播放器。
    /// </summary>
    /// <param name="settings">设置管理器（提供播放参数与显示开关）。</param>
    /// <param name="ustInfo">已解析的 UST。</param>
    /// <returns>已显示的播放窗口。</returns>
    /// <exception cref="ArgumentNullException">参数为空。</exception>
    /// <exception cref="Interop.RendererException">渲染器原生库缺失或配置失败。</exception>
    internal static PlayerWindow Launch(SettingsManager settings, UstInfo ustInfo)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ustInfo);

        var parameters = settings.BuildLaunchParams(ustInfo);
        var lrcLines = ResolveLrcLines(parameters);

        return PlayerWindow.Show(parameters, settings, lrcLines, CreateAudioBackend(parameters.Style.MusicPath));
    }

    /// <summary>
    /// 解析播放器要显示的歌词行。
    /// </summary>
    /// <param name="parameters">播放参数。</param>
    /// <returns>按时间升序的歌词行；未配置或文件缺失时返回空列表。</returns>
    /// <remarks>
    /// 歌词路径来自 <see cref="PlayerStyle.LrcPath"/>（即设置里的 <c>lrc_path</c>）。
    /// <b>歌词缺失不是错误</b>：1.1.x 同样允许没有歌词，播放器只是不显示歌词行，
    /// 因此这里降级为空列表而不抛出。
    /// </remarks>
    internal static IReadOnlyList<LrcLine> ResolveLrcLines(PlayerLaunchParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var path = parameters.Style.LrcPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        if (!File.Exists(path))
        {
            AppLogger.Warning($"歌词文件不存在，已跳过：{path}");
            return [];
        }

        var lines = LrcParser.ParseFile(path);
        AppLogger.Info($"已载入歌词 {lines.Count} 行：{path}");

        return lines;
    }

    /// <summary>
    /// 创建伴奏音频后端。
    /// </summary>
    /// <param name="musicPath">伴奏路径（可为空）。</param>
    /// <returns>
    /// 当前**恒为 <see langword="null"/>**——音频后端尚未选定，见下。
    /// </returns>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>尚未实现</b>：音频库选型未定（LibVLCSharp / NAudio / FFmpeg.AutoGen 各有取舍，
    /// 且 NAudio 仅 Windows，与跨平台目标冲突），决策记录见
    /// <c>docs/plan-deviations.md</c> 的 D3。
    /// </para>
    /// <para>
    /// 返回 <see langword="null"/> 时 <see cref="PlaybackSession"/> 立即按**墙钟计时**，
    /// 画面与歌词时序因此是完整可用的，只是没有伴奏声——这是刻意的降级，
    /// 而不是「音频静默失效」。配了伴奏却听不到声音属于预期行为，故此处显式告警，
    /// 避免用户误判为 bug。
    /// </para>
    /// </remarks>
    internal static IAudioBackend? CreateAudioBackend(string? musicPath)
    {
        if (!string.IsNullOrWhiteSpace(musicPath))
        {
            AppLogger.Warning(
                $"已配置伴奏但音频后端尚未实现，本次播放将按墙钟计时且无伴奏声：{musicPath}");
        }

        return null;
    }
}
