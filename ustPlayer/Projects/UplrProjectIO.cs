using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using UstPlayer.Diagnostics;
using UstPlayer.Settings;

namespace UstPlayer.Projects;

/// <summary>
/// <c>.uplr</c> / <c>.uprd</c> 工程文件的导入导出 — 从 1.1.x <c>core/uplr_io.py</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 新版格式是 **ZIP 容器**（<c>Info.json</c> + 资源），导入时解压到
/// <c>&lt;程序目录&gt;/cache/&lt;工程名&gt;-&lt;路径哈希8位&gt;/</c>。
/// 旧版纯文本格式仍可导入（按 ZIP 魔数 <c>PK\x03\x04</c> 自动识别）。
/// </para>
/// <para>
/// <b>导入是事务化的</b>，三层保护缺一不可：
/// </para>
/// <list type="number">
///   <item>解压到 <c>.staging-&lt;pid&gt;</c> 暂存目录，全部校验通过后才原子替换正式缓存
///   ——失败的包不会破坏上一次成功导入的资源；</item>
///   <item>设置先快照，任一环节失败即整体回滚，避免半成品配置被持久化；</item>
///   <item>失败时清理暂存目录，不留垃圾。</item>
/// </list>
/// <para>
/// <b>解压防护</b>：成员名走 <see cref="ProjectPathSafety"/>（防 zip slip），
/// 并按成员 / 总量上限分块流式写入（防 zip bomb）。
/// </para>
/// </remarks>
internal sealed class UplrProjectIO
{
    /// <summary><c>Info.json</c> 的大小上限：它应该极小，过大即视为异常。</summary>
    private const long MaxInfoSize = 1024 * 1024;

    /// <summary>单个成员解压上限。</summary>
    private const long MaxMemberSize = 512L * 1024 * 1024;

    /// <summary>整个工程解压总量上限。</summary>
    private const long MaxTotalSize = 1024L * 1024 * 1024;

    /// <summary>流式解压的分块大小。</summary>
    private const int ChunkSize = 1024 * 1024;

    /// <summary>缓存目录名（位于程序目录下）。</summary>
    private const string CacheDirectoryName = "cache";

    /// <summary>ZIP 本地文件头魔数。</summary>
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    private readonly SettingsManager _settings;
    private readonly string? _cacheBaseOverride;

    /// <summary>创建工程 IO。</summary>
    /// <param name="settings">设置管理器。</param>
    /// <param name="cacheBaseOverride">
    /// 缓存根目录覆盖；传 <see langword="null"/> 时按默认策略解析
    /// （程序目录下 <c>cache/</c>，不可写时回退用户数据目录）。测试与便携模式走这里。
    /// </param>
    internal UplrProjectIO(SettingsManager settings, string? cacheBaseOverride = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _cacheBaseOverride = cacheBaseOverride;
    }

    // ===================== 导出 =====================

    /// <summary>把当前配置与资源导出为 <c>.uplr</c>。</summary>
    /// <param name="outputFile">输出路径。</param>
    internal void ExportUplr(string outputFile) =>
        Export(outputFile, UplrInfoJson.BuildUplrInfo(_settings, CollectMembers()));

    /// <summary>把当前配置、资源与视频参数导出为 <c>.uprd</c>。</summary>
    /// <param name="outputFile">输出路径。</param>
    /// <param name="width">视频宽。</param>
    /// <param name="height">视频高。</param>
    /// <param name="fps">帧率。</param>
    internal void ExportUprd(string outputFile, int width, int height, int fps) =>
        Export(outputFile, UplrInfoJson.BuildUprdInfo(_settings, CollectMembers(), (width, height, fps)));

    private void Export(string outputFile, JsonObject info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFile);

