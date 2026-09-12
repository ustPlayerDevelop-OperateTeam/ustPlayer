using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Microsoft.Win32.SafeHandles;

namespace UstPlayer.Interop;

/// <summary>
/// 渲染上下文句柄的所有权封装（<c>up_create_context</c> / <c>up_destroy_context</c>）。
/// </summary>
/// <remarks>
/// C ABI 约定「同一上下文的所有调用必须串行，不同上下文可并发」，
/// 因此所有实例方法都在同一把锁内调用原生函数。
/// </remarks>
internal sealed class UplRenderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>初始化句柄。</summary>
    /// <param name="handle">原生上下文句柄（非 0）。</param>
    private UplRenderHandle(ulong handle)
        : base(ownsHandle: true)
    {
        SetHandle((IntPtr)handle);
    }

    /// <summary>创建渲染上下文。</summary>
    /// <returns>句柄包装。</returns>
    /// <exception cref="RendererException">原生创建失败（返回 0）。</exception>
    internal static UplRenderHandle Create()
    {
        var raw = NativeMethods.UpCreateContext();
        if (raw == 0)
        {
            throw new RendererException(NativeMethods.UpErrInternal, "创建渲染上下文失败");
        }

        return new UplRenderHandle(raw);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        var raw = (ulong)handle;
        if (raw != 0)
        {
            NativeMethods.UpDestroyContext(raw);
        }

        SetHandle(IntPtr.Zero);
        return true;
    }
}

/// <summary>
/// 渲染上下文的可用操作：配置、UST / LRC 注入、逐帧渲染与视频导出。
/// </summary>
/// <remarks>
/// 本类只做「把托管数据转成 C ABI 期望的形式并检查错误码」，
/// 业务侧的配置组装与时序逻辑分别属于 `UstPlayer.Video` 与 `UstPlayer.Timing`。
/// </remarks>
internal sealed class UplRenderContext : IDisposable
{
    /// <summary>串行化同一上下文的所有原生调用（C ABI 要求）。</summary>
    private readonly Lock _syncRoot = new();

    private readonly UplRenderHandle _handle;
    private bool _disposed;

    /// <summary>
    /// 进度回调的托管委托引用。
    /// </summary>
    /// <remarks>
    /// 必须保持存活：原生侧只保存函数指针，若托管委托被回收，
    /// 回调触发时会跳到已释放的跳板（对应 1.1.x `renderer_ffi.py` 中
    /// 「保持回调存活，避免被 GC」的同一考量）。
    /// </remarks>
    private NativeMethods.ProgressCallback? _progressCallback;

    /// <summary>创建一个渲染上下文。</summary>
    /// <exception cref="RendererException">原生创建失败。</exception>
    private UplRenderContext(UplRenderHandle handle)
    {
        _handle = handle;
    }

    /// <summary>创建一个渲染上下文。</summary>
    /// <returns>上下文实例。</returns>
    /// <exception cref="RendererException">原生创建失败。</exception>
    internal static UplRenderContext Create() => new(UplRenderHandle.Create());

    /// <summary>设置渲染配置（RenderConfig JSON）。</summary>
    /// <param name="jsonConfig">配置 JSON 文本。</param>
    /// <exception cref="RendererException">配置被拒绝。</exception>
    internal void SetConfig(string jsonConfig)
    {
        var payload = Encode(jsonConfig);
        lock (_syncRoot)
        {
            ThrowIfError(NativeMethods.UpSetConfig(RawHandle, payload), "up_set_config");
        }
    }

    /// <summary>直接传入 UST JSON，替换配置中的 UST 并重建时序模型。</summary>
    /// <param name="ustJson">UST JSON 文本。</param>
    /// <exception cref="RendererException">解析失败。</exception>
    internal void SetUstText(string ustJson)
    {
        var payload = Encode(ustJson);
        lock (_syncRoot)
        {
            ThrowIfError(NativeMethods.UpSetUstText(RawHandle, payload), "up_set_ust_text");
        }
    }

    /// <summary>直接传入 LRC 文本（渲染器内部做多编码探测）。</summary>
    /// <param name="lrcText">LRC 文本。</param>
    /// <exception cref="RendererException">解析失败。</exception>
    internal void SetLrcText(string lrcText)
    {
        var payload = Encode(lrcText);
        lock (_syncRoot)
        {
            ThrowIfError(NativeMethods.UpSetLrcText(RawHandle, payload), "up_set_lrc_text");
        }
    }

    /// <summary>按当前配置初始化编码器（仅导出路径需要）。</summary>
    /// <exception cref="RendererException">编码器初始化失败。</exception>
    internal void BeginExport()
    {
        lock (_syncRoot)
        {
            ThrowIfError(NativeMethods.UpBeginExport(RawHandle), "up_begin_export");
        }
    }

