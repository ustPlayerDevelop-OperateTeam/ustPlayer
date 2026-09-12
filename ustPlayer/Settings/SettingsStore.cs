using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using UstPlayer.Diagnostics;

namespace UstPlayer.Settings;

/// <summary>
/// <c>Settings.json</c> 的文件 I/O — 从 1.1.x <c>core/settings_store.py</c> 的
/// <c>SettingsStore</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 只负责文件读写与旧格式迁移，不含任何设置业务逻辑。
/// </para>
/// <para>
/// 三处与 1.1.x 逐字对齐的行为：
/// </para>
/// <list type="number">
///   <item><b>原子写</b>：先写 <c>*.tmp</c> 再整体替换。崩溃或断电不会留下半截配置。</item>
///   <item><b>首选路径不可替换时改写回退路径</b>：首选目录可写并不代表目标文件可替换
///   （只读文件 / ACL 拒绝替换，Windows 常见），此时改写到用户数据目录**并切换后续使用的路径**，
///   避免设置从此静默丢失。</item>
///   <item><b>旧版 <c>Settings.ini</c> 自动迁移</b>：首次运行时把 ini 转成 JSON 并删除旧文件。
///   值里可能含裸 <c>%</c>（如工程名「100% Pure」），因此解析时**不做插值**——
///   1.1.x 正是因为没有关掉 ConfigParser 的插值而崩溃过。</item>
/// </list>
/// </remarks>
internal sealed class SettingsStore
{
    /// <summary>设置文件名。</summary>
    internal const string FileName = "Settings.json";

    /// <summary>旧版设置文件名。</summary>
    internal const string LegacyFileName = "Settings.ini";

    /// <summary>崩溃前写入的临时文件名（保留以兼容 1.1.x 遗留文件）。</summary>
    private const string TempSuffix = ".tmp";

    /// <summary>
    /// JSON 序列化选项：<b>与 1.1.x 的输出逐字对齐</b>。
    /// </summary>
    /// <remarks>
    /// Python 侧是 <c>json.dump(config, f, ensure_ascii=False, indent=2)</c>：
    /// 两空格缩进、中文不转义。这里用同一组设置，使两个版本写出的文件在格式上一致
    /// （便于用户用 diff 核对，也避免无谓的整文件重写）。
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private string _settingsPath;

    /// <summary>创建存储并解析设置文件路径（程序目录优先，不可写时回退用户数据目录）。</summary>
    internal SettingsStore()
        : this(settingsPath: null)
    {
    }

