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
/// <param name="PageKey">
/// 主窗口启动时直接打开的页面键（<c>basic</c> / <c>file</c> / <c>player_style</c> / <c>lyric</c> /
/// <c>settings</c>）；未指定时为 <see langword="null"/>（即基础页）。
/// </param>
internal sealed record StartupOptions(string? PlayUstPath, string? PageKey = null)
{
    /// <summary>播放模式的开关名。</summary>
    internal const string PlaySwitch = "--play";

    /// <summary>指定启动页面的开关名。</summary>
    internal const string PageSwitch = "--page";

    /// <summary>默认启动参数（打开主窗口的基础页）。</summary>
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
    /// <para>
    /// 两个开关可同时出现（例如 <c>--page settings</c> 便于脚本把窗口开到设置页截图核对），
    /// 缺失取值只影响其自身、不再像早期版本那样丢弃整个解析结果。
    /// 代价是取值不能以 <c>--</c> 开头——路径若真是这样，用 <c>--play=&lt;路径&gt;</c> 写法即可。
    /// </para>
    /// </remarks>
    internal static StartupOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return Default;
        }

        string? playPath = null;
        string? pageKey = null;

        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (TryReadSwitch(argument, args, ref i, PlaySwitch, out var playValue))
            {
                if (playValue is null)
                {
                    AppLogger.Warning($"{PlaySwitch} 缺少路径参数，已忽略");
                }
                else
                {
                    playPath = playValue;
                }

                continue;
            }

            if (TryReadSwitch(argument, args, ref i, PageSwitch, out var pageValue))
            {
                if (pageValue is null)
                {
                    AppLogger.Warning($"{PageSwitch} 缺少页面参数，已忽略");
                }
                else
                {
                    pageKey = pageValue;
                }
            }
        }

        return new StartupOptions(playPath, pageKey);
    }

    /// <summary>
    /// 尝试把当前位置的参数读成 <paramref name="switchName"/> 开关的取值。
    /// </summary>
    /// <param name="argument">当前参数。</param>
    /// <param name="args">完整参数列表（读「开关 取值」两个参数时用）。</param>
    /// <param name="index">当前下标；命中「开关 取值」写法时会被推进到取值位置。</param>
    /// <param name="switchName">开关名（含前导 <c>--</c>）。</param>
    /// <param name="value">取值；开关命中但缺取值时为 <see langword="null"/>。</param>
    /// <returns>是否命中了该开关。</returns>
    private static bool TryReadSwitch(
        string argument,
        IReadOnlyList<string> args,
        ref int index,
        string switchName,
        out string? value)
    {
        value = null;

        // 支持 --开关=取值 与 --开关 取值 两种写法
        if (argument.StartsWith(switchName + "=", StringComparison.OrdinalIgnoreCase))
        {
            var inline = argument[(switchName.Length + 1)..];
            value = string.IsNullOrWhiteSpace(inline) ? null : inline;
            return true;
        }

        if (!string.Equals(argument, switchName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            return true;
        }

        // 下一个参数本身是开关时，不把它当取值吞掉：用户漏写取值（或打错开关名）时
        // 应当只让这个开关失效，而不是静默吃掉后面的开关。
        if (args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return true;
        }

        value = args[index + 1];
        index++;
        return true;
    }
}
