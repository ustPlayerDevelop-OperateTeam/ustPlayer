namespace UstPlayer.Views;

/// <summary>提示条严重级别（对应 FluentAvalonia 的 <c>InfoBarSeverity</c>）。</summary>
internal enum NotificationSeverity
{
    /// <summary>普通信息。</summary>
    Informational,

    /// <summary>成功。</summary>
    Success,

    /// <summary>警告。</summary>
    Warning,

    /// <summary>错误。</summary>
    Error,
}

/// <summary>
/// 提示条宿主 — 页面经此显示用户可见的成功 / 失败信息。
/// </summary>
/// <remarks>
/// <para>
/// 1.1.x 用 qfluentwidgets 的静态 <c>InfoBar.error(...)</c> 到处弹提示；
/// FluentAvalonia 的 <c>InfoBar</c> 是**控件**，必须挂在可视树上，
/// 没有等价的静态入口。因此由主窗口持有一个提示条，并把「显示提示」收成这个窄接口
/// （与 <c>IAudioBackend</c> 同样的思路：页面只依赖它能做的事，而不是整个窗口）。
/// </para>
/// <para>
/// 这样页面也能在没有窗口的环境里被测试——传入一个记录调用的假宿主即可。
/// </para>
/// </remarks>
internal interface INotificationHost
{
    /// <summary>显示一条提示。</summary>
    /// <param name="severity">严重级别。</param>
    /// <param name="title">标题（用户可见，须已翻译）。</param>
    /// <param name="message">正文（用户可见，须已翻译）。</param>
    void Notify(NotificationSeverity severity, string title, string message);
}
