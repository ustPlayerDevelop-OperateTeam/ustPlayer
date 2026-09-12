using System;
using System.Collections.Generic;

namespace UstPlayer.Diagnostics;

/// <summary>
/// 命令行启动参数（解析结果）。
/// </summary>
/// <remarks>
/// <para>
/// 目前只有一个显式开关 <c>--play &lt;UST 路径&gt;</c>：跳过主窗口，直接全屏播放该 UST。
/// 它同时承担两个用途——给用户的「不进界面直接播」入口，以及让播放链路能由脚本用
/// **真实进程**验证（窗口无法在无头单元测试里实例化，见
/// <c>docs/adr-0002-window-chrome.md</c>）。
/// </para>
/// <para>
/// <b>刻意不支持裸路径</b>：1.1.x 允许把 <c>.uplr</c> / <c>.uprd</c> 路径直接作为首个参数，
/// 但那需要「导入工程 → 触发设置信号 → UI 同步」这整套流程（属 Phase 3/5）。
/// 在这里半实现一个裸路径会让人以为工程导入已可用，因此只认显式开关。
/// </para>
/// </remarks>
/// <param name="PlayUstPath">要直接播放的 UST 路径；未指定时为 <see langword="null"/>。</param>
internal sealed record StartupOptions(string? PlayUstPath)
{
    /// <summary>播放模式的开关名。</summary>
    internal const string PlaySwitch = "--play";

    /// <summary>默认启动参数（打开主窗口）。</summary>
    internal static readonly StartupOptions Default = new((string?)null);

    /// <summary>是否处于「直接播放」模式。</summary>
    internal bool IsPlayerMode => PlayUstPath is not null;

    /// <summary>
    /// 解析命令行参数。
    /// </summary>
    /// <param name="args">命令行参数（不含可执行文件本身）。</param>
    /// <returns>解析结果。</returns>
    /// <remarks>
    /// 无法识别的参数一律**忽略**而不报错：平台（macOS 的 <c>-psn_*</c> 等）与
    /// 某些启动器会注入自己的参数，为此拒绝启动是没必要的。
    /// 已知问题只记日志。
    /// </remarks>
    internal static StartupOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return Default;
        }

        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            // 支持 --play=<路径> 与 --play <路径> 两种写法
            if (argument.StartsWith(PlaySwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                var inline = argument[(PlaySwitch.Length + 1)..];

                if (string.IsNullOrWhiteSpace(inline))
                {
                    AppLogger.Warning($"{PlaySwitch} 缺少路径参数，已忽略");
                    return Default;
                }

                return new StartupOptions(inline);
            }

            if (string.Equals(argument, PlaySwitch, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    AppLogger.Warning($"{PlaySwitch} 缺少路径参数，已忽略");
                    return Default;
                }

                return new StartupOptions(args[i + 1]);
            }
        }

        return Default;
    }
}
