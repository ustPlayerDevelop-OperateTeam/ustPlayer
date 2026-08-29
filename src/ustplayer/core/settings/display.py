# settings/display.py — 显示设置子域
"""播放器显示开关，对应 Settings.json 的 [DisplaySettings] 分组。"""

from typing import Optional

from PySide6.QtCore import QObject, Signal

from ustplayer.core.contracts import as_bool


class DisplaySettings(QObject):
    """显示设置（播放器画面开关）。"""

    show_bpm_changed = Signal(bool)
    show_play_time_changed = Signal(bool)
    show_song_name_changed = Signal(bool)
    show_song_author_changed = Signal(bool)
    show_ust_author_changed = Signal(bool)
    fullscreen_changed = Signal(bool)
    show_lyric_changed = Signal(bool)
    show_note_name_changed = Signal(bool)
    show_ust_lyric_changed = Signal(bool)
    show_copyright_changed = Signal(bool)
    font_note_changed = Signal(str)
    font_ust_lyric_changed = Signal(str)
    font_lrc_changed = Signal(str)
    font_other_changed = Signal(str)
    custom_font_paths_changed = Signal(list)

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._show_bpm = True
        self._show_play_time = True
        self._show_song_name = True
        self._show_song_author = True
        self._show_ust_author = True
        self._fullscreen = True
        self._show_lyric = False
        self._show_note_name = True
        self._show_ust_lyric = True
        self._show_copyright = True
        self._font_note = ""
        self._font_ust_lyric = ""
        self._font_lrc = ""
        self._font_other = ""
        self._custom_font_paths: list = []

    @property
    def show_bpm(self) -> bool:
        return self._show_bpm

    @show_bpm.setter
    def show_bpm(self, v: bool):
        if self._show_bpm != v:
            self._show_bpm = v
            self.show_bpm_changed.emit(v)

    @property
    def show_play_time(self) -> bool:
        return self._show_play_time

    @show_play_time.setter
    def show_play_time(self, v: bool):
        if self._show_play_time != v:
            self._show_play_time = v
            self.show_play_time_changed.emit(v)

    @property
    def show_song_name(self) -> bool:
        return self._show_song_name

    @show_song_name.setter
    def show_song_name(self, v: bool):
        if self._show_song_name != v:
            self._show_song_name = v
            self.show_song_name_changed.emit(v)

    @property
    def show_song_author(self) -> bool:
        return self._show_song_author

    @show_song_author.setter
    def show_song_author(self, v: bool):
        if self._show_song_author != v:
            self._show_song_author = v
            self.show_song_author_changed.emit(v)

    @property
    def show_ust_author(self) -> bool:
        return self._show_ust_author

    @show_ust_author.setter
    def show_ust_author(self, v: bool):
        if self._show_ust_author != v:
            self._show_ust_author = v
            self.show_ust_author_changed.emit(v)

    @property
    def fullscreen(self) -> bool:
        return self._fullscreen

    @fullscreen.setter
    def fullscreen(self, v: bool):
        if self._fullscreen != v:
            self._fullscreen = v
            self.fullscreen_changed.emit(v)

    @property
    def show_lyric(self) -> bool:
        return self._show_lyric

    @show_lyric.setter
    def show_lyric(self, v: bool):
        if self._show_lyric != v:
            self._show_lyric = v
            self.show_lyric_changed.emit(v)

    @property
    def show_note_name(self) -> bool:
        return self._show_note_name

    @show_note_name.setter
    def show_note_name(self, v: bool):
        if self._show_note_name != v:
            self._show_note_name = v
            self.show_note_name_changed.emit(v)

    @property
    def show_ust_lyric(self) -> bool:
        return self._show_ust_lyric

    @show_ust_lyric.setter
    def show_ust_lyric(self, v: bool):
        if self._show_ust_lyric != v:
            self._show_ust_lyric = v
            self.show_ust_lyric_changed.emit(v)

    @property
    def show_copyright(self) -> bool:
        return self._show_copyright

    @show_copyright.setter
    def show_copyright(self, v: bool):
        if self._show_copyright != v:
            self._show_copyright = v
            self.show_copyright_changed.emit(v)

    def _font_valid(self, v) -> str:
        """字体族名归一：非字符串回退空串。"""
        return v if isinstance(v, str) else ""

    @property
    def font_note(self) -> str:
        return self._font_note

    @font_note.setter
    def font_note(self, v: str):
        v = self._font_valid(v)
        if self._font_note != v:
            self._font_note = v
            self.font_note_changed.emit(v)

    @property
    def font_ust_lyric(self) -> str:
        return self._font_ust_lyric

    @font_ust_lyric.setter
    def font_ust_lyric(self, v: str):
        v = self._font_valid(v)
        if self._font_ust_lyric != v:
            self._font_ust_lyric = v
            self.font_ust_lyric_changed.emit(v)

    @property
    def font_lrc(self) -> str:
        return self._font_lrc

    @font_lrc.setter
    def font_lrc(self, v: str):
        v = self._font_valid(v)
        if self._font_lrc != v:
            self._font_lrc = v
            self.font_lrc_changed.emit(v)

    @property
    def font_other(self) -> str:
        return self._font_other

    @font_other.setter
    def font_other(self, v: str):
        v = self._font_valid(v)
        if self._font_other != v:
            self._font_other = v
            self.font_other_changed.emit(v)

    @property
    def custom_font_paths(self) -> list:
        return self._custom_font_paths

    @custom_font_paths.setter
    def custom_font_paths(self, v: list):
        if self._custom_font_paths != v:
            self._custom_font_paths = list(v) if v else []
            self.custom_font_paths_changed.emit(self._custom_font_paths)

    # ===================== 分组读写 =====================

    def read_from(self, config):
        """从 [DisplaySettings] 分组读取（宽松 as_bool 解析，避免非法值中断读取）。"""
        if "DisplaySettings" not in config:
            return
        cs = config["DisplaySettings"]
        self._show_bpm = as_bool(cs.get("show_bpm"), self._show_bpm)
        self._show_play_time = as_bool(cs.get("show_play_time"), self._show_play_time)
        self._show_song_name = as_bool(cs.get("show_song_name"), self._show_song_name)
        self._show_song_author = as_bool(cs.get("show_song_author"), self._show_song_author)
        self._show_ust_author = as_bool(cs.get("show_ust_author"), self._show_ust_author)
        self._fullscreen = as_bool(cs.get("fullscreen"), self._fullscreen)
        self._show_lyric = as_bool(cs.get("show_lyric"), self._show_lyric)
        self._show_note_name = as_bool(cs.get("show_note_name"), self._show_note_name)
        self._show_ust_lyric = as_bool(cs.get("show_ust_lyric"), self._show_ust_lyric)
        self._show_copyright = as_bool(cs.get("show_copyright"), self._show_copyright)
        self._font_note = self._font_valid(cs.get("font_note") or "")
        self._font_ust_lyric = self._font_valid(cs.get("font_ust_lyric") or "")
        self._font_lrc = self._font_valid(cs.get("font_lrc") or "")
        self._font_other = self._font_valid(cs.get("font_other") or "")
        raw_paths = cs.get("custom_font_paths") or []
        self._custom_font_paths = (
            [p for p in raw_paths if isinstance(p, str)] if isinstance(raw_paths, list) else []
        )

    def write_to(self, config):
        """写入 [DisplaySettings] 分组。"""
        config["DisplaySettings"] = {
            "show_bpm": "1" if self._show_bpm else "0",
            "show_play_time": "1" if self._show_play_time else "0",
            "show_song_name": "1" if self._show_song_name else "0",
            "show_song_author": "1" if self._show_song_author else "0",
            "show_ust_author": "1" if self._show_ust_author else "0",
            "fullscreen": "1" if self._fullscreen else "0",
            "show_lyric": "1" if self._show_lyric else "0",
            "show_note_name": "1" if self._show_note_name else "0",
            "show_ust_lyric": "1" if self._show_ust_lyric else "0",
            "show_copyright": "1" if self._show_copyright else "0",
            "font_note": self._font_note,
            "font_ust_lyric": self._font_ust_lyric,
            "font_lrc": self._font_lrc,
            "font_other": self._font_other,
            "custom_font_paths": self._custom_font_paths,
        }
