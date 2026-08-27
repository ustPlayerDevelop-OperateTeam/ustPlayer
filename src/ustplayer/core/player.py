# player.py — 全屏播放器（PySide6 版本）
"""UST 音符可视化播放器，使用 QPainter 渲染全屏动画。

通过 NotePlayerLauncher 实现 contracts.PlayerLauncher 接口，
经 AppContext 统一调用。
"""

import math
import os
import re
import time
from typing import List, Optional, Tuple

from PySide6.QtWidgets import QApplication, QWidget
from PySide6.QtCore import Qt, QTimer, QRectF, QPointF
from PySide6.QtGui import (
    QPainter, QColor, QFont, QFontMetrics, QPen, QPolygonF,
)

# 音频已封装至 audio_backend.py：QtMultimedia 缺失时 create_audio_backend 返回
# None，播放器自动降级为纯可视化计时，本模块不再直接接触 QMediaPlayer。
from ustplayer.core.audio_backend import AudioBackend, create_audio_backend

from ustplayer.core.contracts import (
    APP_COPYRIGHT,
    APP_NAME,
    NoteInfo,
    PlayerLaunchParams,
    hex_to_rgb,
    validate_hex_color,
)
from ustplayer.core.log import logger


# ===================== 工具函数 =====================

def format_play_time(seconds: float) -> str:
    """秒数 → 时间文本（HH:MM:SS:CC，超过一小时才带小时位）。"""
    try:
        ms = int((seconds - int(seconds)) * 100)
        total = int(seconds)
        h, rem = divmod(total, 3600)
        m, s = divmod(rem, 60)
        if h > 0:
            return f"{h:02d}:{m:02d}:{s:02d}:{ms:02d}"
        return f"{m:02d}:{s:02d}:{ms:02d}"
    except Exception:
        return "00:00:00"


# ===================== 播放器窗口 =====================

TICKS_PER_BEAT = 480  # 每拍（四分音符）的 tick 数，用于换算时间轴


