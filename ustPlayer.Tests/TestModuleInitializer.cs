using System.Runtime.CompilerServices;

using UstPlayer.Diagnostics;
using UstPlayer.Video;

namespace UstPlayer.Tests;

/// <summary>
/// 测试程序集的模块初始化：在**任何测试运行之前**把内置 ffmpeg 目录挂上 <c>PATH</c>。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不能只靠 <see cref="BundledFfmpegPathScope.Enter"/> 的成对进出：
/// 渲染器是在渲染循环里**按需**启动 ffmpeg 子进程的，它读 <c>PATH</c> 的时刻
/// 可能落在作用域退出之后。两个导出用例并行时会出现
/// 「A 退出时把 PATH 还原成原值 → 顺手摘掉了 B 还需要的内置目录」这种交错，
/// 表现为**单独跑必过、全量跑偶发**，且每次失败的用例可能不同。
/// （已实测复现：一次是 <c>对话框ViewModel能驱动真实导出</c>，
/// 另一次是 <c>能导出无声_MP4_与_uprd</c>。）
/// </para>
/// <para>
/// 这里的做法是让内置目录**从进程启动就常驻** <c>PATH</c>：初始化只跑一次、只加不撤，
/// 于是后续所有 <c>Enter</c> 都会命中「已在 PATH 上」而退化成无操作，
/// 交错窗口彻底消失——比去修补作用域的进入/退出顺序可靠得多。
/// </para>
/// <para>
/// 只在测试程序集里这么做：生产路径保持「只在导出期间临时加入」的语义，
/// 不长期改写用户的 <c>PATH</c>。
/// </para>
/// </remarks>
internal static class TestModuleInitializer
{
    /// <summary>模块加载时执行一次。</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        var programRoot = ProgramPaths.ProgramRoot;
        var added = BundledFfmpegPathScope.EnsureOnPath(programRoot);

        AppLogger.Info(
            added
                ? $"测试进程已把内置 ffmpeg 目录加入 PATH（{programRoot}）"
                : $"测试进程未改写 PATH（内置 ffmpeg 目录缺失或已在 PATH 上：{programRoot}）");
    }
}
