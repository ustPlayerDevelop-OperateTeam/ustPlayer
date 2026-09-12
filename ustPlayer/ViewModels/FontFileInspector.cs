using System;
using System.IO;
using System.Text;

namespace UstPlayer.ViewModels;

/// <summary>
/// 字体文件（<c>.ttf</c> / <c>.otf</c>）的轻量检查：扩展名判定 + 从 sfnt 的
/// <c>name</c> 表读出字体族名。
/// </summary>
/// <remarks>
/// <para>
/// 1.1.x 用 <c>QFontDatabase.addApplicationFont()</c> 注册字体文件并取回家族名。
/// Avalonia 没有等价的「把磁盘上的字体文件动态注册进字体管理器」公开 API
/// （<c>FontManager.AddFontCollection</c> 只接受 <c>IFontCollection</c>，而该接口的
/// 初始化路径不对使用者开放），因此这里**不假装注册成功**：只读出族名、
/// 把路径记进 <c>DisplaySettings.CustomFontPaths</c>，真正绘制文字的是渲染器原生库，
/// 它同时拿到族名与路径。
/// </para>
/// <para>
/// 读不出 <c>name</c> 表时回退文件名（去掉扩展名）：这样即使遇到结构特殊的字体，
/// 导入也不会失败，只是族名可能与合作方预期不一致。
/// </para>
/// </remarks>
internal static class FontFileInspector
{
    /// <summary>支持的字体文件扩展名。</summary>
    private static readonly string[] SupportedExtensions = [".ttf", ".otf"];

    /// <summary>TrueType / OpenType 轮廓的 sfnt 版本号 <c>0x00010000</c>。</summary>
    private const uint TrueTypeVersion = 0x00010000;

    /// <summary>CFF 轮廓的 sfnt 版本号 <c>'OTTO'</c>。</summary>
    private const uint CffVersion = 0x4F54544F;

    /// <summary><c>name</c> 表的标签。</summary>
    private const uint NameTableTag = 0x6E616D65;

