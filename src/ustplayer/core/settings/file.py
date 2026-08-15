# settings/file.py — 文件设置子域
"""UST 路径/编码/音高线开关，对应 Settings.json 的 [FileSettings] 分组。"""

from typing import Optional

from PySide6.QtCore import QObject, Signal

from ustplayer.core.contracts import as_bool


class FileSettings(QObject):
    """文件设置（ust 路径、编码、音高线显示）。"""

    ust_path_changed = Signal(str)
    encoding_changed = Signal(str)
    curve_show_changed = Signal(bool)

    _ENCODINGS = ("UTF-8", "GBK", "Shift-JIS")

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._ust_path = ""
        self._encoding = "Shift-JIS"
        self._curve_show = False

    @property
    def ust_path(self) -> str:
        return self._ust_path

    @ust_path.setter
    def ust_path(self, v: str):
        if self._ust_path != v:
            self._ust_path = v
            self.ust_path_changed.emit(v)

    @property
    def encoding(self) -> str:
        return self._encoding

    @encoding.setter
    def encoding(self, v: str):
        if self._encoding != v:
            self._encoding = v
            self.encoding_changed.emit(v)

    @property
    def curve_show(self) -> bool:
        return self._curve_show

    @curve_show.setter
    def curve_show(self, v: bool):
        if self._curve_show != v:
            self._curve_show = v
            self.curve_show_changed.emit(v)

    # ===================== 分组读写 =====================

    def read_from(self, config):
        """从 [FileSettings] 分组读取。"""
        if "FileSettings" not in config:
            return
        cs = config["FileSettings"]
        self._ust_path = cs.get("ust_path", self._ust_path)
        self._encoding = cs.get("encoding", self._encoding)
        self._curve_show = as_bool(cs.get("curve_show"), self._curve_show)

    def write_to(self, config):
        """写入 [FileSettings] 分组。"""
        config["FileSettings"] = {
            "ust_path": self._ust_path,
            "encoding": self._encoding,
            "curve_show": "1" if self._curve_show else "0",
        }

    def validate(self):
        """编码枚举校验，非法时回退默认。"""
        if self.encoding not in self._ENCODINGS:
            self.encoding = "Shift-JIS"
