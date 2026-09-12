using System;

namespace UstPlayer.Projects;

/// <summary>
/// 工程文件格式错误（结构不对、字段缺失、引用的资源不存在等）。
/// </summary>
/// <remarks>
/// 与「路径不安全」区分开：两者对用户的可操作提示不同。UI 层据此给出
/// 「文件损坏」与「文件包含不安全内容」两类提示。
/// </remarks>
internal sealed class ProjectFormatException : Exception
{
    /// <summary>初始化异常。</summary>
    /// <param name="message">可读消息。</param>
    public ProjectFormatException(string message)
        : base(message)
    {
    }

    /// <summary>初始化异常（带内部异常）。</summary>
    /// <param name="message">可读消息。</param>
    /// <param name="innerException">内部异常。</param>
    public ProjectFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
