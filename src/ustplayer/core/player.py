# player.py — 全屏播放器（PySide6 版本）
"""UST 音符可视化播放器，使用 QPainter 渲染全屏动画。

通过 NotePlayerLauncher 实现 contracts.PlayerLauncher 接口，
经 AppContext 统一调用。
"""

import os
import re
import time
from datetime import timedelta
from typing import List, Optional, Tuple

from PySide6.QtWidgets import QApplication, QWidget
from PySide6.QtCore import Qt, QTimer, QRectF, QPointF, QUrl
from PySide6.QtGui import (
    QPainter, QColor, QFont, QFontMetrics, QPen, QPolygonF,
)

try:
    from PySide6.QtMultimedia import QAudioOutput, QMediaPlayer
    _HAS_AUDIO = True
except Exception:  # QtMultimedia 缺失时降级为纯可视化
    QAudioOutput = None
    QMediaPlayer = None
    _HAS_AUDIO = False

try:
    from PySide6.QtMultimedia import QAudioProbe
    _HAS_PROBE = True
except Exception:
    QAudioProbe = None
    _HAS_PROBE = False

from ustplayer.core.contracts import (
    APP_COPYRIGHT,
    APP_NAME,
    PlayerLaunchParams,
    hex_to_rgb,
    validate_hex_color,
)
from ustplayer.core.log import logger


# ===================== 工具函数 =====================

def format_play_time(seconds: float) -> str:
    """秒数 → MM:SS:CC 格式。"""
    try:
        ms = int((seconds - int(seconds)) * 100)
        td = timedelta(seconds=int(seconds))
        return f"{td.seconds // 60:02d}:{td.seconds % 60:02d}:{ms:02d}"
    except Exception:
        return "00:00:00"


# ===================== 播放器窗口 =====================

