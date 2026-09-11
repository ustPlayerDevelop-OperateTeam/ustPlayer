# settings/color.py — 颜色设置子域
"""播放器配色，对应 Settings.json 的 [ColorSettings] 分组。"""

from typing import Optional

from PySide6.QtCore import QObject, Signal

from ustplayer.core.contracts import is_valid_hex_color, validate_hex_color


class ColorSettings(QObject):
    """颜色设置（背景/音名/歌字/歌词/音高线/其他文字）。"""

    bg_color_changed = Signal(str)
    note_color_changed = Signal(str)
    lyric_color_changed = Signal(str)
    lyric_text_color_changed = Signal(str)
    other_text_color_changed = Signal(str)
    pitch_curve_color_changed = Signal(str)

    # 属性 → 非法值回退默认
    _FALLBACKS = {
        "bg_color": "#000000",
        "note_color": "#6c6c6c",
        "lyric_color": "#FFFFFF",
        "lyric_text_color": "#FFFFFF",
        "other_text_color": "#FFFFFF",
        "pitch_curve_color": "#FFFFFF",
    }

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._bg_color = "#000000"
        self._note_color = "#6c6c6c"
        self._lyric_color = "#FFFFFF"
        self._lyric_text_color = "#FFFFFF"
        self._other_text_color = "#FFFFFF"
        self._pitch_curve_color = "#FFFFFF"

    @property
    def bg_color(self) -> str:
        return self._bg_color

    @bg_color.setter
    def bg_color(self, v: str):
        v = validate_hex_color(v, self._FALLBACKS["bg_color"])
        if self._bg_color != v:
            self._bg_color = v
            self.bg_color_changed.emit(v)

    @property
    def note_color(self) -> str:
        return self._note_color

    @note_color.setter
    def note_color(self, v: str):
        v = validate_hex_color(v, self._FALLBACKS["note_color"])
        if self._note_color != v:
            self._note_color = v
            self.note_color_changed.emit(v)

    @property
    def lyric_color(self) -> str:
        return self._lyric_color

    @lyric_color.setter
    def lyric_color(self, v: str):
        v = validate_hex_color(v, self._FALLBACKS["lyric_color"])
        if self._lyric_color != v:
            self._lyric_color = v
            self.lyric_color_changed.emit(v)

    @property
    def lyric_text_color(self) -> str:
        return self._lyric_text_color

    @lyric_text_color.setter
    def lyric_text_color(self, v: str):
        v = validate_hex_color(v, self._FALLBACKS["lyric_text_color"])
        if self._lyric_text_color != v:
            self._lyric_text_color = v
            self.lyric_text_color_changed.emit(v)

    @property
    def other_text_color(self) -> str:
        return self._other_text_color

    @other_text_color.setter
    def other_text_color(self, v: str):
        v = validate_hex_color(v, self._FALLBACKS["other_text_color"])
        if self._other_text_color != v:
            self._other_text_color = v
            self.other_text_color_changed.emit(v)

    @property
    def pitch_curve_color(self) -> str:
        return self._pitch_curve_color

    @pitch_curve_color.setter
    def pitch_curve_color(self, v: str):
        v = validate_hex_color(v, self._FALLBACKS["pitch_curve_color"])
        if self._pitch_curve_color != v:
            self._pitch_curve_color = v
            self.pitch_curve_color_changed.emit(v)

    # ===================== 分组读写 =====================

    def read_from(self, config):
        """从 [ColorSettings] 分组读取。"""
        if "ColorSettings" not in config:
            return
        cs = config["ColorSettings"]
        self._bg_color = cs.get("bg_color", self._bg_color)
        self._note_color = cs.get("note_color", self._note_color)
        self._lyric_color = cs.get("lyric_color", self._lyric_color)
        self._lyric_text_color = cs.get("lyric_text_color", self._lyric_text_color)
        self._other_text_color = cs.get("other_text_color", self._other_text_color)
        self._pitch_curve_color = cs.get("pitch_curve_color", self._pitch_curve_color)

    def write_to(self, config):
        """写入 [ColorSettings] 分组。"""
        config["ColorSettings"] = {
            "bg_color": self._bg_color,
            "note_color": self._note_color,
            "lyric_color": self._lyric_color,
            "lyric_text_color": self._lyric_text_color,
            "other_text_color": self._other_text_color,
            "pitch_curve_color": self._pitch_curve_color,
        }

    def validate(self):
        """颜色值校验，非法时回退默认（通过 setter 保证信号同步）。"""
        for attr, fallback in self._FALLBACKS.items():
            if not is_valid_hex_color(getattr(self, attr)):
                setattr(self, attr, fallback)
