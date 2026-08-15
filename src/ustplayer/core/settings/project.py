# settings/project.py — 项目信息设置子域
"""项目信息（项目名/曲名/作者/调音师/伴奏路径），对应 Settings.json 的 [ProjectSettings] 分组。"""

from typing import Optional

from PySide6.QtCore import QObject, Signal


class ProjectSettings(QObject):
    """项目信息设置。"""

    project_name_changed = Signal(str)
    song_name_changed = Signal(str)
    song_author_changed = Signal(str)
    ust_author_changed = Signal(str)
    music_path_changed = Signal(str)

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._project_name = ""
        self._song_name = ""
        self._song_author = ""
        self._ust_author = ""
        self._music_path = ""

    @property
    def project_name(self) -> str:
        return self._project_name

    @project_name.setter
    def project_name(self, v: str):
        if self._project_name != v:
            self._project_name = v
            self.project_name_changed.emit(v)

    @property
    def song_name(self) -> str:
        return self._song_name

    @song_name.setter
    def song_name(self, v: str):
        if self._song_name != v:
            self._song_name = v
            self.song_name_changed.emit(v)

    @property
    def song_author(self) -> str:
        return self._song_author

    @song_author.setter
    def song_author(self, v: str):
        if self._song_author != v:
            self._song_author = v
            self.song_author_changed.emit(v)

    @property
    def ust_author(self) -> str:
        return self._ust_author

    @ust_author.setter
    def ust_author(self, v: str):
        if self._ust_author != v:
            self._ust_author = v
            self.ust_author_changed.emit(v)

    @property
    def music_path(self) -> str:
        return self._music_path

    @music_path.setter
    def music_path(self, v: str):
        if self._music_path != v:
            self._music_path = v
            self.music_path_changed.emit(v)

    # ===================== 分组读写 =====================

    def read_from(self, config):
        """从 [ProjectSettings] 分组读取。"""
        if "ProjectSettings" not in config:
            return
        cs = config["ProjectSettings"]
        self._project_name = cs.get("project_name", self._project_name)
        self._song_name = cs.get("song_name", self._song_name)
        self._song_author = cs.get("song_author", self._song_author)
        self._ust_author = cs.get("ust_author", self._ust_author)
        self._music_path = cs.get("music_path", self._music_path)

    def write_to(self, config):
        """写入 [ProjectSettings] 分组。"""
        config["ProjectSettings"] = {
            "project_name": self._project_name,
            "song_name": self._song_name,
            "song_author": self._song_author,
            "ust_author": self._ust_author,
            "music_path": self._music_path,
        }
