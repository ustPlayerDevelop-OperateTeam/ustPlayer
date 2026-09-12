using System;

using UstPlayer.Diagnostics;
using UstPlayer.Projects;
using UstPlayer.Settings;
using UstPlayer.Ust;
using UstPlayer.Video;

namespace UstPlayer;

/// <summary>
/// 组合根 / 门面 — 从 1.1.x <c>ustplayer/context.py</c> 的 <c>AppContext</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 全应用**唯一**组装具体实现的地方。UI 只通过本类拿到服务
/// （<c>ctx.Settings</c> / <c>ctx.Ust</c> / <c>ctx.ProjectIo</c> / <c>ctx.VideoExporter</c>），
/// 不得各自 <c>new</c> 具体实现——否则设置会被重复读取、写入互相覆盖。
/// </para>
/// <para>
/// <b>命名</b>：1.1.x 叫 <c>AppContext</c>，2.0 改名 <see cref="AppServices"/>——
/// .NET 已有 <see cref="System.AppContext"/>，同名类型会**静默遮蔽**该 BCL 类型：
/// <c>AppContext.BaseDirectory</c> 这类写法会突然编译不过，且报错指向本类而非根因。
/// </para>
/// <para>
/// 本类位于根命名空间 <c>UstPlayer</c>，是**刻意**的分层例外：组合根按定义就要跨越
/// 各层（它要 new 出 Projects / Settings / Ust / Video 的实现）。这也是它没有被列入
/// <c>LayeringTests</c> 的自由/受限命名空间清单的原因——其余命名空间仍然照旧受限。
/// </para>
/// <para>
/// 生命周期：<see cref="Dispose"/> 时把设置写回磁盘（1.1.x 即「退出时保存」）。
/// 因此它必须在应用退出路径上被释放。
/// </para>
/// </remarks>
internal sealed class AppServices : IDisposable
{
    private bool _disposed;

    /// <summary>创建组合根并装配全部服务。</summary>
    /// <param name="settingsPath">
    /// 设置文件路径；传 <see langword="null"/> 时按默认策略解析（程序目录旁，
    /// 不可写时回退用户数据目录）。测试注入临时路径。
    /// </param>
    internal AppServices(string? settingsPath = null)
    {
        Settings = new SettingsManager(settingsPath);
        Ust = new UstFileReader();
        ProjectIo = new UplrProjectIO(Settings);
        VideoExporter = new VideoExporter(Settings, Ust, ProjectIo);

        AppLogger.Info($"组合根就绪（设置文件：{Settings.SettingsPath}）");
    }

    /// <summary>设置门面（七个设置子域 + 播放参数组装）。</summary>
    internal SettingsManager Settings { get; }

    /// <summary>UST 解析器（只处理 <c>.ust</c> 文本，不支持 USTX）。</summary>
    internal UstFileReader Ust { get; }

    /// <summary>工程 IO（<c>.uplr</c> 导入导出、<c>.uprd</c> 导出）。</summary>
    internal UplrProjectIO ProjectIo { get; }

    /// <summary>视频导出器。</summary>
    internal VideoExporter VideoExporter { get; }

    /// <summary>
    /// 把设置写回磁盘。
    /// </summary>
    /// <remarks>
    /// 写入失败只记录日志（<see cref="SettingsManager.WriteSettings"/> 内已兜住异常），
    /// 不会在退出路径上抛出——退出时抛异常会让用户看到「关不掉的窗口」。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Settings.WriteSettings();
    }
}
