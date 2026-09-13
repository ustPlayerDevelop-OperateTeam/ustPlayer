using System;
using System.Globalization;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using UstPlayer.Models;
using UstPlayer.Settings;

namespace UstPlayer.Views.Rendering;

/// <summary>
/// 播放器画面的一帧快照 —— 自绘所需的全部输入。
/// </summary>
/// <remarks>
/// <para>
/// 把「画什么」与「怎么画」分开：本记录由播放时序状态 + 设置组装（见
/// <see cref="PlayerCanvasRenderer.Build"/>），<see cref="PlayerCanvasRenderer"/>
/// 只负责把它画到画布上。这样版式规则可以脱离窗口与时序单独测试。
/// </para>
/// <para>
/// <b>颜色一律是 <see cref="Color"/> 而不是字符串</b>：字符串颜色要经过
/// 「解析 → 存储 → 再解析」，而设置层的字符串在切换语言时可能被本地化文本污染
/// （本仓库踩过：译文覆盖设置值），一旦被污染颜色就静默失效。数值颜色没有这条路径。
/// </para>
/// </remarks>
/// <param name="Background">背景色。</param>
/// <param name="NoteColor">音名颜色。</param>
/// <param name="NoteAlpha">音名透明度（0–255）。</param>
/// <param name="LyricColor">歌字颜色。</param>
/// <param name="SmallTextColor">信息文字颜色（标题 / 作者 / BPM / 时间）。</param>
/// <param name="LrcTextColor">LRC 歌词颜色。</param>
/// <param name="PitchCurveColor">音高曲线颜色。</param>
/// <param name="CopyrightColor">版权文字颜色（含透明度）。</param>
/// <param name="NoteName">要画的音名；空表示不画。</param>
/// <param name="Lyric">要画的歌字；空表示不画。</param>
/// <param name="PitchBend">当前音符的音高数据（原始值，除以 100 后按画布高度换算）。</param>
/// <param name="NoteLengthTicks">当前音符长度（ticks）；用于决定曲线宽度。</param>
/// <param name="PitchLineWidth">音高曲线线宽。</param>
/// <param name="ShowPitchCurve">是否画音高曲线。</param>
/// <param name="LrcText">当前 LRC 歌词行；空表示不画。</param>
/// <param name="LrcAtTop">LRC 歌词靠上（否则靠下）。</param>
/// <param name="SongName">左上角曲名。</param>
/// <param name="SongAuthor">左上角曲师。</param>
/// <param name="UstAuthor">左上角调音师。</param>
/// <param name="BpmText">右上角 BPM 文本；空表示不画。</param>
/// <param name="PlayTimeText">左下角播放时间；空表示不画。</param>
/// <param name="CopyrightText">底部版权文本；空表示不画。</param>
internal sealed record PlayerCanvasSnapshot(
    Color Background,
    Color NoteColor,
    int NoteAlpha,
    Color LyricColor,
    Color SmallTextColor,
    Color LrcTextColor,
    Color PitchCurveColor,
    Color CopyrightColor,
    string? NoteName,
    string? Lyric,
    IReadOnlyList<int> PitchBend,
    int NoteLengthTicks,
    double PitchLineWidth,
    bool ShowPitchCurve,
    string? LrcText,
    bool LrcAtTop,
    string? SongName,
    string? SongAuthor,
    string? UstAuthor,
    string? BpmText,
    string? PlayTimeText,
    string? CopyrightText);

