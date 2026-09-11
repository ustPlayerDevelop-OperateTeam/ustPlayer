using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UstPlayer.Interop;

/// <summary>
/// uPlRender 渲染器原生库的 P/Invoke 绑定（C ABI，见仓库根 <c>API_Docs.md</c>
/// 与 uPlRender 的 <c>bindings/ustplayer_renderer.h</c> / <c>bindings/ustplayer_renderer.py</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 约定（来自 C ABI）：
/// </para>
/// <list type="bullet">
///   <item>字符串一律 UTF-8，由库分配/释放；宿主不得释放库返回的指针；</item>
///   <item>上下文用 <c>u64</c> 句柄标识，同一上下文的所有调用必须串行，不同上下文可并发；</item>
///   <item>错误通过负返回值 + <see cref="UpLastError"/> 上报，绝不 panic 跨越 FFI。</item>
/// </list>
/// <para>
/// <b>EntryPoint 必须显式书写</b>：<c>[LibraryImport]</c> 不像 <c>[DllImport]</c> 那样
/// 尝试 <c>ExactSpelling</c> 之外的名称映射，方法名会被原样当作导出名使用。
/// DLL 的导出是 snake_case（<c>up_create_context</c>），故每个声明都写上 <c>EntryPoint</c>。
/// </para>
/// </remarks>
internal static partial class NativeMethods
{
    /// <summary>库的逻辑名。实际文件名按平台不同，由 <see cref="NativeLibraryResolver"/> 解析。</summary>
    internal const string Library = "ustplayer_renderer";

    // ---------- 错误码（与头文件 enum UstPlayerErr 一致） ----------

    /// <summary>成功。</summary>
    internal const int UpOk = 0;

    /// <summary>空指针 / 非法枚举 / 长度越界。</summary>
    internal const int UpErrInvalidArg = -1;

    /// <summary>UST / LRC / JSON 解析失败。</summary>
    internal const int UpErrParse = -2;

    /// <summary>文件读写失败。</summary>
    internal const int UpErrIo = -3;

    /// <summary>字体加载失败。</summary>
    internal const int UpErrFont = -4;

    /// <summary>渲染阶段失败。</summary>
    internal const int UpErrRender = -5;

    /// <summary>编码器初始化 / 写入失败。</summary>
    internal const int UpErrEncode = -6;

    /// <summary>未分类内部错误（已捕获的 panic）。</summary>
    internal const int UpErrInternal = -99;

    // ---------- 句柄生命周期 ----------

