using System;
using System.Text;
using System.Threading;

namespace UstPlayer.Diagnostics;

/// <summary>
/// 文本编码基础设施的启动注册。
/// </summary>
/// <remarks>
/// <para>
/// .NET 默认只带 Unicode 家族与 ISO-8859-1；<c>Shift-JIS</c> / <c>GBK</c> / <c>GB2312</c>
/// 这些代码页需要显式注册 <see cref="CodePagesEncodingProvider"/> 才能使用，
/// 否则 <see cref="Encoding.GetEncoding(string)"/> 会抛
/// 「'Shift-JIS' is not a supported encoding name」。
/// </para>
/// <para>
/// 而 Shift-JIS 是**默认的 UST 编码**（日文工程最常见），所以这不是可选项：
/// 不注册就等于「默认打不开日文 UST」。
/// </para>
/// <para>
/// 注册必须早于任何一次编码解析。这里用模块初始化器保证，
/// 避免依赖「某人记得在启动流程里调一下」——那正是容易漏的地方。
/// </para>
/// </remarks>
internal static class EncodingBootstrap
{
    private static int _registered;

    /// <summary>
    /// 注册代码页编码提供程序（幂等）。
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch (Exception)
        {
            // 注册失败不致命：UTF-8 家族仍可用，只是部分代码页解析不了
            // （此时 EncodingResolver 会回退，而不是抛异常）
        }
    }
}
