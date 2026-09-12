using System;
using System.Collections.Generic;
using System.IO;

namespace UstPlayer.Diagnostics;

/// <summary>
/// 程序路径解析 — 从 1.1.x <c>core/contracts.py</c> 的 <c>resolve_program_root</c> /
/// <c>ensure_writable_dir</c> 与 <c>core/log.py</c> / <c>core/settings_store.py</c> 的
/// 路径回退逻辑移植。
/// </summary>
/// <remarks>
/// <para>
/// <b>策略（沿用 1.1.x）</b>：用户数据（设置、工程缓存、日志）优先放在程序目录，
/// 让绿色版把配置留在身边；程序目录不可写时回退到平台标准的用户数据目录。
/// </para>
/// <para>
/// <b>可写性必须用真实写探针判断</b>：Windows 的 <c>Directory.GetAccessControl</c> 类检查
/// 只看只读属性、不看 ACL，对 <c>Program Files</c> 这类受限目录会误报「可写」，
/// 导致设置与日志静默写入失败（1.1.x 踩过）。这里是实测：建目录 + 写一个探针文件。
/// </para>
/// </remarks>
internal static class ProgramPaths
{
    /// <summary>应用名（用于用户数据目录与日志文件名）。</summary>
    private const string AppDirectoryName = "ustPlayer";

    private static readonly Lock SyncRoot = new();
    private static string? _programRoot;
    private static string? _userDataDirectory;

    /// <summary>
    /// 程序根目录：打包后为可执行文件所在目录，开发期为当前工作目录。
    /// </summary>
    /// <remarks>
    /// 单文件发布时 <see cref="System.Reflection.Assembly.Location"/> 为空，
    /// 因此以进程主模块路径为准（与 1.1.x 用 <c>sys.executable</c> 的判断等价）。
    /// </remarks>
    internal static string ProgramRoot
    {
        get
        {
            lock (SyncRoot)
            {
                return _programRoot ??= ResolveProgramRoot();
            }
        }
    }

    /// <summary>
    /// 用户数据目录（设置、工程缓存、日志的回退位置与输出位置）。
    /// </summary>
    /// <remarks>
    /// 平台标准位置：Windows <c>%APPDATA%</c>、macOS <c>~/Library/Application Support</c>、
    /// Linux <c>~/.config</c>。<see cref="Environment.SpecialFolder.ApplicationData"/> 会自动分平台。
    /// </remarks>
    internal static string UserDataDirectory
    {
        get
        {
            lock (SyncRoot)
            {
                if (_userDataDirectory is not null)
                {
                    return _userDataDirectory;
                }

                var baseDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify);

                if (string.IsNullOrEmpty(baseDirectory))
                {
                    baseDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config");
                }

                _userDataDirectory = Path.Combine(baseDirectory, AppDirectoryName);
                return _userDataDirectory;
            }
        }
    }

    /// <summary>
    /// 日志目录：程序目录优先，不可写时回退用户数据目录。
    /// </summary>
    /// <returns>可写目录路径。</returns>
    internal static string LogDirectory() => ResolveWritableDirectory(ProgramRoot, UserDataDirectory);

    /// <summary>
    /// 设置文件目录：程序目录优先，不可写时回退用户数据目录。
    /// </summary>
    /// <returns>可写目录路径。</returns>
    internal static string SettingsDirectory() => ResolveWritableDirectory(ProgramRoot, UserDataDirectory);

    /// <summary>
    /// 判断目录是否**实际可写**：建目录并真实写入一个探针文件验证。
    /// </summary>
    /// <param name="directory">目录路径。</param>
    /// <returns>可写返回 <see langword="true"/>。</returns>
    internal static bool IsWritable(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception)
        {
            return false;
        }

        var probe = Path.Combine(directory, $".ustplayer_probe_{Environment.ProcessId}.tmp");

        try
        {
            File.WriteAllText(probe, string.Empty);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            TryDelete(probe);
        }
    }

    /// <summary>把路径分隔符统一为当前平台风格（用于日志与其他展示）。</summary>
    /// <param name="path">路径。</param>
    /// <returns>规范化后的路径。</returns>
    internal static string Normalize(string path) => Path.GetFullPath(path);

    private static string ResolveWritableDirectory(string preferred, string fallback)
    {
        if (IsWritable(preferred))
        {
            return preferred;
        }

        try
        {
            Directory.CreateDirectory(fallback);
        }
        catch (Exception)
        {
            // 连回退目录也建不出来时仍返回它：调用方写入失败会自行记录，好过在此抛异常
        }

        return fallback;
    }

    private static string ResolveProgramRoot()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 探针文件清理失败无关紧要
        }
    }
}

/// <summary>
/// 应用日志 — 从 1.1.x <c>core/log.py</c>（loguru）移植的轻量替代。
/// </summary>
/// <remarks>
/// <para>
/// 写入 <c>ustPlayer.log</c>（程序目录优先、不可写时回退用户数据目录），
/// 同时输出到控制台（打包后的 GUI 无控制台，此时静默跳过）。
/// </para>
/// <para>
/// 刻意保持极简：只提供级别方法、线程安全、追加写入与按大小轮转。
/// 1.1.x 用 loguru 的 <c>rotation="1 MB"</c> / <c>retention="7 days"</c>；
/// 2.0 只保留按大小轮转（保留 1 份），避免为日志引入第三方依赖。
/// </para>
/// <para>
/// 语言约定：<b>日志不翻译</b>（与 1.1.x 一致）。
/// </para>
/// </remarks>
internal static class AppLogger
{
    private const string LogFileName = "ustPlayer.log";
    private const long MaxBytes = 1024 * 1024;

    private static readonly Lock SyncRoot = new();
    private static readonly Lazy<string> LogFilePathHolder = new(ResolveLogFilePath);
    private static readonly bool HasConsole = !Console.IsOutputRedirected || IsAttachedToConsole();

    /// <summary>当前日志文件路径（供 UI「打开日志」使用）。</summary>
    internal static string LogFilePath => LogFilePathHolder.Value;

    /// <summary>写调试级日志。</summary>
    /// <param name="message">消息。</param>
    internal static void Debug(string message) => Write("DEBUG", message);

    /// <summary>写信息级日志。</summary>
    /// <param name="message">消息。</param>
    internal static void Info(string message) => Write("INFO", message);

    /// <summary>写警告级日志。</summary>
    /// <param name="message">消息。</param>
    internal static void Warning(string message) => Write("WARN", message);

    /// <summary>写错误级日志（附异常信息）。</summary>
    /// <param name="message">消息。</param>
    /// <param name="exception">异常；可为空。</param>
    internal static void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

        lock (SyncRoot)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // 日志写入失败绝不能再抛：否则会把「记录问题」变成「制造问题」。
                // 控制台输出仍然尝试一次（若可用）。
            }
        }

        if (HasConsole)
        {
            Console.WriteLine(line);
        }
    }

    private static void RotateIfNeeded()
    {
        var path = LogFilePath;
        var info = new FileInfo(path);

        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        var previous = path + ".1";
        if (File.Exists(previous))
        {
            File.Delete(previous);
        }

        File.Move(path, previous);
    }

    private static string ResolveLogFilePath()
    {
        var directory = ProgramPaths.LogDirectory();

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception)
        {
            // 目录建不出来时退到临时目录，保证日志功能不至于完全失效
            directory = Path.GetTempPath();
        }

        return Path.Combine(directory, LogFileName);
    }

    private static bool IsAttachedToConsole()
    {
        try
        {
            return Console.Out is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