        var members = CollectMembers();
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputFile));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 先写临时文件再整体替换：导出中途失败不会留下半截 zip 覆盖掉旧工程
        var tempFile = outputFile + ".tmp";

        try
        {
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // 与 1.1.x 一致：不压缩（工程内多为音频，压缩收益低而耗时明显）
                WriteEntry(archive, UplrInfoJson.InfoFileName, SerializeInfo(info));

                foreach (var (settingName, memberName) in members.Items)
                {
                    var localPath = ResolveLocalPath(settingName);
                    if (localPath.Length > 0 && File.Exists(localPath))
                    {
                        archive.CreateEntryFromFile(localPath, memberName, CompressionLevel.NoCompression);
                    }
                }
            }

            File.Move(tempFile, outputFile, overwrite: true);
            AppLogger.Info($"已导出工程：{outputFile}（资源 {members.Count} 个）");
        }
        finally
        {
            TryDeleteFile(tempFile);
        }
    }

    // ===================== 导入 =====================

    /// <summary>
    /// 导入工程文件（自动识别新版 ZIP 与旧版文本格式）。
    /// </summary>
    /// <param name="inputFile">工程文件路径。</param>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="ProjectFormatException">格式损坏、引用缺失或路径不安全。</exception>
    internal void ImportUplr(string inputFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFile);

        if (!File.Exists(inputFile))
        {
            throw new FileNotFoundException($"工程文件不存在：{inputFile}", inputFile);
        }

        if (IsZipContainer(inputFile))
        {
            ImportZip(inputFile);
            return;
        }

        ImportLegacyText(inputFile);
    }

    /// <summary>判断是否为 ZIP 容器（读魔数，不看扩展名）。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>是 ZIP 返回 <see langword="true"/>。</returns>
    private static bool IsZipContainer(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        Span<byte> head = stackalloc byte[4];

        return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length &&
               head.SequenceEqual(ZipMagic);
    }

    /// <summary>导入新版 ZIP 容器。</summary>
    /// <param name="inputFile">文件路径。</param>
    private void ImportZip(string inputFile)
    {
        var cacheDirectory = ResolveCacheDirectory(inputFile);
        var stagingDirectory = $"{cacheDirectory}.staging-{Environment.ProcessId}";

        TryDeleteDirectory(stagingDirectory);

        var snapshot = _settings.CreateSnapshot();

        try
        {
            JsonObject info;

            using (var archive = ZipFile.OpenRead(inputFile))
            {
                var infoEntry = archive.GetEntry(UplrInfoJson.InfoFileName)
                    ?? throw new ProjectFormatException("ZIP 工程文件缺少 Info.json");

                if (infoEntry.Length > MaxInfoSize)
                {
                    throw new ProjectFormatException("Info.json 异常过大，已中止导入");
                }

                info = ReadInfoJson(infoEntry)
                    ?? throw new ProjectFormatException("Info.json 顶层结构必须是对象");

                long total = 0;
                foreach (var entry in archive.Entries)
                {
                    if (string.Equals(entry.FullName, UplrInfoJson.InfoFileName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    total += ExtractEntrySafely(entry, stagingDirectory);

                    if (total > MaxTotalSize)
                    {
                        throw new ProjectFormatException("工程文件解压总量超限，已中止导入");
                    }
                }
            }

            // 全部校验与应用成功后才替换旧缓存：失败的包不允许破坏上一次成功导入的资源
            UplrInfoJson.ApplyInfoJson(_settings, info, stagingDirectory);

            TryDeleteDirectory(cacheDirectory);
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Move(stagingDirectory, cacheDirectory);
            }

            RepointResourcePaths(stagingDirectory, cacheDirectory);
            AppLogger.Info($"已导入工程：{inputFile}");
        }
        catch (Exception)
        {
            // 事务化：回滚设置并清理半成品缓存；旧缓存保持原样
            _settings.RestoreSnapshot(snapshot);
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    /// <summary>导入旧版纯文本格式（不删除资源，仅解析键值）。</summary>
    /// <param name="inputFile">文件路径。</param>
    private void ImportLegacyText(string inputFile)
    {
        var snapshot = _settings.CreateSnapshot();

        try
        {
            // 与 ZIP 导入语义一致：先把会被导入触碰的字段重置为默认值，
            // 旧文件里没出现的字段不允许残留上一个工程的状态
            UplrInfoJson.ApplyInfoJson(_settings, new JsonObject(), baseDirectory: string.Empty);

            var content = ReadTextWithEncodingFallback(inputFile);
            ApplyLegacyKeyValues(content);
            ResolveLegacyRelativePaths(inputFile);
        }
        catch (Exception)
        {
            _settings.RestoreSnapshot(snapshot);
            throw;
        }
    }

    /// <summary>解析旧格式的 <c>key=value</c> 行。</summary>
    /// <param name="content">文件内容。</param>
    private void ApplyLegacyKeyValues(string content)
    {
        foreach (var rawLine in EnumerateLines(content))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            switch (key)
            {
                // 字符串字段
                case "project_name":
                    _settings.Project.ProjectName = value;
                    break;
                case "song_name":
                    _settings.Project.SongName = value;
                    break;
                case "song_author":
                    _settings.Project.SongAuthor = value;
                    break;
                case "ust_author":
                    _settings.Project.UstAuthor = value;
                    break;
                case "ust_path":
                    _settings.File.UstPath = value;
                    break;
                case "music_path":
                    _settings.Project.MusicPath = value;
                    break;
                case "encoding":
                    _settings.File.Encoding = value;
                    break;

                case "bg_color":
                    _settings.Color.BackgroundColor = value;
                    break;
                case "note_color":
                    _settings.Color.NoteColor = value;
                    break;
                case "lyric_color":
                    _settings.Color.LyricColor = value;
                    break;
                case "lyric_text_color":
                    _settings.Color.LyricTextColor = value;
                    break;
                case "other_text_color":
                    _settings.Color.OtherTextColor = value;
                    break;
                case "pitch_curve_color":
                    _settings.Color.PitchCurveColor = value;
                    break;

                // 枚举经 setter 完成旧中文值迁移
                case "lyric_pos":
                    _settings.Player.LyricPosition = value;
                    break;
                case "silent_display":
                    _settings.Player.SilentDisplay = value;
                    break;
                case "end_display":
                    _settings.Player.EndDisplay = value;
                    break;
                case "pitch_placeholder":
                    _settings.Player.PitchPlaceholder = value;
                    break;

                case "silent_custom_text":
                    _settings.Player.SilentCustomText = value;
                    break;
                case "end_custom_text":
                    _settings.Player.EndCustomText = value;
                    break;
                case "pitch_custom_text":
                    _settings.Player.PitchCustomText = value;
                    break;
                case "lrc_path":
                    _settings.Player.LrcPath = value;
                    break;

                // 布尔字段（旧格式用字符串真值表）
                case "show_bpm":
                    _settings.Display.ShowBpm = IsTruthy(value);
                    break;
                case "show_play_time":
                    _settings.Display.ShowPlayTime = IsTruthy(value);
                    break;
                case "show_song_name":
                    _settings.Display.ShowSongName = IsTruthy(value);
                    break;
                case "show_song_author":
                    _settings.Display.ShowSongAuthor = IsTruthy(value);
                    break;
                case "show_ust_author":
                    _settings.Display.ShowUstAuthor = IsTruthy(value);
                    break;
                case "fullscreen":
                    _settings.Display.Fullscreen = IsTruthy(value);
                    break;
                case "show_lyric":
                    _settings.Display.ShowLyric = IsTruthy(value);
                    break;
                case "curve_show":
                    _settings.File.CurveShow = IsTruthy(value);
                    break;
            }
        }
    }

    /// <summary>
    /// 旧格式的资源路径常是相对路径：优先按工程文件所在目录解析。
    /// </summary>
    /// <param name="inputFile">工程文件路径。</param>
    private void ResolveLegacyRelativePaths(string inputFile)
    {
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(inputFile)) ?? string.Empty;

        RepointOne(baseDirectory, _settings.File.UstPath, path => _settings.File.UstPath = path);
        RepointOne(baseDirectory, _settings.Player.LrcPath, path => _settings.Player.LrcPath = path);
        RepointOne(baseDirectory, _settings.Project.MusicPath, path => _settings.Project.MusicPath = path);
    }

    private static void RepointOne(string baseDirectory, string path, Action<string> assign)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var candidate = path;

        if (!Path.IsPathRooted(path))
        {
            var joined = Path.GetFullPath(Path.Combine(baseDirectory, path));
            if (File.Exists(joined))
            {
                candidate = joined;
            }
        }

        if (!File.Exists(candidate))
        {
            AppLogger.Warning($"旧版 .uplr 资源路径在本机不存在：{path}");
        }

        assign(candidate);
    }

    // ===================== 工程缓存目录 =====================

    /// <summary>
    /// 缓存根目录：默认 <c>&lt;程序目录&gt;/cache</c>，程序目录不可写时回退用户数据目录。
    /// </summary>
    /// <returns>缓存根目录。</returns>
    /// <remarks>
    /// 可写性用真实写探针判断（见 <c>ProgramPaths.IsWritable</c>）——只看只读属性 / ACL
    /// 在 Windows 上会误报可写。同名普通文件占位时直接回退（不可能当目录用）。
    /// </remarks>
    internal string CacheBase()
    {
        if (!string.IsNullOrWhiteSpace(_cacheBaseOverride))
        {
            return _cacheBaseOverride;
        }

        var root = _settings.ProgramRoot;
        var preferred = Path.Combine(root, CacheDirectoryName);

        if (File.Exists(preferred))
        {
            return FallbackCacheBase();
        }

        if (Directory.Exists(preferred))
        {
            // 目录已存在：确认它本身仍可写（ACL 可能事后收紧）
            return ProgramPaths.IsWritable(preferred) ? preferred : FallbackCacheBase();
        }

        // 目录不存在：只要程序根可写就采用默认位置（首次导入时才真正创建）
        return ProgramPaths.IsWritable(root) ? preferred : FallbackCacheBase();
    }

    /// <summary>统计缓存占用字节数。</summary>
    /// <returns>字节数；目录不存在返回 0。</returns>
    internal long CacheUsage()
    {
        var baseDirectory = CacheBase();

        if (!Directory.Exists(baseDirectory))
        {
            return 0;
        }

        long total = 0;

        foreach (var file in Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception)
            {
                // 单个文件不可读（权限 / 已被删除）时跳过即可
            }
        }

        return total;
    }

    /// <summary>清空工程缓存目录。</summary>
    internal void ClearCache()
    {
        var baseDirectory = CacheBase();
        TryDeleteDirectory(baseDirectory);
        AppLogger.Info($"已清除工程缓存目录：{baseDirectory}");
    }

    /// <summary>
    /// 工程专属缓存目录：<c>&lt;缓存根&gt;/&lt;工程名&gt;-&lt;路径哈希前8位&gt;</c>。
    /// </summary>
    /// <param name="uplrPath">工程文件路径。</param>
    /// <returns>缓存目录路径。</returns>
    /// <remarks>
    /// 哈希取自**绝对路径**：同名但位于不同目录的工程不会互相覆盖。
    /// </remarks>
    internal string ResolveCacheDirectory(string uplrPath)
    {
        var stem = Path.GetFileNameWithoutExtension(uplrPath);
        var absolute = Path.GetFullPath(uplrPath);
        var digest = Convert.ToHexString(
            SHA1.HashData(Encoding.UTF8.GetBytes(absolute)))[..8];

        return Path.Combine(CacheBase(), $"{stem}-{digest}");
    }

    // ===================== 内部辅助 =====================

    private static string FallbackCacheBase() =>
        Path.Combine(ProgramPaths.UserDataDirectory, CacheDirectoryName);

    /// <summary>收集存在且非空的三类资源。</summary>
    /// <returns>资源清单。</returns>
    private ResourceMembers CollectMembers()
    {
        var members = new ResourceMembers();

        members.Add("ust_path", _settings.File.UstPath);
        members.Add("lrc_path", _settings.Player.LrcPath);
        members.Add("music_path", _settings.Project.MusicPath);

        return members;
    }

    private string ResolveLocalPath(string settingName) => settingName switch
    {
        "ust_path" => _settings.File.UstPath,
        "lrc_path" => _settings.Player.LrcPath,
        _ => _settings.Project.MusicPath,
    };

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>序列化 Info.json（4 空格缩进，与 1.1.x 的 <c>indent=4</c> 一致）。</summary>
    /// <param name="info">信息对象。</param>
    /// <returns>JSON 文本。</returns>
    private static string SerializeInfo(JsonObject info) =>
        info.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentSize = 4,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    private static JsonObject? ReadInfoJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();

        try
        {
            return JsonNode.Parse(text.TrimStart('\uFEFF')) as JsonObject;
        }
        catch (JsonException exception)
        {
            throw new ProjectFormatException("Info.json 不是合法 JSON", exception);
        }
    }

    /// <summary>
    /// 安全解压单个成员：路径校验 + 分块流式写入 + 单成员上限。
    /// </summary>
    /// <param name="entry">ZIP 成员。</param>
    /// <param name="destinationDirectory">目标目录。</param>
    /// <returns>实际写入的字节数。</returns>
    /// <exception cref="ProjectFormatException">路径不安全或成员过大。</exception>
    private static long ExtractEntrySafely(ZipArchiveEntry entry, string destinationDirectory)
    {
        // 目录条目也要校验：名为 ../x/ 的穿越条目同样要拒绝
        var target = ProjectPathSafety.ResolveInside(destinationDirectory, entry.FullName);

        if (ProjectPathSafety.IsDirectoryEntry(entry.FullName))
        {
            // 目录条目无需创建：子文件写入时 EnsureDirectory 会自动补父目录
            return 0;
        }

        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var source = entry.Open();
        using var destination = new FileStream(target, FileMode.Create, FileAccess.Write);

        long written = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);

        try
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;

                if (written > MaxMemberSize)
                {
                    throw new ProjectFormatException($"工程文件成员过大，已中止导入：{entry.FullName}");
                }

                destination.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return written;
    }

    /// <summary>暂存目录原子切换后，把设置里的资源路径改指正式缓存目录。</summary>
    /// <param name="stagingDirectory">暂存目录。</param>
    /// <param name="cacheDirectory">正式缓存目录。</param>
    private void RepointResourcePaths(string stagingDirectory, string cacheDirectory)
    {
        var stagingPrefix = Path.GetFullPath(stagingDirectory);
        var finalPrefix = Path.GetFullPath(cacheDirectory);

        RepointOne(stagingPrefix, finalPrefix, _settings.File.UstPath, path => _settings.File.UstPath = path);
        RepointOne(stagingPrefix, finalPrefix, _settings.Player.LrcPath, path => _settings.Player.LrcPath = path);
        RepointOne(stagingPrefix, finalPrefix, _settings.Project.MusicPath, path => _settings.Project.MusicPath = path);
    }

    private static void RepointOne(string fromPrefix, string toPrefix, string path, Action<string> assign)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var absolute = Path.GetFullPath(path);

        // 用「相对路径重拼」而不是字符串替换：大小写与分隔符差异由运行时处理
        var relative = Path.GetRelativePath(fromPrefix, absolute);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return;
        }

        assign(Path.GetFullPath(Path.Combine(toPrefix, relative)));
    }

    /// <summary>多编码回退读取（旧文本格式的编码不确定）。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>内容。</returns>
    private static string ReadTextWithEncodingFallback(string path)
    {
        foreach (var encoding in EncodingsToTry())
        {
            try
            {
                return File.ReadAllText(path, encoding);
            }
            catch (DecoderFallbackException)
            {
                // 换下一个编码
            }
        }

        // 全部失败时用替换字符兜底，保证不因编码问题中断导入
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static Encoding[] EncodingsToTry()
    {
        var list = new List<Encoding>
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true),
        };

        foreach (var name in (string[])["gbk", "gb2312", "shift-jis"])
        {
            try
            {
                list.Add(Encoding.GetEncoding(
                    name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
            }
            catch (ArgumentException)
            {
                // 该代码页在本机不可用：跳过即可，不影响其余编码尝试
            }
        }

        return [.. list];
    }

    private static bool IsTruthy(string value)
    {
        var normalized = value.Trim();
        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateLines(string text)
    {
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r'))
            {
                continue;
            }

            yield return text[start..i];

            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    private static void TryDeleteFile(string path)
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
            // 清理失败无关紧要
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // 目录被占用或权限不足时无法清理：不影响导入结果
        }
    }
}
