using Xunit;

namespace UstPlayer.Tests;

/// <summary>
/// 依赖**原生渲染器**的测试集合：放进这个集合的类之间**串行执行**。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要它：xUnit 默认让不同测试类并行跑，而这些类会同时使用原生渲染器；
/// 更关键的是导出路径会把**进程级 <c>PATH</c>** 临时加上内置 ffmpeg 目录
/// （见 <c>Video/BundledFfmpegPathScope.cs</c>，这是渲染器的硬要求——它只从 PATH 找 ffmpeg）。
/// 进程级环境变量是全局状态，并行时一个测试的进入/还原可能与另一个交错，
/// 表现为**偶发**失败：单独跑必过，整套跑偶尔报
/// 「ffmpeg init failed: ffmpeg executable not found in PATH」。
/// </para>
/// <para>
/// 实测过一次这样的偶发失败（<c>对话框ViewModel能驱动真实导出</c> 期望 Success 得到 Failed，
/// 单独跑则通过）。与其把它当噪声重跑，不如把「共用原生件 / 改全局环境」的测试显式串行化——
/// 其余纯托管测试仍照常并行，整体仍是秒级。
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public class NativeRendererCollection
{
    /// <summary>集合名。</summary>
    public const string Name = "native-renderer";
}
