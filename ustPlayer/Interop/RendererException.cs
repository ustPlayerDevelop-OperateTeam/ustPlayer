using System;

namespace UstPlayer.Interop;

/// <summary>
/// 渲染器调用失败（携带 C ABI 错误码与可读消息）。
/// </summary>
public sealed class RendererException : Exception
{
    /// <summary>初始化异常。</summary>
    /// <param name="code">C ABI 错误码（<c>UP_ERR_*</c>）。</param>
    /// <param name="message">可读错误消息（来自 <c>up_last_error</c>）。</param>
    public RendererException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>初始化异常（带内部异常）。</summary>
    /// <param name="code">C ABI 错误码。</param>
    /// <param name="message">可读错误消息。</param>
    /// <param name="innerException">内部异常。</param>
    public RendererException(int code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>C ABI 错误码（<c>UP_ERR_*</c>）。</summary>
    public int Code { get; }

    /// <summary>把错误码翻译成中文阶段名，便于日志与用户提示。</summary>
    /// <param name="code">错误码。</param>
    /// <returns>可读的阶段描述。</returns>
    public static string DescribeCode(int code) => code switch
    {
        NativeMethods.UpErrInvalidArg => "参数非法",
        NativeMethods.UpErrParse => "解析失败",
        NativeMethods.UpErrIo => "文件读写失败",
        NativeMethods.UpErrFont => "字体加载失败",
        NativeMethods.UpErrRender => "渲染失败",
        NativeMethods.UpErrEncode => "编码失败",
        NativeMethods.UpErrInternal => "渲染器内部错误",
        _ => $"未知错误（{code}）",
    };
}