    /// <summary>渲染并送入一帧（仅导出路径需要；帧由宿主驱动）。</summary>
    /// <param name="elapsedSeconds">该帧的播放时间（秒）。</param>
    /// <exception cref="RendererException">渲染失败。</exception>
    internal void RenderFrame(double elapsedSeconds)
    {
        lock (_syncRoot)
        {
            ThrowIfError(NativeMethods.UpRenderFrame(RawHandle, elapsedSeconds), "up_render_frame");
        }
    }

    /// <summary>结束导出：flush 编码器、写尾部、关闭文件。</summary>
    /// <exception cref="RendererException">收尾失败。</exception>
    internal void EndExport()
    {
        lock (_syncRoot)
        {
            ThrowIfError(NativeMethods.UpEndExport(RawHandle), "up_end_export");
        }
    }

    /// <summary>
    /// 设置编码进度回调（千分比 0..1000）。
    /// </summary>
    /// <param name="onProgress">进度回调；传 <see langword="null"/> 清空。</param>
    /// <remarks>
    /// 回调由渲染器在编码过程中从**渲染线程**触发，实现方需自行保证线程安全。
    /// 本方法会持有委托引用以防被 GC 回收。
    /// </remarks>
    internal void SetProgressCallback(Action<int>? onProgress)
    {
        NativeMethods.ProgressCallback? callback = null;
        if (onProgress is not null)
        {
            callback = new NativeMethods.ProgressCallback(progress => onProgress(progress));
        }

        lock (_syncRoot)
        {
            _progressCallback = callback;
            NativeMethods.UpSetProgressCallback(RawHandle, callback!);
        }
    }

    /// <summary>
    /// 渲染单帧到调用方提供的缓冲（RGBA8888，premultiplied）。播放器实时预览走这条路（见 ADR 0001）。
    /// </summary>
    /// <param name="elapsedSeconds">该帧的播放时间（秒）。</param>
    /// <param name="buffer">目标缓冲，长度须 ≥ <paramref name="width"/> × <paramref name="height"/> × 4。</param>
    /// <param name="width">渲染宽度（像素）。</param>
    /// <param name="height">渲染高度（像素）。</param>
    /// <returns>实际写入的像素尺寸。</returns>
    /// <exception cref="ArgumentException">缓冲过小。</exception>
    /// <exception cref="RendererException">渲染失败。</exception>
    internal unsafe (int Width, int Height) RenderToBuffer(
        double elapsedSeconds,
        Span<byte> buffer,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var required = (long)width * height * 4;
        if (required > int.MaxValue)
        {
            throw new ArgumentException(
                $"渲染尺寸过大：{width}x{height} 需要 {required} 字节，超过 32 位缓冲上限",
                nameof(width));
        }

        if (buffer.Length < required)
        {
            throw new ArgumentException(
                $"缓冲过小：需要 {required} 字节，实际 {buffer.Length} 字节",
                nameof(buffer));
        }

        int outWidth;
        int outHeight;
        int code;

        fixed (byte* pointer = buffer)
        {
            lock (_syncRoot)
            {
                code = NativeMethods.UpRenderToBuffer(
                    RawHandle, elapsedSeconds, pointer, buffer.Length, &outWidth, &outHeight);
            }
        }

        ThrowIfError(code, "up_render_to_buffer");
        return (outWidth, outHeight);
    }

    /// <summary>读取最近一次错误消息（无错误返回空串）。</summary>
    /// <returns>UTF-8 错误消息。</returns>
    internal string LastError()
    {
        lock (_syncRoot)
        {
            return ReadLastError(RawHandle);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }

    private ulong RawHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (ulong)_handle.DangerousGetHandle();
        }
    }

    private static byte[] Encode(string text)
    {
        // C ABI 要求 UTF-8；附带结尾 0 以兼容按 C 字符串解析的实现
        var bytes = Encoding.UTF8.GetBytes(text);
        var payload = new byte[bytes.Length + 1];
        bytes.CopyTo(payload, 0);
        return payload;
    }

    private static string ReadLastError(ulong ctx)
    {
        // 与 1.1.x renderer_ffi.py 的 _err_message 对齐：读取错误消息本身绝不能再抛异常，
        // 否则「取错误信息」会把真正的失败原因盖掉。缓冲区按 C ABI 保证为合法 UTF-8，
        // 指针为空 ⇒ 无错误。
        try
        {
            var pointer = NativeMethods.UpLastError(ctx);
            return pointer == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private void ThrowIfError(int code, string stage)
    {
        if (code == NativeMethods.UpOk)
        {
            return;
        }

        var detail = ReadLastError(RawHandle);
        var description = RendererException.DescribeCode(code);
        var message = string.IsNullOrEmpty(detail)
            ? $"{stage} 失败：{description}"
            : $"{stage} 失败：{description}（{detail}）";

        throw new RendererException(code, message);
    }
}
