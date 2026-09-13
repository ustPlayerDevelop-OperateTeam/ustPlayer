using System;
using System.Collections.Generic;

namespace UstPlayer.Timing;

/// <summary>
/// LRC 歌词行的按时间检索。
/// </summary>
/// <remarks>
/// 1.1.x 在播放器内部维护 <c>current_lrc_idx</c>：随播放位置前进，
/// 找到「时间戳不晚于当前播放位置」的最后一行。逻辑很薄，但有两个必须守住的点：
/// <list type="number">
///   <item>播放位置在第一行之前时返回 <c>-1</c>（此时**不该**显示任何歌词，
///   否则开头会先闪一下第一行）；</item>
///   <item>返回的是**下标**而不是文本，因为调用方还要判断「这一行是否为空文本」
///   （空行表示该区间不显示歌词）。</item>
/// </list>
/// </remarks>
internal static class LrcTimeline
{
    /// <summary>
    /// 找出当前应显示的歌词行下标。
    /// </summary>
    /// <param name="lines">按时间升序排列的歌词行。</param>
    /// <param name="elapsedSeconds">当前播放位置（秒）。</param>
    /// <returns>行下标；播放位置还没到第一行时为 <c>-1</c>。</returns>
    public static int FindLineIndex(IReadOnlyList<LrcLine> lines, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return -1;
        }

        var found = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TimestampSeconds <= elapsedSeconds)
            {
                found = i;
                continue;
            }

            // 列表已按时间升序，遇到第一条超前的即可停
            break;
        }

        return found;
    }
}