    /// <summary>判断路径是否是可导入的字体文件（只看扩展名，不要求文件存在）。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>扩展名受支持返回 <see langword="true"/>。</returns>
    internal static bool IsSupportedFontFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path.Trim());

        return Array.Exists(
            SupportedExtensions,
            supported => string.Equals(supported, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 解析字体文件应使用的族名。
    /// </summary>
    /// <param name="path">字体文件路径。</param>
    /// <returns>族名；路径不可用或不是字体文件时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 返回 <see langword="null"/> 表示「这个路径不能作为字体导入」，调用方应提示用户；
    /// 文件存在但 <c>name</c> 表读不出时**不算失败**，回退文件名。
    /// </remarks>
    internal static string? ResolveFamilyName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsSupportedFontFile(path))
        {
            return null;
        }

        var fullPath = path.Trim();

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return TryReadFamilyName(fullPath) ?? Path.GetFileNameWithoutExtension(fullPath);
    }

    /// <summary>
    /// 从 sfnt 的 <c>name</c> 表读取字体族名。
    /// </summary>
    /// <param name="path">字体文件路径。</param>
    /// <returns>族名；结构不可识别时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 优先 <c>nameID = 16</c>（Typographic Family，可变字体的首选），其次 <c>nameID = 1</c>
    /// （Font Family）；两者都优先取 Windows 平台（UTF-16BE）的中性语言记录。
    /// 任何越界、长度不符都只返回 <see langword="null"/>，不抛异常——损坏的字体文件不该
    /// 让页面崩掉。
    /// </remarks>
    internal static string? TryReadFamilyName(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            return ReadFamilyName(stream);
        }
        catch (Exception)
        {
            // IO / 结构错误都视为「读不出族名」，由调用方决定回退策略
            return null;
        }
    }

    /// <summary>从流中读取族名（与文件解耦，便于测试）。</summary>
    /// <param name="stream">字体文件流。</param>
    /// <returns>族名；结构不可识别时返回 <see langword="null"/>。</returns>
    private static string? ReadFamilyName(Stream stream)
    {
        if (stream.Length < 12)
        {
            return null;
        }

        var version = ReadUInt32(stream);

        // 只处理单体字体（TrueType / CFF）；ttcf 字体集合不在此列
        if (version != TrueTypeVersion && version != CffVersion)
        {
            return null;
        }

        // 表目录：numTables / searchRange / entrySelector / rangeShift
        var tableCount = ReadUInt16(stream);
        _ = ReadUInt16(stream);
        _ = ReadUInt16(stream);
        _ = ReadUInt16(stream);

        for (var index = 0; index < tableCount; index++)
        {
            if (stream.Position + 16 > stream.Length)
            {
                return null;
            }

            var tag = ReadUInt32(stream);
            _ = ReadUInt32(stream);
            var offset = ReadUInt32(stream);
            var length = ReadUInt32(stream);

            if (tag != NameTableTag)
            {
                continue;
            }

            if (offset + length > stream.Length || length < 6)
            {
                return null;
            }

            stream.Position = offset;

            return ReadNameTable(stream);
        }

        return null;
    }

    /// <summary>解析 <c>name</c> 表。</summary>
    /// <param name="stream">已定位到 <c>name</c> 表起始处的流。</param>
    /// <returns>族名；没有可用记录时返回 <see langword="null"/>。</returns>
    private static string? ReadNameTable(Stream stream)
    {
        var tableStart = stream.Position;
        _ = ReadUInt16(stream);                       // format
        var recordCount = ReadUInt16(stream);
        var storageOffset = ReadUInt16(stream);

        string? typographicFamily = null;
        string? family = null;

        for (var index = 0; index < recordCount; index++)
        {
            if (stream.Position + 12 > stream.Length)
            {
                break;
            }

            var platformId = ReadUInt16(stream);
            _ = ReadUInt16(stream);                   // encodingID
            _ = ReadUInt16(stream);                   // languageID
            var nameId = ReadUInt16(stream);
            var length = ReadUInt16(stream);
            var offset = ReadUInt16(stream);

            if (nameId is not (1 or 16) || length == 0)
            {
                continue;
            }

            var start = tableStart + storageOffset + offset;

            if (start + length > stream.Length)
            {
                continue;
            }

            var saved = stream.Position;
            stream.Position = start;
            var text = DecodeString(stream, platformId, length);
            stream.Position = saved;

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (nameId == 16 && typographicFamily is null)
            {
                typographicFamily = text;
            }
            else if (nameId == 1 && family is null)
            {
                family = text;
            }
        }

        return typographicFamily ?? family;
    }

    /// <summary>按平台编码解出一段名称字符串。</summary>
    /// <param name="stream">流（已定位到字符串起始处）。</param>
    /// <param name="platformId">平台 ID。</param>
    /// <param name="length">字节长度。</param>
    /// <returns>名称文本。</returns>
    private static string? DecodeString(Stream stream, int platformId, int length)
    {
        var buffer = new byte[length];
        stream.ReadExactly(buffer);

        // 平台 3（Windows）与平台 0（Unicode）用 UTF-16BE；平台 1（Macintosh）多为 MacRoman，
        // 这里按 Latin-1 处理即可覆盖 ASCII 字体名（非 ASCII 名称极罕见）
        var encoding = platformId is 0 or 3
            ? Encoding.BigEndianUnicode
            : Encoding.Latin1;

        return encoding.GetString(buffer).Trim('\0', ' ');
    }

    /// <summary>读大端 16 位整数。</summary>
    /// <param name="stream">流。</param>
    /// <returns>数值。</returns>
    private static ushort ReadUInt16(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);

        return (ushort)((buffer[0] << 8) | buffer[1]);
    }

    /// <summary>读大端 32 位整数。</summary>
    /// <param name="stream">流。</param>
    /// <returns>数值。</returns>
    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);

        return ((uint)buffer[0] << 24) | ((uint)buffer[1] << 16) | ((uint)buffer[2] << 8) | buffer[3];
    }
}
