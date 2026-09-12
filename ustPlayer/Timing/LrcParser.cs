using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using UstPlayer.Diagnostics;

namespace UstPlayer.Timing;

/// <summary>一行歌词：时间戳 + 文本。</summary>
/// <param name="TimestampSeconds">时间戳（秒）。</param>
/// <param name="Text">歌词文本。</param>
internal readonly record struct LrcLine(double TimestampSeconds, string Text);

/// <summary>
/// LRC 歌词解析 — 从 1.1.x <c>core/player.py</c> 的 <c>_parse_lrc</c> /
/// <c>_update_lrc</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// 需要支持的写法比想象中多（1.1.x 逐条踩过）：
/// </para>
/// <list type="bullet">
///   <item><b>一行多时间戳</b>：<c>[00:05.00][00:06.00]词</c> 表示同一句词出现在两个时刻；</item>
///   <item><b>无毫秒</b>：<c>[00:05]词</c>；</item>
///   <item><b>1 / 2 位毫秒</b>：<c>[00:05.5]</c> 是 500ms、<c>[00:05.50]</c> 是 500ms——
///   位数决定倍率，<b>不是</b>小数位（写成 <c>5</c> 当 5ms 会让歌词整体提前）。</item>
/// </list>
/// <para>
/// 编码不确定：按 UTF-8（含 BOM）→ GBK → GB2312 → Shift-JIS 顺序回退尝试。
/// </para>
/// </remarks>
internal static partial class LrcParser
{
    /// <summary>时间戳匹配：<c>[分:秒(.毫秒)]</c>，毫秒 1..3 位。</summary>
    [GeneratedRegex(@"\[(\d{1,2}):(\d{1,2})(?:\.(\d{1,3}))?\]")]
    private static partial Regex TimestampPattern();

    /// <summary>LRC 文本可能使用的编码（按尝试顺序）。</summary>
    private static readonly string[] CandidateEncodings =
        ["utf-8", "gbk", "gb2312", "shift-jis"];

    /// <summary>
    /// 解析 LRC 文本。
    /// </summary>
    /// <param name="content">LRC 文本。</param>
    /// <returns>按时间戳升序排列的歌词行。</returns>
    /// <remarks>
    /// 无时间戳的行（标题、作者等元信息）与空歌词行都会被跳过——
    /// 与 1.1.x 一致：只显示真正有词的时间点。
    /// </remarks>
    internal static IReadOnlyList<LrcLine> Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var lines = new List<LrcLine>();

        foreach (var rawLine in EnumerateLines(content))
        {
            var matches = TimestampPattern().Matches(rawLine);
            if (matches.Count == 0)
            {
                continue;
            }

            // 歌词取最后一个时间戳之后的内容（一行多时间戳共用同一句词）
            var lastTag = matches[^1];
            var lyric = rawLine[(lastTag.Index + lastTag.Length)..].Trim();

            if (lyric.Length == 0)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                if (TryParseTimestamp(match, out var timestamp))
                {
                    lines.Add(new LrcLine(timestamp, lyric));
                }
            }
        }

        lines.Sort((left, right) => left.TimestampSeconds.CompareTo(right.TimestampSeconds));
        return lines;
    }

    /// <summary>
    /// 从文件读取并解析 LRC（多编码回退）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>歌词行；文件不存在或无法读取时返回空列表。</returns>
    internal static IReadOnlyList<LrcLine> ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        foreach (var encoding in ResolveEncodings())
        {
            try
            {
                return Parse(File.ReadAllText(path, encoding));
            }
            catch (DecoderFallbackException)
            {
                // 换下一个编码
            }
            catch (IOException exception)
            {
                AppLogger.Warning($"读取 LRC 失败：{path}（{exception.Message}）");
                return [];
            }
        }

        // 全部编码都失败时用替换字符兜底，保证歌词不至于完全消失
        AppLogger.Warning($"LRC 编码无法识别，按 UTF-8 容错读取：{path}");
        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>
    /// 根据播放位置推进当前歌词索引。
    /// </summary>
    /// <param name="lines">歌词行（须按时间升序）。</param>
    /// <param name="currentIndex">上一帧的索引（首次传 -1）。</param>
    /// <param name="elapsedSeconds">当前播放位置（秒）。</param>
    /// <returns>新的索引；尚无歌词时返回 -1。</returns>
    /// <remarks>
    /// <para>
    /// 播放时间通常单调递增，因此先从上一索引向后扫；一旦发现时间**回退**
    /// （如音频 seek）就从头重扫，否则会显示错误的歌词（1.1.x 已处理）。
    /// </para>
    /// <para>
    /// 传入上一索引使每帧只需检查少数几行，而不是每帧从头扫全表。
    /// </para>
    /// </remarks>
    internal static int AdvanceIndex(IReadOnlyList<LrcLine> lines, int currentIndex, double elapsedSeconds)
    {
        if (lines.Count == 0)
        {
            return -1;
        }

        var index = currentIndex;

        while (index + 1 < lines.Count && lines[index + 1].TimestampSeconds <= elapsedSeconds)
        {
            index++;
        }

        // 时间回退（如 seek）：从头重扫
        if (index < 0 || lines[index].TimestampSeconds > elapsedSeconds)
        {
            index = -1;

            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TimestampSeconds <= elapsedSeconds)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }
        }

        return index;
    }

    /// <summary>解析单个时间戳标签。</summary>
    /// <param name="match">匹配结果。</param>
    /// <param name="timestamp">时间戳（秒）。</param>
    /// <returns>解析成功返回 <see langword="true"/>。</returns>
    private static bool TryParseTimestamp(Match match, out double timestamp)
    {
        timestamp = 0;

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var millisecondsText = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

        // 位数决定倍率：1 位 → ×100，2 位 → ×10，3 位 → 原值。
        // 这不是"小数位"——把 [00:05.5] 当作 5ms 会让整句歌词提前约半秒。
        var milliseconds = millisecondsText.Length switch
        {
            1 when int.TryParse(millisecondsText, out var one) => one * 100,
            2 when int.TryParse(millisecondsText, out var two) => two * 10,
            3 when int.TryParse(millisecondsText, out var three) => three,
            _ => 0,
        };

        timestamp = (minutes * 60) + seconds + (milliseconds / 1000.0);
        return true;
    }

    /// <summary>按尝试顺序构造编码实例（带失败即抛的解码回退）。</summary>
    /// <returns>编码序列。</returns>
    private static IEnumerable<Encoding> ResolveEncodings()
    {
        EncodingBootstrap.EnsureRegistered();

        // UTF-8 家族：显式用「吞 BOM」的实例，避免 \uFEFF 出现在首行歌词前
        yield return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (var name in CandidateEncodings)
        {
            if (name.StartsWith("utf-8", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Encoding? encoding = null;

            try
            {
                encoding = Encoding.GetEncoding(
                    name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch (ArgumentException)
            {
                // 该代码页不可用：跳过
            }

            if (encoding is not null)
            {
                yield return encoding;
            }
        }
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
}