    /// <summary>创建存储并指定设置文件路径。</summary>
    /// <param name="settingsPath">
    /// 设置文件路径；传 <see langword="null"/> 时按默认策略解析
    /// （程序目录优先，不可写时回退用户数据目录）。测试与将来的便携模式都走这里。
    /// </param>
    internal SettingsStore(string? settingsPath)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? ResolveSettingsPath()
            : settingsPath;
    }

    /// <summary>当前使用的设置文件绝对路径（回退后会切换）。</summary>
    internal string SettingsPath => _settingsPath;

    /// <summary>
    /// 读取设置。
    /// </summary>
    /// <returns>配置；文件不存在且无旧格式可迁移时返回空配置。</returns>
    /// <remarks>
    /// 文件损坏（非法 JSON）时返回空配置并记录日志，不抛异常——
    /// 与 1.1.x 一致：坏掉的配置文件不该让程序无法启动。
    /// </remarks>
    internal SettingsConfig Load()
    {
        if (File.Exists(_settingsPath))
        {
            return LoadJsonFile(_settingsPath);
        }

        var legacyPath = ResolveLegacyPath();
        if (legacyPath is not null && File.Exists(legacyPath))
        {
            return MigrateLegacy(legacyPath);
        }

        return SettingsConfig.Empty();
    }

    /// <summary>
    /// 保存设置（原子写；首选路径不可替换时改写回退路径）。
    /// </summary>
    /// <param name="config">配置。</param>
    internal void Save(SettingsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (TrySave(_settingsPath, config))
        {
            return;
        }

        var fallback = ResolveFallbackPath();
        AppLogger.Warning($"首选设置文件不可替换，回退到：{fallback}");

        if (TrySave(fallback, config))
        {
            _settingsPath = fallback;
        }
    }

    /// <summary>
    /// 读取 JSON 设置文件。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>配置。</returns>
    private static SettingsConfig LoadJsonFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);

            // 兼容记事本等编辑器写入的 UTF-8 BOM：BOM 会让 JSON 解析器直接报错
            var node = JsonNode.Parse(text.TrimStart('\uFEFF'));

            if (node is JsonObject root)
            {
                return new SettingsConfig(root);
            }

            AppLogger.Warning($"设置文件格式异常（顶层不是对象），按空配置处理：{path}");
            return SettingsConfig.Empty();
        }
        catch (Exception exception)
        {
            AppLogger.Error($"读取设置文件失败，按空配置处理：{path}", exception);
            return SettingsConfig.Empty();
        }
    }

    /// <summary>原子写入：先写临时文件再替换。</summary>
    /// <param name="targetPath">目标路径。</param>
    /// <param name="config">配置。</param>
    /// <returns>成功返回 <see langword="true"/>。</returns>
    private static bool TrySave(string targetPath, SettingsConfig config)
    {
        var tempPath = targetPath + TempSuffix;

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // UTF-8 无 BOM：与 1.1.x 的 open(..., encoding="utf-8") 一致
            var json = config.Root.ToJsonString(SerializerOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.Move(tempPath, targetPath, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"写入设置失败：{targetPath}（{exception.Message}）");
            return false;
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// 解析设置文件路径：程序目录优先，实际不可写时回退用户数据目录。
    /// </summary>
    /// <returns>设置文件路径。</returns>
    private static string ResolveSettingsPath() =>
        Path.Combine(ProgramPaths.SettingsDirectory(), FileName);

    /// <summary>用户数据目录下的回退路径。</summary>
    /// <returns>设置文件路径。</returns>
    private static string ResolveFallbackPath() =>
        Path.Combine(ProgramPaths.UserDataDirectory, FileName);

    /// <summary>旧版 <c>Settings.ini</c> 的路径（与设置文件同目录逻辑）。</summary>
    /// <returns>路径；目录不可判定时返回 <see langword="null"/>。</returns>
    private static string? ResolveLegacyPath()
    {
        var directory = ProgramPaths.SettingsDirectory();
        return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, LegacyFileName);
    }

    /// <summary>
    /// 把旧版 <c>Settings.ini</c> 迁移为 JSON，成功后删除旧文件。
    /// </summary>
    /// <param name="legacyPath">旧文件路径。</param>
    /// <returns>迁移得到的配置。</returns>
    private static SettingsConfig MigrateLegacy(string legacyPath)
    {
        var root = new JsonObject();

        try
        {
            foreach (var (section, values) in ParseIni(legacyPath))
            {
                var group = new JsonObject();
                foreach (var (key, value) in values)
                {
                    group[key] = value;
                }

                root[section] = group;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error($"解析旧版设置文件失败：{legacyPath}", exception);
            return SettingsConfig.Empty();
        }

        var config = new SettingsConfig(root);

        try
        {
            var store = new SettingsStore();
            store.Save(config);
            File.Delete(legacyPath);
            AppLogger.Info($"已从旧版 {LegacyFileName} 迁移设置至 {FileName}");
        }
        catch (Exception exception)
        {
            AppLogger.Error("旧版设置迁移失败（旧文件保留）", exception);
        }

        return config;
    }

    /// <summary>
    /// 解析 INI 文本（段 → 键值）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>段与键值。</returns>
    /// <remarks>
    /// <b>不做任何插值</b>：值里的 <c>%</c> 按字面处理。1.1.x 曾因 ConfigParser 默认插值
    /// 在含 <c>%</c> 的值上抛 <c>InterpolationSyntaxError</c> 导致迁移崩溃。
    /// 只按第一个 <c>=</c> 切分，值里的 <c>=</c> 原样保留。
    /// </remarks>
    private static IEnumerable<(string Section, List<(string Key, string Value)> Values)> ParseIni(
        string path)
    {
        var result = new List<(string, List<(string, string)>)>();
        List<(string Key, string Value)>? current = null;

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');

            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = [];
                result.Add((line[1..^1].Trim(), current));
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            current?.Add((line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }

        return result;
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
            // 临时文件清理失败无关紧要
        }
    }
}
