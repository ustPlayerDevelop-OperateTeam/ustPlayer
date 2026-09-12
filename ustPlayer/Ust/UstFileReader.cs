using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using UstPlayer.Diagnostics;
using UstPlayer.Models;

namespace UstPlayer.Ust;

/// <summary>
/// UST 文件解析器 — 从 1.1.x <c>core/ustreader.py</c> 的 <c>UstFileReader</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 只处理 <c>.ust</c> 文本；<b>不支持 USTX</b>（<c>.ustx</c>）。
/// </para>
/// <para>
/// 移植时保留了 1.1.x 踩坑后加固的三处行为：
/// </para>
/// <list type="number">
///   <item><b>UTF-8 BOM</b>：UTF-8 家族按「吞掉 BOM」的方式解码。否则 <c>\uFEFF</c> 会残留在
///   首行，使 <c>[#VERSION]</c> / <c>[#SETTING]</c> / 首个音符段匹配失败——版本号丢失，
///   甚至整段音符被静默丢弃。</item>
///   <item><b>速度边界校验</b>：<c>Tempo</c> 为 0 / 负数 / NaN / Inf 时回退 120 BPM
///   （0 会让播放时间轴永远停在第 0 tick）。</item>
///   <item><b>OpenUtau 音高曲线</b>：除传统 <c>PitchBend</c> 外还解析
///   <c>PBS</c>/<c>PBW</c>/<c>PBY</c>/<c>PBM</c>，把 <c>PBS</c>（起点）+ <c>PBY</c>（后续点）
///   合成 <see cref="NoteInfo.PitchBend"/>。</item>
/// </list>
/// </remarks>
internal sealed class UstFileReader
{
    /// <summary>默认编码：日文 UST 通常为 Shift-JIS。</summary>
    public const string DefaultEncoding = "Shift-JIS";

    /// <summary>解析 UST 文件。</summary>
    /// <param name="ustPath">文件路径。</param>
    /// <param name="encoding">文件编码；为空时用 <see cref="DefaultEncoding"/>。</param>
    /// <returns>解析结果。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="DecoderFallbackException">按给定编码无法解码。</exception>
    internal UstInfo Parse(string ustPath, string? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ustPath);

        if (!File.Exists(ustPath))
        {
            throw new FileNotFoundException($"UST 文件不存在：{ustPath}", ustPath);
        }

        var text = File.ReadAllText(ustPath, ResolveEncoding(encoding));