/// <summary>
/// 播放器画面自绘 —— 把 1.1.x <c>player.py</c> 的 <c>paintEvent</c> 版式移植到 Avalonia。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么播放器要自己画，而不是用 uPlRender 渲染器出帧</b>：
/// 渲染器是**为视频导出**设计的（逐帧编码 MP4），它的版式（字号按画布换算、
/// 音名与歌字重叠、元素位置）与 1.1.x 播放器的观感并不一致。
/// 播放器画面属于播放器自己的职责，因此这里按 1.1.x 的绘制规则重写一遍，
/// 渲染器只留给导出使用。
/// </para>
/// <para>
/// 版式规则逐条对齐 <c>player.py</c> 的 <c>paintEvent</c>（绘制顺序也一致，
/// 因为后画的会盖住先画的）：
/// 背景 → 音名（居中）→ 音高曲线 → 歌字（居中）→ 左上信息 → 右上 BPM →
/// 左下时间 → LRC 歌词 → 底部版权。
/// </para>
/// <para>
/// 字号按**窗口高度**换算（与 1.1.x 相同），因此换分辨率观感一致：
/// 音名 <c>h×2/3×0.4</c>（下限 50）、歌字 <c>h×2/3×0.2</c>（下限 80）、
/// LRC <c>h×0.03</c>（下限 10）、信息文字固定 14、版权固定 12。
/// </para>
/// </remarks>
internal static class PlayerCanvasRenderer
{
    /// <summary>音名透明度（对齐 1.1.x 的 <c>note_alpha = 225</c>）。</summary>
    internal const int NoteAlpha = 225;

    /// <summary>版权文字透明度（对齐 1.1.x 的 <c>copyright_alpha = 100</c>）。</summary>
    internal const int CopyrightAlpha = 100;

    /// <summary>音高曲线线宽（对齐 1.1.x 的 <c>note_line_width = 5</c>）。</summary>
    internal const double PitchLineWidth = 5.0;

    /// <summary>信息文字的固定字号（对齐 1.1.x 的 <c>small_font = 14</c>）。</summary>
    private const double SmallFontSize = 14.0;

    /// <summary>版权文字的固定字号（对齐 1.1.x 的 <c>copyright_font = 12</c>）。</summary>
    private const double CopyrightFontSize = 12.0;

    /// <summary>
    /// 组装一帧快照。
    /// </summary>
    /// <param name="style">播放器样式（颜色、字体、歌词位置等）。</param>
    /// <param name="project">项目信息（曲名 / 曲师 / 调音师）。</param>
    /// <param name="show">显示开关。</param>
    /// <param name="state">时序状态。</param>
    /// <param name="note">当前音符（含音高数据）；可为空。</param>
    /// <param name="tempo">速度（BPM），用于右上角文本。</param>
    /// <param name="showPlayTime">是否显示播放时间（显示结束文字时隐藏，与 1.1.x 一致）。</param>
    /// <param name="copyrightText">版权文案。</param>
    /// <returns>快照。</returns>
    public static PlayerCanvasSnapshot Build(
        PlayerStyle style,
        ProjectInfo project,
        ShowConfig show,
        Timing.PlaybackState state,
        NoteInfo? note,
        double tempo,
        bool showPlayTime,
        string copyrightText)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(state);

