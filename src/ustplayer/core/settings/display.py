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

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._show_bpm = True
        self._show_play_time = True
        self._show_song_name = True
        self._show_song_author = True
        self._show_ust_author = True
        self._fullscreen = True
        self._show_lyric = False

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
        }