class NoteLyricDisplay(QWidget):
    """全屏播放器 — QPainter 渲染所有内容。"""

    NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]

    def __init__(self, params: PlayerLaunchParams):
        super().__init__()
        self._params = params

        self.setWindowTitle(APP_NAME)
        self.fullscreen = params.style.fullscreen

        self._bg_color_hex = validate_hex_color(params.style.bg_color, "#000000")
        self._bg_color = QColor(self._bg_color_hex)

        self.notes = params.ust.notes
        # 防御性校验：解析器已保证 tempo 合法，但直接构造 PlayerLaunchParams
        # 的调用方可能绕过；0/负数/NaN/Inf 会让时间轴失效，统一回退默认 120。
        tempo = params.ust.tempo
        self.tempo = (
            tempo
            if isinstance(tempo, (int, float)) and math.isfinite(tempo) and tempo > 0
            else 120.0
        )
        self.last_valid_lyric = ""
        pb_notes = sum(1 for n in self.notes if len(n.pitch_bend) >= 2)
        logger.info(
            f"播放器初始化 — 音符数={len(self.notes)}, BPM={self.tempo}, "
            f"含PitchBend的音符={pb_notes}"
        )

        self.start_real_time = 0.0  # 在 showEvent 中与音乐同步设置
        self.tick_per_second = (self.tempo * TICKS_PER_BEAT) / 60
        self.total_tick = sum(max(n.length, 1) for n in self.notes)
        self.note_tick_ranges = self._calc_note_tick_ranges()
        logger.debug(
            f"时间轴 — tick_per_second={self.tick_per_second:.1f}, "
            f"total_tick={self.total_tick}"
        )

        sc = params.show
        self.show_bpm = sc.bpm
        self.show_play_time = sc.play_time
        self.show_song_name = sc.song_name
        self.show_song_author = sc.song_author
        self.show_ust_author = sc.ust_author
        self.show_lyric = sc.lyric
        self.curve_show = sc.curve_show

        pi = params.project
        self.song_name = pi.song_name
        self.song_author = pi.song_author
        self.ust_author = pi.ust_author

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

        self.ust_lyric_color = hex_to_rgb(
            validate_hex_color(ps.lyric_color, "#FFFFFF")
        )
        self.note_color = hex_to_rgb(
            validate_hex_color(ps.note_color, "#6c6c6c")
        )
        self.small_font_color_hex = validate_hex_color(
            ps.other_text_color, "#FFFFFF"
        )
        self.lyric_text_color_rgb = hex_to_rgb(
            validate_hex_color(ps.lyric_text_color, "#FFFFFF")
        )
        self.pitch_curve_color_hex = validate_hex_color(
            ps.pitch_curve_color, "#FFFFFF"
        )
        self.note_alpha = 225
        self.copyright_alpha = 100

        self.lrc_lines: List[Tuple[float, str]] = []
        self.current_lrc_idx = -1
        if self.show_lyric and self.lrc_path:
            self._parse_lrc()

        screen = QApplication.primaryScreen()
        if screen:
            geo = screen.geometry()
            self.w, self.h = geo.width(), geo.height()
        else:
            self.w, self.h = 1920, 1080
        logger.debug(f"屏幕尺寸: {self.w}x{self.h}")

        self._init_fonts()

        self.note_line_width = 5

        self._current_lyric = ""
        self._current_note_name = ""
        self._current_note: Optional[NoteInfo] = None
        self._play_elapsed = 0.0
        self._last_pb_log_note_idx = -1
        self._tick_error_count = 0
        self._note_idx_hint = 0

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)

        self._close_timer = QTimer(self)
        self._close_timer.setSingleShot(True)
        self._close_timer.timeout.connect(self.close)

        self._audio: Optional[AudioBackend] = None
        self._audio_ok = False
        self._media_finished = False
        self._media_duration_s = 0.0
        self._media_finish_real = 0.0
        self._end_shown = False  # 已显示结束文字（之后不再播放音频、隐藏秒表）
        self._audio_ready_checks = 0  # 音频就绪看门狗重试次数（超限防“永远加载中”卡死）
        self._play_issued = False  # 是否已调用过 play()（播完后不再重播）
        # 降级锚点：音频失效瞬间重锚定墙钟零点，避免时间轴跳变（_audio_degraded_real=0 表示未降级）
        self._audio_degraded_at = 0.0
        self._audio_degraded_real = 0.0
        self._init_audio()

        logger.debug("播放器 __init__ 完成")

    def _init_fonts(self):
        """初始化字体和度量缓存（屏幕尺寸变化后可重新调用）。"""
        is_cjk = self._is_cjk_locale()
        family = "等线" if is_cjk else "Segoe UI"

        note_fs = max(int(self.h * 2 / 3 * 0.4), 50)
        lyric_fs = max(int(self.h * 0.03), 10)
        ust_lyric_fs = max(int(self.h * 2 / 3 * 0.2), 80)

        self.note_font = QFont(family, note_fs, QFont.Weight.Bold)
        self.lyric_font = QFont(family, lyric_fs)
        self.ust_lyric_font = QFont(family, ust_lyric_fs, QFont.Weight.Bold)
        self.small_font = QFont(family, 14)
        self.copyright_font = QFont(family, 12)
        self.title_font = QFont(family, 14, QFont.Weight.Bold)

        self._fm_note = QFontMetrics(self.note_font)
        self._fm_lyric = QFontMetrics(self.lyric_font)
        self._fm_ust_lyric = QFontMetrics(self.ust_lyric_font)
        self._fm_small = QFontMetrics(self.small_font)
        self._fm_copyright = QFontMetrics(self.copyright_font)

    @staticmethod
    def _is_cjk_locale() -> bool:
        try:
            from ustplayer.core.i18n import current_locale
            return current_locale().startswith(("zh", "ja", "ko"))
        except Exception:
            return True

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

    # ===================== 伴奏音频 =====================

    def _init_audio(self):
        """初始化伴奏音频后端；缺失/失败时降级为纯可视化计时。"""
        path = (self.music_path or "").strip()
        if not path or not os.path.exists(path):
            logger.info("未配置伴奏音频，使用纯可视化计时")
            return
        backend = create_audio_backend(parent=self)
        if backend is None:
            logger.info("QtMultimedia 不可用，使用纯可视化计时")
            return
        try:
            backend.media_ready.connect(self._on_media_ready)
            backend.media_ended.connect(self._on_media_ended)
            backend.media_error.connect(self._on_audio_error)
            backend.load(path)
        except Exception as e:
            logger.warning(f"音频初始化失败，降级为纯可视化: {e}")
            return
        self._audio = backend
        self._audio_ok = True
        logger.info(f"伴奏音频已加载: {path}")
        QTimer.singleShot(3000, self._check_audio_ready)

    def _on_media_ready(self):
        """媒体加载完成 → 首次 play()；播完（_media_finished）或已进入结束态后不再重播。"""
        if (
            self._audio is not None
            and self._audio_ok
            and not self._play_issued
            and not self._media_finished
            and not self._end_shown
        ):
            self._play_issued = True
            self._audio.play()
            logger.info("伴奏开始播放")

    def _on_media_ended(self):
        """播放到结尾：记录结束锚点（时长优先，未知时退回当前位置）。"""
        self._media_finished = True
        dur = self._audio.duration_seconds() if self._audio is not None else 0.0
        pos = self._audio.position_seconds() if self._audio is not None else 0.0
        self._media_duration_s = dur if dur > 0 else pos
        self._media_finish_real = time.time()
        logger.info("伴奏播放结束")

    def _on_audio_error(self, message: str):
        logger.warning(f"音频错误：{message}，降级为纯可视化")
        self._degrade_audio()

    def _degrade_audio(self):
        """降级为纯可视化：以当前播放位置为墙钟零点重锚定，避免时间轴跳变。"""
        self._audio_ok = False
        self._audio_degraded_at = self._play_elapsed
        self._audio_degraded_real = time.time()

    def _check_audio_ready(self):
        """看门狗：媒体就绪后未进入播放状态则降级（离屏/无声卡环境）。

        仅当媒体确已加载（LoadedMedia/BufferedMedia）但不在播放状态时才降级；
        仍在加载中时再等 3 秒；媒体无效时立即降级。
        """
        if not self._audio_ok or self._audio is None:
            return
        try:
            if self._audio.is_loaded():
                if not self._audio.is_playing():
                    logger.warning("音频已加载但未进入播放状态，降级为纯可视化")
                    self._degrade_audio()
            elif self._audio.is_invalid():
                logger.warning("音频媒体无效，降级为纯可视化")
                self._degrade_audio()
            elif self._audio.is_loading():
                # 仍在加载：再等 3 秒；超过总次数后强制降级，避免“永远加载中”卡死
                self._audio_ready_checks += 1
                if self._audio_ready_checks >= 3:
                    logger.warning("音频长时间未就绪，降级为纯可视化")
                    self._degrade_audio()
                else:
                    logger.debug("音频仍在加载中，3 秒后再次检查")
                    QTimer.singleShot(3000, self._check_audio_ready)
            # 其他状态（NoMedia / Unbuffered / EndOfMedia）暂不处理
        except Exception:
            pass

    # ===================== 预计算音符 Tick 区间 =====================

    def _calc_note_tick_ranges(self) -> List[Tuple[int, int, NoteInfo]]:
        ranges: List[Tuple[int, int, NoteInfo]] = []
        current_tick = 0
        for note in self.notes:
            length = max(note.length, 1)
            ranges.append((current_tick, current_tick + length, note))
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

    def _resolve_end_step(self, current_tick: float) -> Optional[str]:
        """根据当前位置与音频状态判定收尾步骤。

        - 'end'：显示结束文字（有音频时=音频播完且内容结束；无音频时=tick 内容结束）。
        - 'silent'：显示空拍/静默文字（有音频、内容已结束但音频未播完）。
        - None：继续正常显示音符。
        """
        if self._audio_ok:
            content_done = current_tick >= self.total_tick
            if self._media_finished and content_done:
                return "end"
            if content_done:
                return "silent"
            return None
        if current_tick >= self.total_tick:
            return "end"
        return None

    def _tick(self):
        """定时器回调：计算当前位置 → 更新绘制状态。"""
        try:
            if self._audio_ok and self._audio is not None:
                if self._media_finished:
                    self._play_elapsed = self._media_duration_s + (
                        time.time() - self._media_finish_real
                    )
                else:
                    self._play_elapsed = self._audio.position_seconds()
            elif self._audio_degraded_real:
                # 音频已降级：从降级瞬间的位置 + 墙钟增量继续，时间轴连续不跳变
                self._play_elapsed = self._audio_degraded_at + (
                    time.time() - self._audio_degraded_real
                )
            else:
                self._play_elapsed = time.time() - self.start_real_time
            current_tick = self._play_elapsed * self.tick_per_second

            step = self._resolve_end_step(current_tick)
            if step == "end":
                # 音频播完（或无音频内容结束）→ 显示结束文字，1 秒后关闭（之后不再有音频）
                self._end_shown = True
                self._current_lyric = self._get_end_text()
                self._current_note_name = ""
                self._current_note = None
                if self._audio_ok and self._audio is not None:
                    # 立刻停止音频，避免结束后被重复触发播放
                    self._audio.stop()
                self.update()
                self._timer.stop()
                logger.info("播放完成，1秒后关闭窗口")
                self._close_timer.start(1000)
                return
            if step == "silent":
                # 音符内容结束、但音频仍未播完 → 显示空拍/静默文字，继续等待音频
                self._current_lyric = self._get_silent_text()
                self._current_note_name = ""
                self._current_note = None
                self.update()
                return

            current_note = None
            hint = self._note_idx_hint
            ranges = self.note_tick_ranges
            n = len(ranges)

            # 从 hint 向前找，找不到再向后、最后从头扫
            if hint < n and ranges[hint][0] <= current_tick < ranges[hint][1]:
                current_note = ranges[hint][2]
            else:
                for i in range(hint, n):
                    if ranges[i][0] <= current_tick < ranges[i][1]:
                        current_note = ranges[i][2]
                        self._note_idx_hint = i
                        break
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

            self._update_lrc()

            self.update()

        except Exception:
            self._tick_error_count += 1
            if self._tick_error_count % 60 == 1:
                logger.exception("_tick 异常")

    def _process_note(self, note: NoteInfo):
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

    def _update_lrc(self):
        if not self.lrc_lines:
            return
        try:
            lines = self.lrc_lines
            n = len(lines)
            # 播放时间单调递增：从上次索引向后扫，避免每帧从头扫
            idx = self.current_lrc_idx
            while idx + 1 < n and lines[idx + 1][0] <= self._play_elapsed:
                idx += 1
            # 时间回退（如音频 seek）时退回正确位置
            if idx < 0 or lines[idx][0] > self._play_elapsed:
                idx = -1
                for i, (ts, _) in enumerate(lines):
                    if ts <= self._play_elapsed:
                        idx = i
                    else:
                        break
            self.current_lrc_idx = idx
        except Exception:
            pass

    # ===================== 绘制（paintEvent） =====================

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        ww, wh = self.width(), self.height()
        painter.fillRect(0, 0, ww, wh, self._bg_color)
        cx, cy = ww // 2, wh // 2

        # 音名
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
                Qt.AlignmentFlag.AlignCenter, self._current_note_name,
            )

        # 音高线
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

        # 歌字
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
                Qt.AlignmentFlag.AlignCenter, self._current_lyric,
            )

        # 左上角静态信息
        painter.setPen(QColor(self.small_font_color_hex))
        y_off = 20
        if self.show_song_name and self.song_name:
            painter.setFont(self.title_font)
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

        # 播放时间（左下角）—— 显示结束文字时隐藏秒表
        if self.show_play_time and not self._end_shown:
            painter.setFont(self.small_font)
            painter.drawText(20, wh - 20, format_play_time(self._play_elapsed))

        # LRC 歌词（颜色由「歌词色」控制）
        if self.show_lyric and self.lrc_lines and 0 <= self.current_lrc_idx < len(self.lrc_lines):
            lrc_text = self.lrc_lines[self.current_lrc_idx][1]
            if lrc_text:
                lrc_y = int(wh * 0.3) if self.lyric_pos == "top" else int(wh * 0.7)
                painter.setPen(QColor(*self.lyric_text_color_rgb))
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
        if self.silent_display == "r":
            return "R"
        elif self.silent_display == "dash":
            return "-"
        elif self.silent_display == "custom":
            return self.silent_custom_text
        return ""

    def _get_end_text(self) -> str:
        if self.end_display == "end":
            return "END"
        elif self.end_display == "dash":
            return "-"
        elif self.end_display == "custom":
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
                if self.pitch_placeholder == "none":
                    return f"{note}{num}"
                elif self.pitch_placeholder == "dash":
                    return f"{note}-{num}"
                elif self.pitch_placeholder == "custom":
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
        if event.key() == Qt.Key.Key_Escape:
            self.close()

    def closeEvent(self, event):
        self._timer.stop()
        self._close_timer.stop()
        if self._audio is not None:
            self._audio.stop()
        super().closeEvent(event)


# ===================== 启动器（实现 PlayerLauncher 契约） =====================

class NotePlayerLauncher:
    """播放器启动器 — 实现 contracts.PlayerLauncher 接口。"""

    def launch(self, params: PlayerLaunchParams) -> NoteLyricDisplay:
        """启动播放器窗口，返回引用（调用方需保持引用防止 GC）。

        窗口标志必须在 show/showFullScreen 之前统一设置，
        否则全屏与置顶标志互相冲突导致边角漏出。
        """
        logger.info("创建播放器窗口...")
        window = NoteLyricDisplay(params)

        flags = window.windowFlags() | Qt.WindowType.WindowStaysOnTopHint
        if window.fullscreen:
            flags |= Qt.WindowType.FramelessWindowHint
        window.setWindowFlags(flags)

        if window.fullscreen:
            window.showFullScreen()
            logger.info("播放器全屏显示")
        else:
            window.show()
            logger.info("播放器窗口显示")
        return window
