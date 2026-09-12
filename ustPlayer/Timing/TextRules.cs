using System;
using System.Text.RegularExpressions;

namespace UstPlayer.Timing;

/// <summary>空拍 / 静默文字的显示方式（存储层稳定 key：<c>r</c> / <c>dash</c> / <c>custom</c> / <c>none</c>）。</summary>
internal enum SilentDisplayMode
{
    /// <summary>显示 "R"。</summary>
    Rest,

    /// <summary>显示 "-"。</summary>
    Dash,

    /// <summary>显示自定义文字。</summary>
    Custom,

    /// <summary>不显示。</summary>
    None,
}

/// <summary>结束文字的显示方式（存储层稳定 key：<c>end</c> / <c>dash</c> / <c>custom</c> / <c>none</c>）。</summary>
internal enum EndDisplayMode
{
    /// <summary>显示 "END"。</summary>
    End,

    /// <summary>显示 "-"。</summary>
    Dash,

    /// <summary>显示自定义文字。</summary>
    Custom,

    /// <summary>不显示。</summary>
    None,
}

/// <summary>音名占位符规则（存储层稳定 key：<c>none</c> / <c>dash</c> / <c>custom</c>）。</summary>
internal enum PitchPlaceholderMode
{
    /// <summary>直接显示，如 <c>C4</c>。</summary>
    None,

    /// <summary>八度前补占位符，如 <c>C-4</c>；负八度保留符号（<c>C-1</c> → <c>C--1</c>）。</summary>
    Dash,

    /// <summary>八度前插入自定义文字，如 <c>C(升)4</c>。</summary>
    Custom,
}

/// <summary>
/// 播放器文字生成规则 — 从 1.1.x <c>player.py</c> 的
/// <c>_get_silent_text</c> / <c>_get_end_text</c> / <c>_get_pitch_text</c> /
/// <c>_midi_to_note</c> / <c>_process_note</c> 逐条移植。
/// </summary>
/// <remarks>
/// 这些规则是纯函数式的，与 UI、音频、渲染器都无关，因此独立成静态类以便单测覆盖。
/// 「歌字」的当前值与「上一个有效歌词」属于时序状态，由 <see cref="PlaybackSession"/> 持有。
/// </remarks>
internal static class TextRules
{
    /// <summary>十二平均律音名（升号写法）。</summary>
    private static readonly string[] NoteNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private static readonly Regex PureNotePattern = new(@"^([A-G])(-?\d+)$", RegexOptions.Compiled);
    private static readonly Regex SharpNotePattern = new(@"^([A-G]#)(-?\d+)$", RegexOptions.Compiled);

    /// <summary>空拍 / 静默文字。</summary>
    /// <param name="mode">显示方式。</param>
    /// <param name="customText">自定义文字（<see cref="SilentDisplayMode.Custom"/> 时使用）。</param>
    /// <returns>要显示的文字（可能为空串）。</returns>
    internal static string SilentText(SilentDisplayMode mode, string customText) => mode switch
    {
        SilentDisplayMode.Rest => "R",
        SilentDisplayMode.Dash => "-",
        SilentDisplayMode.Custom => customText,
        _ => string.Empty,
    };

    /// <summary>结束文字。</summary>
    /// <param name="mode">显示方式。</param>
    /// <param name="customText">自定义文字（<see cref="EndDisplayMode.Custom"/> 时使用）。</param>
    /// <returns>要显示的文字（可能为空串）。</returns>
    internal static string EndText(EndDisplayMode mode, string customText) => mode switch
    {
        EndDisplayMode.End => "END",
        EndDisplayMode.Dash => "-",
        EndDisplayMode.Custom => customText,
        _ => string.Empty,
    };

    /// <summary>MIDI 音号 → 音名（如 60 → <c>C4</c>）。</summary>
    /// <param name="midiNumber">MIDI 音号。</param>
    /// <returns>音名；非法输入原样返回其字符串形式。</returns>
    internal static string MidiToNote(int midiNumber)
    {
        // C# 的整数除法向零截断，Python 的 // 向下取整；midi 号非负时二者一致，
        // 但音号理论上可能为负，这里显式用向下取整以与 1.1.x 行为完全一致。
        var octave = (int)Math.Floor(midiNumber / 12.0) - 1;
        var index = ((midiNumber % 12) + 12) % 12;
        return $"{NoteNames[index]}{octave}";
    }

    /// <summary>
    /// 音名 + 占位符规则（对应 1.1.x <c>_get_pitch_text</c>）。
    /// </summary>
    /// <param name="midiNumber">MIDI 音号。</param>
    /// <param name="mode">占位符规则。</param>
    /// <param name="customText">自定义占位文字（<see cref="PitchPlaceholderMode.Custom"/> 时使用）。</param>
    /// <returns>显示用音名。</returns>
    /// <remarks>
    /// 升号音名（<c>C#4</c>）**不**应用占位符——1.1.x 即如此，移植时保持。
    /// </remarks>
    internal static string PitchText(int midiNumber, PitchPlaceholderMode mode, string customText)
    {
        var raw = MidiToNote(midiNumber);

        if (SharpNotePattern.Match(raw) is { Success: true })
        {
            return raw;
        }

        if (PureNotePattern.Match(raw) is not { Success: true } pure)
        {
            return raw;
        }

        var note = pure.Groups[1].Value;
        var octave = pure.Groups[2].Value;

        return mode switch
        {
            PitchPlaceholderMode.None => $"{note}{octave}",
            // 负八度保留符号：C-1 → C--1（占位符 + 原八度符号）
            PitchPlaceholderMode.Dash => $"{note}-{octave}",
            PitchPlaceholderMode.Custom => string.IsNullOrEmpty(customText.Trim())
                ? $"{note}{octave}"
                : $"{note}({customText.Trim()}){octave}",
            _ => raw,
        };
    }

    /// <summary>UST 歌词是否为休止符（空拍）。</summary>
    /// <param name="lyric">UST 歌词原文。</param>
    /// <returns>是休止符返回 <see langword="true"/>。</returns>
    internal static bool IsRest(string lyric) => lyric == "R";

    /// <summary>UST 歌词是否为延音符（延续上一个有效歌词）。</summary>
    /// <param name="lyric">UST 歌词原文。</param>
    /// <returns>是延音符返回 <see langword="true"/>。</returns>
    internal static bool IsSustain(string lyric) => lyric == "-";
}