        return new PlayerCanvasSnapshot(
            Background: ParseColor(style.BackgroundColor, Colors.Black),
            NoteColor: ParseColor(style.NoteColor, Color.FromRgb(0x6C, 0x6C, 0x6C)),
            NoteAlpha: NoteAlpha,
            LyricColor: ParseColor(style.LyricColor, Colors.White),
            SmallTextColor: ParseColor(style.OtherTextColor, Colors.White),
            LrcTextColor: ParseColor(style.LyricTextColor, Colors.White),
            PitchCurveColor: ParseColor(style.PitchCurveColor, Colors.White),
            CopyrightColor: Color.FromArgb(CopyrightAlpha, 195, 195, 195),
            NoteName: show.NoteName ? state.NoteName : null,
            Lyric: show.UstLyric ? state.LyricText : null,
            PitchBend: note?.PitchBend ?? [],
            NoteLengthTicks: note?.Length ?? 0,
            PitchLineWidth: PitchLineWidth,
            ShowPitchCurve: show.CurveShow,
            LrcText: null,
            LrcAtTop: !string.Equals(style.LyricPosition, "bottom", StringComparison.OrdinalIgnoreCase),
            SongName: show.SongName ? project.SongName : null,
            SongAuthor: show.SongAuthor ? project.SongAuthor : null,
            UstAuthor: show.UstAuthor ? project.UstAuthor : null,
            BpmText: show.Bpm ? $"BPM={FormatTempo(tempo)}" : null,
            PlayTimeText: show.PlayTime && showPlayTime ? FormatPlayTime(state.ElapsedSeconds) : null,
            CopyrightText: show.Copyright ? copyrightText : null);
    }

    /// <summary>
    /// 把一帧画到画布上。
    /// </summary>
    /// <param name="context">绘图上下文。</param>
    /// <param name="width">画布宽（像素）。</param>
    /// <param name="height">画布高（像素）。</param>
    /// <param name="snapshot">要画的快照。</param>
    /// <param name="fontFamily">字体族名（空则用默认）。</param>
    public static void Render(
        DrawingContext context,
        double width,
        double height,
        PlayerCanvasSnapshot snapshot,
        string? fontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var centerX = width / 2.0;
        var centerY = height / 2.0;

        // 背景铺满（1.1.x 是 fillRect(0, 0, ww, wh, bg)）
        context.FillRectangle(new ImmutableSolidColorBrush(snapshot.Background), new Rect(0, 0, width, height));

        DrawCenteredText(context, snapshot.NoteName, snapshot.NoteColor, snapshot.NoteAlpha,
            FontSizeNote(height), FontWeight.Bold, centerX, centerY, fontFamily);

        if (snapshot.ShowPitchCurve)
        {
            DrawPitchCurve(context, width, height, snapshot);
        }

        DrawCenteredText(context, snapshot.Lyric, snapshot.LyricColor, 255,
            FontSizeLyric(height), FontWeight.Bold, centerX, centerY, fontFamily);

        DrawStaticInfo(context, width, height, snapshot, fontFamily);
    }

    /// <summary>音名字号（1.1.x：<c>max(int(h*2/3*0.4), 50)</c>）。</summary>
    /// <param name="height">画布高。</param>
    /// <returns>字号。</returns>
    internal static double FontSizeNote(double height) => Math.Max(height * 2.0 / 3.0 * 0.4, 50.0);

    /// <summary>歌字字号（1.1.x：<c>max(int(h*2/3*0.2), 80)</c>）。</summary>
    /// <param name="height">画布高。</param>
    /// <returns>字号。</returns>
    internal static double FontSizeLyric(double height) => Math.Max(height * 2.0 / 3.0 * 0.2, 80.0);

    /// <summary>LRC 歌词字号（1.1.x：<c>max(int(h*0.03), 10)</c>）。</summary>
    /// <param name="height">画布高。</param>
    /// <returns>字号。</returns>
    internal static double FontSizeLrc(double height) => Math.Max(height * 0.03, 10.0);

    /// <summary>
    /// 解析 <c>#RRGGBB</c> 颜色；无法解析时回退。
    /// </summary>
    /// <param name="text">颜色文本。</param>
    /// <param name="fallback">回退色。</param>
    /// <returns>颜色。</returns>
    /// <remarks>
    /// 直接把十六进制当**数字**解析，不做字符串比较：设置层的颜色字符串可能在
    /// 切语言时被本地化文本覆盖（本仓库踩过这个坑），而 <see cref="Color.TryParse"/>
    /// 对非法输入只会静默回退，看不出问题出在哪。
    /// </remarks>
    internal static Color ParseColor(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var value = text.Trim().TrimStart('#');

        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            // 带 alpha 的 8 位写法（#AARRGGBB）也接受
            if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return fallback;
            }

            return Color.FromUInt32(argb);
        }

        return Color.FromUInt32(0xFF000000u | rgb);
    }

    /// <summary>BPM 文本里的速度格式（1.1.x 直接内插浮点值）。</summary>
    /// <param name="tempo">速度。</param>
    /// <returns>可读文本。</returns>
    internal static string FormatTempo(double tempo) =>
        tempo.ToString("0.0###", CultureInfo.InvariantCulture);

    /// <summary>
    /// 播放时间文本（<c>mm:ss.ff</c>，对齐 1.1.x 的 <c>format_play_time</c>）。
    /// </summary>
    /// <param name="seconds">已播放秒数。</param>
    /// <returns>播放时间文本。</returns>
    internal static string FormatPlayTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var total = (int)seconds;
        var minutes = total / 60;
        var secs = total % 60;
        var hundredths = (int)Math.Floor((seconds - total) * 100);

        return $"{minutes:00}:{secs:00}.{hundredths:00}";
    }

    /// <summary>画居中文字（含 1.1.x 的 0.2×行高留白）。</summary>
    /// <param name="context">绘图上下文。</param>
    /// <param name="text">文本；空则不画。</param>
    /// <param name="color">颜色。</param>
    /// <param name="alpha">透明度（0–255）。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="weight">字重。</param>
    /// <param name="centerX">中心 X。</param>
    /// <param name="centerY">中心 Y。</param>
    /// <param name="fontFamily">字体族。</param>
    private static void DrawCenteredText(
        DrawingContext context,
        string? text,
        Color color,
        int alpha,
        double fontSize,
        FontWeight weight,
        double centerX,
        double centerY,
        string? fontFamily)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = CreateText(text, fontSize, weight, WithAlpha(color, alpha), fontFamily);

        context.DrawText(formatted, new Point(centerX - (formatted.Width / 2), centerY - (formatted.Height / 2)));
    }

    /// <summary>
    /// 画音高曲线。
    /// </summary>
    /// <param name="context">绘图上下文。</param>
    /// <param name="width">画布宽。</param>
    /// <param name="height">画布高。</param>
    /// <param name="snapshot">快照。</param>
    /// <remarks>
    /// 逐条对齐 1.1.x：宽度取 <c>min(音符长度(ticks), 屏宽×0.8)</c> 并居中
    /// （音高线宽度直接按 tick 当像素会在超长音符上画出屏幕外，因此必须夹紧），
    /// 纵向换算 <c>(值/100)×(屏高×0.09)</c>，超出安全区（上下各 100px）时按
    /// <c>max(0.3, 1 - 超出量/屏高×2)</c> 压缩回来，最后再夹到 <c>[50, 屏高-50]</c>。
    /// 这套夹紧规则是 1.1.x 为了保证任何长度的曲线都完整可见而写的，照搬以免观感变化。
    /// </remarks>
    private static void DrawPitchCurve(
        DrawingContext context,
        double width,
        double height,
        PlayerCanvasSnapshot snapshot)
    {
        var data = snapshot.PitchBend;

        if (data.Count < 2)
        {
            return;
        }

        var curveWidth = Math.Max(Math.Min(snapshot.NoteLengthTicks, width * 0.8), 1.0);
        var startX = (width / 2.0) - (curveWidth / 2.0);
        var baseline = height / 2.0;

        var pen = new ImmutablePen(
            new ImmutableSolidColorBrush(snapshot.PitchCurveColor),
            snapshot.PitchLineWidth);

        Point? previous = null;

        for (var i = 0; i < data.Count; i++)
        {
            var x = startX + (i / (double)(data.Count - 1) * curveWidth);
            var y = baseline - (data[i] / 100.0 * (height * 0.09));

            const double safeTop = 100.0;
            var safeBottom = height - 100.0;

            if (y < safeTop)
            {
                var exceed = safeTop - y;
                var scale = Math.Max(0.3, 1 - (exceed / height * 2));
                y = safeTop - (exceed * scale);
            }
            else if (y > safeBottom)
            {
                var exceed = y - safeBottom;
                var scale = Math.Max(0.3, 1 - (exceed / height * 2));
                y = safeBottom + (exceed * scale);
            }

            y = Math.Max(50.0, Math.Min(y, height - 50.0));

            var current = new Point(x, y);

            if (previous is { } from)
            {
                context.DrawLine(pen, from, current);
            }

            previous = current;
        }
    }

    /// <summary>画左上角信息、右上 BPM、左下时间、LRC 歌词、底部版权。</summary>
    /// <param name="context">绘图上下文。</param>
    /// <param name="width">画布宽。</param>
    /// <param name="height">画布高。</param>
    /// <param name="snapshot">快照。</param>
    /// <param name="fontFamily">字体族。</param>
    private static void DrawStaticInfo(
        DrawingContext context,
        double width,
        double height,
        PlayerCanvasSnapshot snapshot,
        string? fontFamily)
    {
        const double Margin = 20.0;
        var y = 20.0;

        // 曲名略大且加粗，之后是曲师 / 调音师（1.1.x 的行距 27 / 25）
        if (!string.IsNullOrEmpty(snapshot.SongName))
        {
            var text = CreateText(snapshot.SongName, SmallFontSize, FontWeight.Bold, snapshot.SmallTextColor, fontFamily);
            context.DrawText(text, new Point(Margin, y + 14));
            y += 27;
        }

        if (!string.IsNullOrEmpty(snapshot.SongAuthor))
        {
            var text = CreateText(snapshot.SongAuthor, SmallFontSize, FontWeight.Normal, snapshot.SmallTextColor, fontFamily);
            context.DrawText(text, new Point(Margin, y + 14));
            y += 25;
        }

        if (!string.IsNullOrEmpty(snapshot.UstAuthor))
        {
            var text = CreateText(snapshot.UstAuthor, SmallFontSize, FontWeight.Normal, snapshot.SmallTextColor, fontFamily);
            context.DrawText(text, new Point(Margin, y + 14));
        }

        if (!string.IsNullOrEmpty(snapshot.BpmText))
        {
            var text = CreateText(snapshot.BpmText, SmallFontSize, FontWeight.Normal, snapshot.SmallTextColor, fontFamily);
            context.DrawText(text, new Point(width - Margin - text.Width, 34));
        }

        if (!string.IsNullOrEmpty(snapshot.PlayTimeText))
        {
            var text = CreateText(snapshot.PlayTimeText, SmallFontSize, FontWeight.Normal, snapshot.SmallTextColor, fontFamily);
            context.DrawText(text, new Point(Margin, height - Margin));
        }

        if (!string.IsNullOrEmpty(snapshot.LrcText))
        {
            var text = CreateText(snapshot.LrcText, FontSizeLrc(height), FontWeight.Normal, snapshot.LrcTextColor, fontFamily);
            var lrcY = snapshot.LrcAtTop ? height * 0.3 : height * 0.7;
            context.DrawText(text, new Point((width / 2.0) - (text.Width / 2), lrcY));
        }

        if (!string.IsNullOrEmpty(snapshot.CopyrightText))
        {
            var text = CreateText(snapshot.CopyrightText, CopyrightFontSize, FontWeight.Normal, snapshot.CopyrightColor, fontFamily);
            context.DrawText(text, new Point((width / 2.0) - (text.Width / 2), height - Margin));
        }
    }

    /// <summary>创建一段可测量、可绘制的文本。</summary>
    /// <param name="text">文本。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="weight">字重。</param>
    /// <param name="color">颜色。</param>
    /// <param name="fontFamily">字体族（空则用默认）。</param>
    /// <returns>文本对象。</returns>
    private static FormattedText CreateText(
        string text,
        double fontSize,
        FontWeight weight,
        Color color,
        string? fontFamily)
    {
        var family = string.IsNullOrWhiteSpace(fontFamily)
            ? FontFamily.Default
            : new FontFamily(fontFamily);

        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyle.Normal, weight),
            fontSize,
            new ImmutableSolidColorBrush(color));
    }

    /// <summary>给颜色套一个透明度。</summary>
    /// <param name="color">颜色。</param>
    /// <param name="alpha">透明度（0–255）。</param>
    /// <returns>带透明度的颜色。</returns>
    private static Color WithAlpha(Color color, int alpha) =>
        alpha >= 255 ? color : Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);
}