        return ParseText(text);
    }

    /// <summary>解析 UST 文本内容（与 <see cref="Parse"/> 共用同一套规则，便于测试）。</summary>
    /// <param name="text">UST 文本。</param>
    /// <returns>解析结果。</returns>
    internal static UstInfo ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // 显式剥离 UTF-8 BOM：文件路径下 StreamReader 会吞掉它，但直接传入文本时不会。
        // 不在解析器层剥离的话，\uFEFF 会残留在首行，使 [#VERSION] / [#SETTING] / 首个音符段
        // 匹配失败——版本号丢失，甚至整段音符被静默丢弃（1.1.x 的实际 bug）。
        var content = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

        var version = string.Empty;
        var tempo = 120.0;
        var tracks = 1;
        var notes = new List<NoteInfo>();

        var inSetting = false;
        var expectVersion = false;
        NoteInfo? current = null;

        // OpenUtau 风格音高曲线的原始字段：随当前音符暂存，音符结束时合成
        var openUtauStart = string.Empty;
        var openUtauY = string.Empty;

        foreach (var rawLine in EnumerateLines(content))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line == "[#VERSION]")
            {
                FlushNote(ref current, notes, ref openUtauStart, ref openUtauY);
                inSetting = false;
                expectVersion = true;
                continue;
            }

            if (line == "[#SETTING]")
            {
                FlushNote(ref current, notes, ref openUtauStart, ref openUtauY);
                inSetting = true;
                expectVersion = false;
                continue;
            }

            // 音符段，形如 [#0000]
            if (line.Length > 3 &&
                line.StartsWith("[#", StringComparison.Ordinal) &&
                line.EndsWith(']') &&
                IsAllDigits(line.AsSpan(2, line.Length - 3)))
            {
                FlushNote(ref current, notes, ref openUtauStart, ref openUtauY);
                inSetting = false;
                expectVersion = false;
                current = new NoteInfo { Index = line[2..^1] };
                continue;
            }

            // [#VERSION] 段的第一行有效内容即为版本号
            if (expectVersion && line.StartsWith("UST Version", StringComparison.Ordinal))
            {
                version = line;
                expectVersion = false;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (inSetting)
            {
                ReadSetting(key, value, ref tempo, ref tracks);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            switch (key)
            {
                case "Length":
                    if (TryParseInt(value, out var length))
                    {
                        current.Length = length;
                    }

                    break;

                case "Lyric":
                    current.Lyric = value;
                    break;

                case "NoteNum":
                    if (TryParseInt(value, out var noteNumber))
                    {
                        current.NoteNumber = noteNumber;
                    }

                    break;

                case "Phoneme":
                    current.Phoneme = value;
                    break;

                case "PitchBend":
                    current.PitchBend = ParseIntegers(value);
                    break;

                case "PBS":
                    openUtauStart = value;
                    break;

                case "PBY":
                    openUtauY = value;
                    break;

                // PBW / PBM 只影响插值曲线形状；当前按均匀点近似，故仅识别不参与合成
                case "PBW":
                case "PBM":
                    break;
            }
        }

        FlushNote(ref current, notes, ref openUtauStart, ref openUtauY);

        // 速度值边界校验：0 / 负数 / NaN / Inf 都会让下游时间轴失效，统一回退默认值
        if (!double.IsFinite(tempo) || tempo <= 0)
        {
            tempo = 120.0;
        }

        return new UstInfo
        {
            Version = version,
            Tempo = tempo,
            Tracks = tracks,
            Notes = notes,
        };
    }

    /// <summary>把当前音符收尾（合成音高曲线）并加入列表，然后清空暂存。</summary>
    /// <param name="current">当前音符；为 <see langword="null"/> 时只清空暂存。</param>
    /// <param name="notes">目标列表。</param>
    /// <param name="openUtauStart">暂存的 <c>PBS</c> 原文。</param>
    /// <param name="openUtauY">暂存的 <c>PBY</c> 原文。</param>
    private static void FlushNote(
        ref NoteInfo? current,
        List<NoteInfo> notes,
        ref string openUtauStart,
        ref string openUtauY)
    {
        if (current is not null)
        {
            FinalizePitchBend(current, openUtauStart, openUtauY);
            notes.Add(current);
            current = null;
        }

        openUtauStart = string.Empty;
        openUtauY = string.Empty;
    }

    /// <summary>
    /// 合成 OpenUtau 风格音高曲线（<c>PBS</c> 起点 + <c>PBY</c> 后续点）。
    /// </summary>
    /// <param name="note">音符。</param>
    /// <param name="openUtauStart">暂存的 <c>PBS</c> 原文。</param>
    /// <param name="openUtauY">暂存的 <c>PBY</c> 原文。</param>
    /// <remarks>
    /// 传统 UST 直接给 <c>PitchBend=数值列表</c>，此时不做任何事（与 1.1.x 一致：
    /// 有传统值就忽略 OpenUtau 字段）。
    /// </remarks>
    private static void FinalizePitchBend(NoteInfo note, string openUtauStart, string openUtauY)
    {
        if (note.PitchBend.Count > 0)
        {
            return;
        }

        var pitchY = ParseRoundedValues(openUtauY);
        if (pitchY.Count == 0)
        {
            return;
        }

        var pitchStart = ParseRoundedValues(openUtauStart);

        note.PitchBend = new List<int>(pitchY.Count + 1)
        {
            pitchStart.Count > 0 ? pitchStart[0] : 0,
        };
        note.PitchBend.AddRange(pitchY);
    }

    /// <summary>解析 <c>[#SETTING]</c> 段的键值。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    /// <param name="tempo">速度（就地更新）。</param>
    /// <param name="tracks">轨道数（就地更新）。</param>
    private static void ReadSetting(string key, string value, ref double tempo, ref int tracks)
    {
        switch (key)
        {
            case "Tempo":
                // 不校验有限性与正数：留到最后统一回退，与 1.1.x 的收尾校验保持一致
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    tempo = parsed;
                }

                break;

            case "Tracks":
                if (TryParseInt(value, out var parsedTracks))
                {
                    tracks = parsedTracks;
                }

                break;
        }
    }

    /// <summary>把 <c>"1,2,3"</c> 拆成整数列表，非法项跳过（对应 1.1.x <c>_parse_pitch_bend</c>）。</summary>
    /// <param name="value">逗号分隔的数值。</param>
    /// <returns>整数列表。</returns>
    private static List<int> ParseIntegers(string value)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (var part in value.Split(','))
        {
            if (TryParseInt(part, out var number))
            {
                result.Add(number);
            }
        }

        return result;
    }

    /// <summary>
    /// 把可含小数的音分值拆成整数列表（四舍五入），对应 1.1.x <c>_parse_pitch_values</c>。
    /// </summary>
    /// <param name="value">逗号分隔的数值。</param>
    /// <returns>整数列表。</returns>
    private static List<int> ParseRoundedValues(string value)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (var part in value.Split(','))
        {
            if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                double.IsFinite(number))
            {
                // AwayFromZero 对齐 Python 的 round()：银行家舍入在 .5 处结果不同
                result.Add((int)Math.Round(number, MidpointRounding.AwayFromZero));
            }
        }

        return result;
    }

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// 注册代码页编码提供程序（Shift-JIS / GBK 等）。
    /// </summary>
    /// <remarks>
    /// Shift-JIS 是默认的 UST 编码，不注册就等于默认打不开日文工程。
    /// 用模块初始化器保证早于任何一次编码解析。
    /// </remarks>
    [ModuleInitializer]
    // CA2255：分析器不建议库用模块初始化器；此处是刻意的（见上方理由）。
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2255:The ModuleInitializer attribute should not be used in libraries",
        Justification = "必须在任何一次编码解析前注册代码页提供程序，否则默认的 Shift-JIS 会直接不可用。")]
    internal static void RegisterEncodingProvider() => EncodingBootstrap.EnsureRegistered();

    /// <summary>
    /// 解析编码名。UTF-8 家族统一用「吞 BOM」的编码（对应 1.1.x 的 <c>utf-8-sig</c>）。
    /// </summary>
    /// <param name="encoding">编码名；为空用默认值。</param>
    /// <returns>可用于读取的编码。</returns>
    /// <remarks>
    /// 编码名不受支持时**回退 UTF-8 并记录警告**，而不是抛异常：
    /// 用户拿到的应该是「文件读出来是乱码（可换编码重试）」，而不是一个启动即崩的异常。
    /// </remarks>
    private static Encoding ResolveEncoding(string? encoding)
    {
        EncodingBootstrap.EnsureRegistered();

        var name = string.IsNullOrWhiteSpace(encoding) ? DefaultEncoding : encoding;
        var normalized = name.ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty);

        // UTF-8 家族：显式用「吞 BOM」的实例。.NET 的 StreamReader 默认也会吞 BOM，
        // 这里显式化是为了让意图清楚，并与 1.1.x 的 utf-8-sig 行为对齐。
        if (normalized is "utf8" or "utf8sig" or "utf8bom")
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }

        try
        {
            // 解码失败时抛出（由调用方转为用户提示），与 1.1.x 抛 UnicodeDecodeError 的语义一致
            return Encoding.GetEncoding(
                name,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }
        catch (ArgumentException exception)
        {
            AppLogger.Warning($"不支持的编码「{name}」，回退 UTF-8（{exception.Message}）");
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }
    }

    /// <summary>按行枚举文本，兼容 CRLF / LF / CR。</summary>
    /// <param name="text">文本。</param>
    /// <returns>行序列。</returns>
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

            // CRLF 视为一个换行
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

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        foreach (var ch in span)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