    /// <summary>创建渲染上下文；失败返回 0。一次创建可多次复用（内部缓存字体 / LRC / 时序模型）。</summary>
    /// <returns>上下文句柄。</returns>
    [LibraryImport(Library, EntryPoint = "up_create_context")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong UpCreateContext();

    /// <summary>销毁渲染上下文，释放内部全部资源（幂等）。</summary>
    /// <param name="ctx">上下文句柄。</param>
    [LibraryImport(Library, EntryPoint = "up_destroy_context")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void UpDestroyContext(ulong ctx);

    // ---------- 配置 ----------

    /// <summary>设置渲染配置（RenderConfig JSON，UTF-8）。</summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <param name="jsonConfig">配置 JSON 的 UTF-8 字节（调用方负责补结尾 NUL）。</param>
    /// <returns>错误码。</returns>
    [LibraryImport(Library, EntryPoint = "up_set_config")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int UpSetConfig(ulong ctx, byte[] jsonConfig);

    /// <summary>直接传入 UST JSON，替换 <c>config.ust</c> 并重建时序模型。</summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <param name="ustJson">UST JSON 的 UTF-8 字节。</param>
    /// <returns>错误码。</returns>
    [LibraryImport(Library, EntryPoint = "up_set_ust_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int UpSetUstText(ulong ctx, byte[] ustJson);

    /// <summary>直接传入 LRC 文本（内部多编码探测），替换 <c>lrc_path</c> 的读取结果。</summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <param name="lrcText">LRC 文本的 UTF-8 字节。</param>
    /// <returns>错误码。</returns>
    [LibraryImport(Library, EntryPoint = "up_set_lrc_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int UpSetLrcText(ulong ctx, byte[] lrcText);

    // ---------- 视频导出 ----------

    /// <summary>按当前配置（宽 / 高 / fps / 输出路径）初始化编码器。</summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <returns>错误码。</returns>
    [LibraryImport(Library, EntryPoint = "up_begin_export")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int UpBeginExport(ulong ctx);

    /// <summary>渲染并送入一帧（帧完全由宿主驱动，库不维护内部时钟）。</summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <param name="elapsedSec">该帧的播放时间（秒）。</param>
    /// <returns>错误码。</returns>
    [LibraryImport(Library, EntryPoint = "up_render_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int UpRenderFrame(ulong ctx, double elapsedSec);

    /// <summary>结束导出：flush 编码器、写尾部、关闭文件。</summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <returns>错误码。</returns>
    [LibraryImport(Library, EntryPoint = "up_end_export")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int UpEndExport(ulong ctx);

    /// <summary>设置编码进度回调（千分比 0..1000）。回调须由调用方保持存活。</summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <param name="callback">进度回调。</param>
    [LibraryImport(Library, EntryPoint = "up_set_progress_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void UpSetProgressCallback(ulong ctx, ProgressCallback callback);

    /// <summary>编码进度回调的原生签名：<c>extern "C" fn(i32)</c>。</summary>
    /// <param name="progress">千分比（0..1000）。</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ProgressCallback(int progress);

    // ---------- 单帧离屏渲染（播放器实时预览用，见 ADR 0001） ----------

    /// <summary>
    /// 渲染单帧到宿主提供的 RGBA8888（premultiplied）缓冲。
    /// </summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <param name="elapsedSec">该帧的播放时间（秒）。</param>
    /// <param name="buffer">宿主分配的输出缓冲，长度须 ≥ width*height*4。</param>
    /// <param name="bufferLength">缓冲字节数。</param>
    /// <param name="outWidth">回写实际宽度。</param>
    /// <param name="outHeight">回写实际高度。</param>
    /// <returns>错误码。</returns>
    [LibraryImport(Library, EntryPoint = "up_render_to_buffer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int UpRenderToBuffer(
        ulong ctx,
        double elapsedSec,
        byte* buffer,
        int bufferLength,
        int* outWidth,
        int* outHeight);

    // ---------- 错误消息 ----------

    /// <summary>
    /// 取最近一次错误消息（UTF-8，静态缓冲；无错误返回空指针）。
    /// </summary>
    /// <param name="ctx">上下文句柄。</param>
    /// <returns>指向库内静态字符串的指针；调用方不得释放。</returns>
    [LibraryImport(Library, EntryPoint = "up_last_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr UpLastError(ulong ctx);
}

/// <summary>
/// 把逻辑名 <c>ustplayer_renderer</c> 解析到实际的平台文件名。
/// </summary>
/// <remarks>
/// 各平台文件名不同（Windows <c>ustplayer_renderer.dll</c>、
/// macOS <c>libustplayer_renderer.dylib</c>、Linux <c>libustplayer_renderer.so</c>），
/// 因此用 <see cref="NativeLibrary.SetDllImportResolver"/> 统一处理，
/// 避免为每个平台写条件编译。
/// <para>
/// 解析器在模块初始化时注册，确保早于任何 <c>[LibraryImport]</c> 桩的首次调用。
/// </para>
/// </remarks>
internal static class NativeLibraryResolver
{
    private static int _registered;

    /// <summary>本平台的渲染器文件名。</summary>
    internal static string PlatformFileName { get; } = ResolvePlatformFileName();

    /// <summary>
    /// 注册渲染器原生库解析器。
    /// </summary>
    /// <remarks>
    /// 必须早于任何 <c>[LibraryImport]</c> 桩的首次调用，否则默认解析会去找
    /// <c>ustplayer_renderer.dll</c> 而错过各平台的真实文件名（macOS/Linux 带 <c>lib</c>
    /// 前缀与不同扩展名）。模块初始化器是保证这一时序的最简手段。
    /// </remarks>
    [ModuleInitializer]
    // CA2255：分析器不建议库使用模块初始化器；此处是刻意的（见上方理由）。
    [SuppressMessage(
        "Usage",
        "CA2255:The ModuleInitializer attribute should not be used in libraries",
        Justification = "需要在任何 P/Invoke 调用前注册 DLL 解析器，保证跨平台文件名被正确解析。")]
    internal static void Register()
    {
        EnsureRegistered();
    }

    /// <summary>
    /// 幂等地注册 DLL 解析器。供模块初始化器与显式调用（如测试）共用。
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        // 返回 IntPtr.Zero 表示「本解析器不处理」，交由默认逻辑继续尝试，
        // 因此这里可以安全地只在命中逻辑名时介入。
        NativeLibrary.SetDllImportResolver(
            typeof(NativeMethods).Assembly,
            (libraryName, assembly, searchPath) =>
            {
                if (!string.Equals(libraryName, NativeMethods.Library, StringComparison.Ordinal))
                {
                    return IntPtr.Zero;
                }

                foreach (var directory in UplRenderLoader.SearchDirectories())
                {
                    var candidate = Path.Combine(directory, PlatformFileName);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                    {
                        return handle;
                    }
                }

                return IntPtr.Zero;
            });
    }

    private static string ResolvePlatformFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "ustplayer_renderer.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "libustplayer_renderer.dylib";
        }

        return "libustplayer_renderer.so";
    }
}
