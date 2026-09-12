using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 合成字体文件（测试用）：手写 sfnt 头 + <c>name</c> 表，不依赖本机真实字体。
/// </summary>
/// <remarks>
/// 只构造解析器真正会读的部分（偏移表 + <c>name</c> 表），因此文件很小且完全确定，
/// 在任意平台上都能跑。
/// </remarks>
internal static class SyntheticFontFile
{
    /// <summary>把合成的 TrueType 文件写到指定目录。</summary>
    /// <param name="directory">目录（不存在则创建）。</param>
    /// <param name="fileName">文件名（须以 .ttf / .otf 结尾）。</param>
    /// <param name="records">要写入 <c>name</c> 表的记录（nameID, 文本）。</param>
    /// <returns>文件完整路径。</returns>
    internal static string Write(
        string directory,
        string fileName,
        params (ushort NameId, string Text)[] records)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Create(records));

        return path;
    }

    /// <summary>生成字体文件字节。</summary>
    /// <param name="records">要写入 <c>name</c> 表的记录（nameID, 文本）。</param>
    /// <returns>文件内容。</returns>
    internal static byte[] Create(params (ushort NameId, string Text)[] records)
    {
        var nameTable = new List<byte>();
        var stringStorage = new List<byte>();

        WriteUInt16(nameTable, 0);
        WriteUInt16(nameTable, (ushort)records.Length);

        // 字符串存储紧跟名称记录之后：6 字节表头 + 12 字节 × 记录数
        WriteUInt16(nameTable, (ushort)(6 + (12 * records.Length)));

        foreach (var (nameId, text) in records)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(text);

            WriteUInt16(nameTable, 3);                    // platformID：Windows
            WriteUInt16(nameTable, 1);                    // encodingID：Unicode BMP
            WriteUInt16(nameTable, 0x0409);               // languageID：en-US
            WriteUInt16(nameTable, nameId);
            WriteUInt16(nameTable, (ushort)bytes.Length);
            WriteUInt16(nameTable, (ushort)stringStorage.Count);

            stringStorage.AddRange(bytes);
        }

        nameTable.AddRange(stringStorage);

        var file = new List<byte>();

        // sfnt 偏移表（12 字节）+ 一个 16 字节的表记录
        WriteUInt32(file, 0x00010000);                    // sfntVersion：TrueType
        WriteUInt16(file, 1);                             // numTables
        WriteUInt16(file, 0);                             // searchRange
        WriteUInt16(file, 0);                             // entrySelector
        WriteUInt16(file, 0);                             // rangeShift
        WriteUInt32(file, 0x6E616D65);                    // tag：'name'
        WriteUInt32(file, 0);                             // checksum
        WriteUInt32(file, 28);                            // offset：12 + 16
        WriteUInt32(file, (uint)nameTable.Count);         // length

        file.AddRange(nameTable);

        return [.. file];
    }

    private static void WriteUInt16(List<byte> target, int value)
    {
        target.Add((byte)((value >> 8) & 0xFF));
        target.Add((byte)(value & 0xFF));
    }

    private static void WriteUInt32(List<byte> target, uint value)
    {
        target.Add((byte)((value >> 24) & 0xFF));
        target.Add((byte)((value >> 16) & 0xFF));
        target.Add((byte)((value >> 8) & 0xFF));
        target.Add((byte)(value & 0xFF));
    }
}
