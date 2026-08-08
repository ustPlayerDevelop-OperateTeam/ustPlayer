# ustplayer.py — 全屏播放器（PySide6 版本）
"""UST 音符可视化播放器，使用 QPainter 渲染全屏动画。

替代原 tkinter Canvas 版本，保留全部渲染逻辑。
"""

import re
import time
from datetime import timedelta
from typing import List, Tuple, Optional

from PySide6.QtWidgets import QApplication, QWidget
from PySide6.QtCore import Qt, QTimer, QRectF, QPointF
from PySide6.QtGui import (
    QPainter, QColor, QFont, QFontMetrics, QPen, QPolygonF, QImage,
)

from ustplayer.core.log import logger


# ===================== 工具函数 =====================

def validate_hex_color(hex_color: str) -> str:
    """校验十六进制颜色，无效时返回 #FFFFFF。"""
    if re.match(r'^#([0-9A-Fa-f]{6})$', str(hex_color)):
        return hex_color.strip()
    return "#FFFFFF"


def hex_to_rgb(hex_color: str) -> Tuple[int, int, int]:
    """#RRGGBB → (R, G, B)。"""
    try:
        h = hex_color.lstrip('#')
        return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))
    except Exception:
        return (255, 255, 255)


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

    def __init__(self, ust_info: dict):
        super().__init__()
        self._info = ust_info

        # ---- 窗口配置 ----
        self.setWindowTitle("ustPlayerform")
        self._fullscreen = ust_info["player_style"].get("fullscreen", True)
        # 窗口标志由 display() 在 show 前统一设置

        # ---- 背景色 ----
        self._bg_color_hex = validate_hex_color(
            ust_info["player_style"].get("bg_color", "#000000")
        )
        self._bg_color = QColor(self._bg_color_hex)

        # ---- 核心数据 ----
        self.notes = ust_info.get("notes", [])
        self.tempo = ust_info.get("tempo", 120)
        self.last_valid_lyric = ""
        pb_notes = sum(1 for n in self.notes if len(n.get("pitch_bend", [])) >= 2)
        logger.info(
            f"播放器初始化 — 音符数={len(self.notes)}, BPM={self.tempo}, "
            f"含PitchBend的音符={pb_notes}"
        )

        # ---- 时间轴 ----
        self.start_real_time = 0.0  # 在 showEvent 中与音乐同步设置
        self.tick_per_second = (self.tempo * 480) / 60
        self.total_tick = sum(max(n.get("length", 480), 1) for n in self.notes)
        self.note_tick_ranges = self._calc_note_tick_ranges()
        logger.debug(
            f"时间轴 — tick_per_second={self.tick_per_second:.1f}, "
            f"total_tick={self.total_tick}"
        )

        # ---- 显示开关 ----
        sc = ust_info["show_config"]
        self.show_bpm = sc.get("bpm", True)
        self.show_play_time = sc.get("play_time", True)
        self.show_song_name = sc.get("song_name", True)
        self.show_song_author = sc.get("song_author", True)
        self.show_ust_author = sc.get("ust_author", True)
        self.show_lyric = sc.get("lyric", True)
        self.curve_show = sc.get("curve_show", False)

        # ---- 项目信息 ----
        pi = ust_info.get("project_info", {})
        self.song_name = pi.get("song_name", "")
        self.song_author = pi.get("song_author", "")
        self.ust_author = pi.get("ust_author", "")

        # ---- 播放器样式 ----
        ps = ust_info["player_style"]
        self.lyric_pos = ps.get("lyric_pos", "上")
        self.lrc_path = ps.get("lrc_path", "")
        self.silent_display = ps.get("silent_display", "R")
        self.silent_custom_text = ps.get("silent_custom_text", "")
        self.end_display = ps.get("end_display", "END")
        self.end_custom_text = ps.get("end_custom_text", "")
        self.pitch_placeholder = ps.get("pitch_placeholder", "无")
        self.pitch_custom_text = ps.get("pitch_custom_text", "")
        # ---- 颜色 ----
        self.ust_lyric_color = hex_to_rgb(
            validate_hex_color(ps.get("lyric_color", "#FFFFFF"))
        )
        self.note_color = hex_to_rgb(
            validate_hex_color(ps.get("note_color", "#C3C3C3"))
        )
        self.small_font_color_hex = validate_hex_color(
            ps.get("other_text_color", "#FFFFFF")
        )
        self.pitch_curve_color_hex = validate_hex_color(
            ps.get("pitch_curve_color", "#FFFFFF")
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
        self._current_note: Optional[dict] = None
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

    # ===================== 预计算音符 Tick 区间 =====================

    def _calc_note_tick_ranges(self):
        ranges = []
        current_tick = 0
        for note in self.notes:
            length = max(note.get("length", 480), 1)
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
            self._play_elapsed = time.time() - self.start_real_time
            current_tick = self._play_elapsed * self.tick_per_second

            # 播放结束
            if current_tick >= self.total_tick:
                self._current_lyric = self._get_end_text()
                self._current_note_name = ""
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

    def _process_note(self, note: dict):
        """根据音符数据更新当前显示的歌字和音名。"""
        raw_lyric = note.get("lyric", "")
        note_num = note.get("note_num", 0)

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
            pb_data = note.get("pitch_bend", [])
            note_length = note.get("length", 0)
            note_idx = note.get("index", -1)
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
        copy_text = "ustPlayer- 1.0.0 © 2026 SYEternalR"
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
        super().closeEvent(event)


# ===================== 对外接口 =====================

def display(ust_info: dict) -> NoteLyricDisplay:
    """启动播放器窗口，返回窗口引用（调用方需保持引用防止 GC）。

    关键：窗口标志必须在 show/showFullScreen 之前统一设置，
    否则全屏标志和置顶标志互相冲突导致边角漏出。
    """
    logger.info("创建播放器窗口...")
    window = NoteLyricDisplay(ust_info)

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