class NoteLyricDisplay(QWidget):
    """全屏播放器 — QPainter 渲染所有内容。"""

    NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]

    def __init__(self, params: PlayerLaunchParams):
        super().__init__()
        self._params = params

        # ---- 窗口配置 ----
        self.setWindowTitle(APP_NAME)
        self._fullscreen = params.style.fullscreen
        # 窗口标志由启动器在 show 前统一设置

        # ---- 背景色 ----
        self._bg_color_hex = validate_hex_color(params.style.bg_color, "#000000")
        self._bg_color = QColor(self._bg_color_hex)

        # ---- 核心数据 ----
        self.notes = params.ust.notes
        self.tempo = params.ust.tempo
        self.last_valid_lyric = ""
        pb_notes = sum(1 for n in self.notes if len(n.pitch_bend) >= 2)
        logger.info(
            f"播放器初始化 — 音符数={len(self.notes)}, BPM={self.tempo}, "
            f"含PitchBend的音符={pb_notes}"
        )

        # ---- 时间轴 ----
        self.start_real_time = 0.0  # 在 showEvent 中与音乐同步设置
        self.tick_per_second = (self.tempo * 480) / 60
        self.total_tick = sum(max(n.length, 1) for n in self.notes)
        self.note_tick_ranges = self._calc_note_tick_ranges()
        logger.debug(
            f"时间轴 — tick_per_second={self.tick_per_second:.1f}, "
            f"total_tick={self.total_tick}"
        )

        # ---- 显示开关 ----
        sc = params.show
        self.show_bpm = sc.bpm
        self.show_play_time = sc.play_time
        self.show_song_name = sc.song_name
        self.show_song_author = sc.song_author
        self.show_ust_author = sc.ust_author
        self.show_lyric = sc.lyric
        self.curve_show = sc.curve_show
        self.show_phoneme = sc.phoneme
        self.show_midinote = sc.midinote
        self.show_waveform = sc.waveform

        # ---- 项目信息 ----
        pi = params.project
        self.song_name = pi.song_name
        self.song_author = pi.song_author
        self.ust_author = pi.ust_author

        # ---- 播放器样式 ----
        ps = params.style
        self.lyric_pos = ps.lyric_pos
        self.lrc_path = ps.lrc_path
        self.music_path = ps.music_path
        self.silent_display = ps.silent_display
        self.silent_custom_text = ps.silent_custom_text
        self.end_display = ps.end_display
        self.end_custom_text = ps.end_custom_text
        self.pitch_placeholder = ps.pitch_placeholder
        self.pitch_custom_text = ps.pitch_custom_text

        # ---- 颜色 ----
        self.ust_lyric_color = hex_to_rgb(
            validate_hex_color(ps.lyric_color, "#FFFFFF")
        )
        self.note_color = hex_to_rgb(
            validate_hex_color(ps.note_color, "#6c6c6c")
        )
        self.small_font_color_hex = validate_hex_color(
            ps.other_text_color, "#FFFFFF"
        )
        self.pitch_curve_color_hex = validate_hex_color(
            ps.pitch_curve_color, "#FFFFFF"
        )
        self.note_alpha = 225
        self.copyright_alpha = 100

        # ---- LRC 歌词 ----
        self.lrc_lines: List[Tuple[float, str]] = []
        self.current_lrc_idx = -1
        if self.show_lyric and self.lrc_path:
            self._parse_lrc()

        # ---- 屏幕尺寸 & 字体 ----
        screen = QApplication.primaryScreen()
        if screen:
            geo = screen.geometry()
            self.w, self.h = geo.width(), geo.height()
        else:
            self.w, self.h = 1920, 1080
        logger.debug(f"屏幕尺寸: {self.w}x{self.h}")

        self._init_fonts()

        self.note_line_width = 5

        # ---- 当前渲染状态 ----
        self._current_lyric = ""
        self._current_note_name = ""
        self._current_note: Optional[object] = None
        self._play_elapsed = 0.0
        self._last_pb_log_note_idx = -1
        self._note_idx_hint = 0  # 加速音符查找

        # ---- 定时器（16ms ≈ 60fps，画面流畅 + 低 CPU） ----
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)

        # 播放结束后的自动关闭定时器（单次触发，允许 closeEvent 提前停止）
        self._close_timer = QTimer(self)
        self._close_timer.setSingleShot(True)
        self._close_timer.timeout.connect(self.close)

        # ---- 伴奏音频与波形 ----
        self._audio_player = None
        self._audio_output = None
        self._audio_ok = False
        self._media_finished = False
        self._media_duration_s = 0.0
        self._media_finish_real = 0.0
        self._probe = None
        self._waveform_peaks: List[float] = []
        self._init_audio()

        # ---- 音素 / MIDI 号显示状态 ----
        self._current_phoneme = ""
        self._current_midinote = ""

        logger.debug("播放器 __init__ 完成")

    def _init_fonts(self):
        """初始化字体和度量缓存（屏幕尺寸变化后可重新调用）。"""
        note_fs = max(int(self.h * 2 / 3 * 0.4), 50)
        lyric_fs = max(int(self.h * 0.03), 10)
        ust_lyric_fs = max(int(self.h * 2 / 3 * 0.2), 80)

        self.note_font = QFont("等线", note_fs, QFont.Bold)
        self.lyric_font = QFont("等线", lyric_fs)
        self.ust_lyric_font = QFont("等线", ust_lyric_fs, QFont.Bold)
        self.small_font = QFont("等线", 14)
        self.copyright_font = QFont("等线", 12)

        # 缓存 QFontMetrics，避免每帧重复创建
        self._fm_note = QFontMetrics(self.note_font)
        self._fm_lyric = QFontMetrics(self.lyric_font)
        self._fm_ust_lyric = QFontMetrics(self.ust_lyric_font)
        self._fm_small = QFontMetrics(self.small_font)
        self._fm_copyright = QFontMetrics(self.copyright_font)

    def showEvent(self, event):
        """窗口显示后启动定时器。"""
        super().showEvent(event)
        self._update_screen_size()
        logger.info(f"播放器窗口已显示 — 实际尺寸: {self.w}x{self.h}")
        self.start_real_time = time.time()
        self._timer.start(16)
        logger.debug("定时器已启动 (16ms)")

    def resizeEvent(self, event):
        """窗口大小变化时更新尺寸和字体。"""
        super().resizeEvent(event)
        self._update_screen_size()

    def _update_screen_size(self):
        """用实际 widget 尺寸更新 w/h 并重建字体。"""
        new_w, new_h = self.width(), self.height()
        if new_w > 0 and new_h > 0 and (new_w != self.w or new_h != self.h):
            self.w, self.h = new_w, new_h
            self._init_fonts()

    # ===================== 伴奏音频与波形 =====================

    def _init_audio(self):
        """初始化伴奏音频（QMediaPlayer）；缺失/失败时降级为纯可视化计时。"""
        if not _HAS_AUDIO:
            return
        path = (self.music_path or "").strip()
        if not path or not os.path.exists(path):
            logger.info("未配置伴奏音频，使用纯可视化计时")
            return
        try:
            self._audio_output = QAudioOutput(self)
            self._audio_output.setVolume(1.0)
            self._audio_player = QMediaPlayer(self)
            self._audio_player.setAudioOutput(self._audio_output)
            self._audio_player.mediaStatusChanged.connect(self._on_media_status)
            self._audio_player.errorOccurred.connect(self._on_audio_error)
            self._audio_player.setSource(QUrl.fromLocalFile(path))
            self._audio_ok = True
            logger.info(f"伴奏音频已加载: {path}")
        except Exception as e:
            logger.warning(f"音频初始化失败，降级为纯可视化: {e}")
            self._audio_player = None
            self._audio_output = None
            self._audio_ok = False

        if self._audio_ok:
            # 波形探测（Qt6 中 QAudioProbe 已标记 deprecated，仍可用）
            if self.show_waveform and _HAS_PROBE:
                try:
                    self._probe = QAudioProbe(self)
                    self._probe.audioBufferProbed.connect(self._on_audio_buffer)
                    self._probe.setSource(self._audio_player)
                except Exception as e:
                    logger.warning(f"波形探测初始化失败: {e}")
                    self._probe = None
            # 看门狗：3 秒内未进入播放状态则降级（如离屏/无声卡环境）
            QTimer.singleShot(3000, self._check_audio_ready)

    def _on_media_status(self, status):
        """媒体状态变化：就绪后开播；播放结束记录锚点。"""
        try:
            if status == QMediaPlayer.MediaStatus.LoadedMedia:
                if self._audio_player is not None and self._audio_ok:
                    self._audio_player.play()
                    logger.info("伴奏开始播放")
            elif status == QMediaPlayer.MediaStatus.EndOfMedia:
                self._media_finished = True
                self._media_duration_s = (
                    self._audio_player.position() / 1000.0
                    if self._audio_player is not None else 0.0
                )
                self._media_finish_real = time.time()
                logger.info("伴奏播放结束")
        except Exception:
            logger.exception("媒体状态处理异常")

    def _on_audio_error(self, error, error_string):
        """音频错误 → 降级为纯可视化计时。"""
        logger.warning(f"音频错误({error}): {error_string}，降级为纯可视化")
        self._audio_ok = False
        if self._probe is not None:
            self._probe.setSource(None)
            self._probe = None

    def _check_audio_ready(self):
        """看门狗：媒体就绪后未进入播放状态则降级（离屏/无声卡环境）。"""
        if not self._audio_ok or self._audio_player is None:
            return
        try:
            state = self._audio_player.playbackState()
            if state != QMediaPlayer.PlaybackState.PlayingState:
                logger.warning("音频 3 秒内未进入播放状态，降级为纯可视化")
                self._audio_ok = False
                if self._probe is not None:
                    self._probe.setSource(None)
                    self._probe = None
        except Exception:
            pass

    def _on_audio_buffer(self, buffer):
        """音频缓冲回调：提取峰值用于波形显示（异常时静默降级）。"""
        try:
            fmt = buffer.format()
            channel_count = max(fmt.channelCount(), 1)
            data = buffer.constData()
            if data is None:
                return
            raw = bytes(data)
            step = 2 * channel_count
            bucket = 256
            for start in range(0, len(raw) - step, step * bucket):
                peak = 0
                end = min(start + step * bucket, len(raw) - step)
                for i in range(start, end, step):
                    s = int.from_bytes(raw[i:i + 2], "little", signed=True)
                    if abs(s) > peak:
                        peak = abs(s)
                if peak:
                    self._waveform_peaks.append(min(peak / 32768.0, 1.0))
            if len(self._waveform_peaks) > 600:
                del self._waveform_peaks[:len(self._waveform_peaks) - 600]
        except Exception:
            pass

    # ===================== 预计算音符 Tick 区间 =====================

    def _calc_note_tick_ranges(self):
        ranges = []
        current_tick = 0
        for note in self.notes:
            length = max(note.length, 1)
            ranges.append([current_tick, current_tick + length, note])
            current_tick += length
        return ranges

    # ===================== LRC 解析 =====================

    def _parse_lrc(self):
        encodings = ['utf-8-sig', 'utf-8', 'gbk', 'gb2312', 'shift-jis']
        content = ""
        for enc in encodings:
            try:
                with open(self.lrc_path, 'r', encoding=enc) as f:
                    content = f.read()
                break
            except Exception:
                continue
        if not content:
            return

        pattern = r'\[(\d{1,2}):(\d{1,2})\.(\d{2,3})\]([^\[]*)'
        fragments = re.findall(pattern, content)
        for frag in fragments:
            try:
                minutes, seconds, ms = int(frag[0]), int(frag[1]), int(frag[2])
                if len(frag[2]) == 2:
                    ms *= 10
                timestamp = minutes * 60 + seconds + ms / 1000
                lyric = frag[3].strip()
                if lyric:
                    self.lrc_lines.append((timestamp, lyric))
            except Exception:
                continue
        self.lrc_lines.sort(key=lambda x: x[0])

    # ===================== 主循环 =====================

    def _tick(self):
        """定时器回调：计算当前位置 → 更新绘制状态。"""
        try:
            if self._audio_ok and self._audio_player is not None:
                if self._media_finished:
                    # 音频结束后按真实时间继续走完 UST 剩余部分
                    self._play_elapsed = self._media_duration_s + (
                        time.time() - self._media_finish_real
                    )
                else:
                    self._play_elapsed = self._audio_player.position() / 1000.0
            else:
                self._play_elapsed = time.time() - self.start_real_time
            current_tick = self._play_elapsed * self.tick_per_second

            # 播放结束
            if current_tick >= self.total_tick:
                self._current_lyric = self._get_end_text()
                self._current_note_name = ""
                self._current_phoneme = ""
                self._current_midinote = ""
                self._current_note = None
                self.update()
                self._timer.stop()
                logger.info("播放完成，1秒后关闭窗口")
                self._close_timer.start(1000)
                return

            # 匹配当前音符（从上次位置开始查，加速扫描）
            current_note = None
            hint = self._note_idx_hint
            ranges = self.note_tick_ranges
            n = len(ranges)

            # 从 hint 向前查找
            if hint < n and ranges[hint][0] <= current_tick < ranges[hint][1]:
                current_note = ranges[hint][2]
            else:
                # 从 hint 向后扫描
                for i in range(hint, n):
                    if ranges[i][0] <= current_tick < ranges[i][1]:
                        current_note = ranges[i][2]
                        self._note_idx_hint = i
                        break
                # 没找到则从 0 开始重新扫（处理循环或跳转）
                if current_note is None:
                    for i in range(0, hint):
                        if ranges[i][0] <= current_tick < ranges[i][1]:
                            current_note = ranges[i][2]
                            self._note_idx_hint = i
                            break

            if current_note:
                self._process_note(current_note)
                self._current_note = current_note
            else:
                self._current_note = None

            # 更新 LRC
            self._update_lrc()

            self.update()  # 触发 paintEvent

        except Exception:
            logger.exception("_tick 异常")

    def _process_note(self, note):
        """根据音符数据更新当前显示的歌字和音名。"""
        raw_lyric = note.lyric
        note_num = note.note_num

        if raw_lyric == "R":
            self._current_lyric = self._get_silent_text()
            self._current_note_name = ""
        elif raw_lyric == "-":
            self._current_lyric = self.last_valid_lyric or self._get_silent_text()
            self._current_note_name = self._get_pitch_text(note_num)
        else:
            self._current_lyric = raw_lyric
            self.last_valid_lyric = raw_lyric
            self._current_note_name = self._get_pitch_text(note_num)

        # 音素 / MIDI 号（受显示开关控制）
        self._current_phoneme = (note.phoneme or raw_lyric) if self.show_phoneme else ""
        self._current_midinote = str(note_num) if self.show_midinote else ""

    def _update_lrc(self):
        if not self.lrc_lines:
            return
        try:
            new_idx = -1
            for i, (ts, _) in enumerate(self.lrc_lines):
                if ts <= self._play_elapsed:
                    new_idx = i
                else:
                    break
            self.current_lrc_idx = new_idx
        except Exception:
            pass

    # ===================== 绘制（paintEvent） =====================

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        ww, wh = self.width(), self.height()
        painter.fillRect(0, 0, ww, wh, self._bg_color)
        cx, cy = ww // 2, wh // 2

        # ---- 音名 ----
        if self._current_note_name:
            note_c = QColor(*self.note_color)
            note_c.setAlpha(self.note_alpha)
            painter.setPen(note_c)
            painter.setFont(self.note_font)
            fm = self._fm_note
            tw = fm.horizontalAdvance(self._current_note_name)
            th = fm.height()
            pad = th * 0.2
            painter.drawText(
                QRectF(cx - tw / 2 - pad, cy - th / 2 - pad,
                       tw + pad * 2, th + pad * 2),
                Qt.AlignCenter, self._current_note_name,
            )

        # ---- 音高线 ----
        if self.curve_show and self._current_note:
            note = self._current_note
            pb_data = note.pitch_bend
            note_length = note.length
            note_idx = note.index
            if note_idx != self._last_pb_log_note_idx:
                logger.debug(
                    f"音高线: note_idx={note_idx}, pb_len={len(pb_data)}, "
                    f"note_len={note_length}, "
                    f"{'将绘制' if (pb_data and len(pb_data) >= 2 and note_length > 0) else '数据不足，跳过'}"
                )
                self._last_pb_log_note_idx = note_idx
            if pb_data and len(pb_data) >= 2 and note_length > 0:
                curve_width = note_length
                start_x = cx - curve_width // 2
                base_y = cy
                pb_count = len(pb_data)
                points = []
                for i in range(pb_count):
                    x = start_x + (i / (pb_count - 1)) * curve_width
                    y_offset = (pb_data[i] / 100) * (wh * 0.09)
                    y = base_y - y_offset
                    safe_top, safe_bottom = 100, wh - 100
                    if y < safe_top:
                        exceed = safe_top - y
                        scale = max(0.3, 1 - (exceed / wh * 2))
                        y = safe_top - (exceed * scale)
                    elif y > safe_bottom:
                        exceed = y - safe_bottom
                        scale = max(0.3, 1 - (exceed / wh * 2))
                        y = safe_bottom + (exceed * scale)
                    y = max(50, min(y, wh - 50))
                    points.append(QPointF(x, y))
                if len(points) >= 2:
                    pen = QPen(QColor(self.pitch_curve_color_hex))
                    pen.setWidth(self.note_line_width)
                    painter.setPen(pen)
                    painter.drawPolyline(QPolygonF(points))

        # ---- 歌字 ----
        if self._current_lyric:
            lyric_c = QColor(*self.ust_lyric_color)
            painter.setPen(lyric_c)
            painter.setFont(self.ust_lyric_font)
            tw = self._fm_ust_lyric.horizontalAdvance(self._current_lyric)
            th = self._fm_ust_lyric.height()
            pad = th * 0.2
            painter.drawText(
                QRectF(cx - tw / 2 - pad, cy - th / 2 - pad,
                       tw + pad * 2, th + pad * 2),
                Qt.AlignCenter, self._current_lyric,
            )

        # ---- 音素（歌字下方，可选） ----
        if self.show_phoneme and self._current_phoneme:
            painter.setPen(QColor(self.small_font_color_hex))
            painter.setFont(self.small_font)
            ph = self._current_phoneme
            pw = self._fm_small.horizontalAdvance(ph)
            ph_y = cy + self._fm_ust_lyric.height() // 2 + 34
            painter.drawText(cx - pw // 2, ph_y, ph)

        # ---- MIDI 音符号（音名下方，可选） ----
        if self.show_midinote and self._current_midinote:
            midi_c = QColor(*self.note_color)
            painter.setPen(midi_c)
            painter.setFont(self.small_font)
            mt = self._current_midinote
            mw = self._fm_small.horizontalAdvance(mt)
            mt_y = cy + self._fm_note.height() // 2 + 44
            painter.drawText(cx - mw // 2, mt_y, mt)

        # ---- 音频波形（底部，可选） ----
        if self.show_waveform and len(self._waveform_peaks) >= 2:
            peaks = self._waveform_peaks
            n = len(peaks)
            wf_h = wh * 0.12
            base_y = wh - wf_h - 40
            pen = QPen(QColor(self.small_font_color_hex))
            pen.setWidth(2)
            painter.setPen(pen)
            span = ww - 40
            prev_pt = None
            for i, peak in enumerate(peaks):
                x = 20 + (i / (n - 1)) * span
                h = peak * wf_h
                y = base_y + wf_h - h
                if prev_pt is not None:
                    painter.drawLine(prev_pt[0], prev_pt[1], x, y)
                prev_pt = (x, y)

        # ---- 左上角静态信息 ----
        painter.setPen(QColor(self.small_font_color_hex))
        y_off = 20
        if self.show_song_name and self.song_name:
            bf = QFont("等线", 14, QFont.Bold)
            painter.setFont(bf)
            painter.drawText(20, y_off + 14, self.song_name)
            painter.setFont(self.small_font)
            y_off += 27
        if self.show_song_author and self.song_author:
            painter.drawText(20, y_off + 14, self.song_author)
            y_off += 25
        if self.show_ust_author and self.ust_author:
            painter.drawText(20, y_off + 14, self.ust_author)

        # BPM（右上角）
        if self.show_bpm:
            painter.setFont(self.small_font)
            bpm_text = f"BPM={self.tempo}"
            bpm_w = self._fm_small.horizontalAdvance(bpm_text)
            painter.drawText(ww - 20 - bpm_w, 34, bpm_text)

        # 播放时间（左下角）
        if self.show_play_time:
            painter.setFont(self.small_font)
            painter.drawText(20, wh - 20, format_play_time(self._play_elapsed))

        # LRC 歌词
        if self.show_lyric and self.lrc_lines and 0 <= self.current_lrc_idx < len(self.lrc_lines):
            lrc_text = self.lrc_lines[self.current_lrc_idx][1]
            if lrc_text:
                lrc_y = int(wh * 0.3) if self.lyric_pos == "上" else int(wh * 0.7)
                painter.setPen(QColor(self.small_font_color_hex))
                painter.setFont(self.lyric_font)
                lrc_w = self._fm_lyric.horizontalAdvance(lrc_text)
                painter.drawText(ww // 2 - lrc_w // 2, lrc_y, lrc_text)

        # 版权（底部居中）
        copy_c = QColor(195, 195, 195)
        copy_c.setAlpha(self.copyright_alpha)
        painter.setPen(copy_c)
        painter.setFont(self.copyright_font)
        copy_text = APP_COPYRIGHT
        copy_w = self._fm_copyright.horizontalAdvance(copy_text)
        painter.drawText(ww // 2 - copy_w // 2, wh - 20, copy_text)

    # ===================== 文本生成 =====================

    def _get_silent_text(self) -> str:
        if self.silent_display == "R":
            return "R"
        elif self.silent_display == "-":
            return "-"
        elif self.silent_display == "自定义文字":
            return self.silent_custom_text
        return ""

    def _get_end_text(self) -> str:
        if self.end_display == "END":
            return "END"
        elif self.end_display == "-":
            return "-"
        elif self.end_display == "自定义文字":
            return self.end_custom_text
        return ""

    def _get_pitch_text(self, note_num: int) -> str:
        """MIDI 号 → 音名，应用占位符规则。"""
        try:
            ori = self._midi_to_note(note_num)
            pure = re.fullmatch(r'^([A-G])(\d+)$', ori)
            sharp = re.fullmatch(r'^([A-G]#)(\d+)$', ori)

            if sharp:
                return ori
            if pure:
                note, num = pure.group(1), pure.group(2)
                if self.pitch_placeholder == "无":
                    return f"{note}{num}"
                elif self.pitch_placeholder == "-":
                    return f"{note}-{num}"
                elif self.pitch_placeholder == "自定义文字":
                    suffix = self.pitch_custom_text.strip()
                    return f"{note}({suffix}){num}" if suffix else f"{note}{num}"
            return ori
        except Exception:
            pass
        return self._midi_to_note(note_num)

    def _midi_to_note(self, midi_num: int) -> str:
        try:
            midi_num = int(midi_num)
            octave = (midi_num // 12) - 1
            return f"{self.NOTE_NAMES[midi_num % 12]}{octave}"
        except Exception:
            return str(midi_num)

    # ===================== 键盘/关闭事件 =====================

    def keyPressEvent(self, event):
        if event.key() == Qt.Key_Escape:
            self.close()

    def closeEvent(self, event):
        self._timer.stop()
        self._close_timer.stop()
        if self._audio_player is not None:
            self._audio_player.stop()
        if self._probe is not None:
            self._probe.setSource(None)
            self._probe = None
        super().closeEvent(event)


# ===================== 启动器（实现 PlayerLauncher 契约） =====================

class NotePlayerLauncher:
    """播放器启动器 — 实现 contracts.PlayerLauncher 接口。"""

    def launch(self, params: PlayerLaunchParams) -> NoteLyricDisplay:
        """启动播放器窗口，返回窗口引用（调用方需保持引用防止 GC）。

        关键：窗口标志必须在 show/showFullScreen 之前统一设置，
        否则全屏标志和置顶标志互相冲突导致边角漏出。
        """
        logger.info("创建播放器窗口...")
        window = NoteLyricDisplay(params)

        # ---- 在 show 之前统一设置所有窗口标志 ----
        flags = window.windowFlags() | Qt.WindowStaysOnTopHint
        if window._fullscreen:
            flags |= Qt.FramelessWindowHint
        window.setWindowFlags(flags)

        if window._fullscreen:
            window.showFullScreen()
            logger.info("播放器全屏显示")
        else:
            window.show()
            logger.info("播放器窗口显示")
        return window
