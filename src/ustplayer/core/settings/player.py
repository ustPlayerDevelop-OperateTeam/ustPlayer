# settings/player.py — 播放器样式设置子域
"""播放器样式（歌词位置/静默/结束/音高占位符 + LRC 路径），
对应 Settings.json 的 [PlayerSettings] 与 [LyricSettings] 两个分组。

存储值均为语言无关的稳定 key（详见各 _LEGACY_* 迁移表）——
显示文案由 UI 层负责翻译，存储层与 .uplr 工程文件只认 key。
"""

from typing import Dict, Optional

from PySide6.QtCore import QObject, Signal


class PlayerSettings(QObject):
    """播放器样式设置。"""

    lyric_pos_changed = Signal(str)
    silent_display_changed = Signal(str)
    silent_custom_text_changed = Signal(str)
    end_display_changed = Signal(str)
    end_custom_text_changed = Signal(str)
    pitch_placeholder_changed = Signal(str)
    pitch_custom_text_changed = Signal(str)
    lrc_path_changed = Signal(str)

    # ===================== 稳定 key 与旧值迁移表 =====================

    _LYRIC_POSITIONS = ("top", "bottom")
    _SILENT_DISPLAYS = ("r", "dash", "custom", "none")
    _END_DISPLAYS = ("end", "dash", "custom", "none")
    _PITCH_PLACEHOLDERS = ("none", "dash", "custom")

    # 旧版本（i18n 之前）存的是中文显示文案，读取时经迁移表转成新 key
    _LEGACY_LYRIC_POS = {"上": "top", "下": "bottom"}
    _LEGACY_SILENT_DISPLAY = {
        "R": "r", "-": "dash", "自定义文字": "custom", "什么都不显示": "none",
    }
    _LEGACY_END_DISPLAY = {
        "END": "end", "-": "dash", "自定义文字": "custom", "什么都不显示": "none",
    }
    _LEGACY_PITCH_PLACEHOLDER = {"无": "none", "-": "dash", "自定义文字": "custom"}

    # 字段名 → (合法 key 集合, 旧中文迁移表, 默认 key)
    _FIELDS: Dict[str, tuple] = {
        "lyric_pos": (_LYRIC_POSITIONS, _LEGACY_LYRIC_POS, "top"),
        "silent_display": (_SILENT_DISPLAYS, _LEGACY_SILENT_DISPLAY, "r"),
        "end_display": (_END_DISPLAYS, _LEGACY_END_DISPLAY, "end"),
        "pitch_placeholder": (_PITCH_PLACEHOLDERS, _LEGACY_PITCH_PLACEHOLDER, "none"),
    }

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._lyric_pos = "top"
        self._silent_display = "r"
        self._silent_custom_text = ""
        self._end_display = "end"
        self._end_custom_text = ""
        self._pitch_placeholder = "none"
        self._pitch_custom_text = ""
        self._lrc_path = ""

    # ===================== 旧值迁移工具 =====================

    @classmethod
    def migrate_value(cls, field: str, value: str) -> str:
        """把可能来自旧版本的中文存储值迁移为当前稳定 key。

        - 已是合法 key：原样返回；
        - 命中旧中文迁移表：返回对应新 key；
        - 其余非法值：返回该字段默认 key（与 validate 兜底语义一致）。
        """
        valid, legacy, default = cls._FIELDS[field]
        if value in valid:
            return value
        return legacy.get(value, default)

    # ===================== 属性 =====================

    @property
    def lyric_pos(self) -> str:
        return self._lyric_pos

    @lyric_pos.setter
    def lyric_pos(self, v: str):
        if self._lyric_pos != v:
            self._lyric_pos = v
            self.lyric_pos_changed.emit(v)

    @property
    def silent_display(self) -> str:
        return self._silent_display

    @silent_display.setter
    def silent_display(self, v: str):
        if self._silent_display != v:
            self._silent_display = v
            self.silent_display_changed.emit(v)

    @property
    def silent_custom_text(self) -> str:
        return self._silent_custom_text

    @silent_custom_text.setter
    def silent_custom_text(self, v: str):
        if self._silent_custom_text != v:
            self._silent_custom_text = v
            self.silent_custom_text_changed.emit(v)

    @property
    def end_display(self) -> str:
        return self._end_display

    @end_display.setter
    def end_display(self, v: str):
        if self._end_display != v:
            self._end_display = v
            self.end_display_changed.emit(v)

    @property
    def end_custom_text(self) -> str:
        return self._end_custom_text

    @end_custom_text.setter
    def end_custom_text(self, v: str):
        if self._end_custom_text != v:
            self._end_custom_text = v
            self.end_custom_text_changed.emit(v)

    @property
    def pitch_placeholder(self) -> str:
        return self._pitch_placeholder

    @pitch_placeholder.setter
    def pitch_placeholder(self, v: str):
        if self._pitch_placeholder != v:
            self._pitch_placeholder = v
            self.pitch_placeholder_changed.emit(v)

    @property
    def pitch_custom_text(self) -> str:
        return self._pitch_custom_text

    @pitch_custom_text.setter
    def pitch_custom_text(self, v: str):
        if self._pitch_custom_text != v:
            self._pitch_custom_text = v
            self.pitch_custom_text_changed.emit(v)

    @property
    def lrc_path(self) -> str:
        return self._lrc_path

    @lrc_path.setter
    def lrc_path(self, v: str):
        if self._lrc_path != v:
            self._lrc_path = v
            self.lrc_path_changed.emit(v)

    # ===================== 分组读写 =====================

    def read_from(self, config):
        """从 [PlayerSettings] 与 [LyricSettings] 分组读取（旧中文值自动迁移）。"""
        if "PlayerSettings" in config:
            cs = config["PlayerSettings"]
            self._lyric_pos = self.migrate_value(
                "lyric_pos", cs.get("lyric_pos", self._lyric_pos)
            )
            self._silent_display = self.migrate_value(
                "silent_display", cs.get("silent_display", self._silent_display)
            )
            self._silent_custom_text = cs.get("silent_custom_text", self._silent_custom_text)
            self._end_display = self.migrate_value(
                "end_display", cs.get("end_display", self._end_display)
            )
            self._end_custom_text = cs.get("end_custom_text", self._end_custom_text)
            self._pitch_placeholder = self.migrate_value(
                "pitch_placeholder", cs.get("pitch_placeholder", self._pitch_placeholder)
            )
            self._pitch_custom_text = cs.get("pitch_custom_text", self._pitch_custom_text)
        if "LyricSettings" in config:
            self._lrc_path = config["LyricSettings"].get("lrc_path", self._lrc_path)

    def write_to(self, config):
        """写入 [PlayerSettings] 与 [LyricSettings] 分组。"""
        config["PlayerSettings"] = {
            "lyric_pos": self._lyric_pos,
            "silent_display": self._silent_display,
            "silent_custom_text": self._silent_custom_text,
            "end_display": self._end_display,
            "end_custom_text": self._end_custom_text,
            "pitch_placeholder": self._pitch_placeholder,
            "pitch_custom_text": self._pitch_custom_text,
        }
        config["LyricSettings"] = {
            "lrc_path": self._lrc_path,
        }

    def validate(self):
        """枚举校验（兼容旧中文值迁移），非法时回退默认。"""
        if self.lyric_pos not in self._LYRIC_POSITIONS:
            self.lyric_pos = self.migrate_value("lyric_pos", self.lyric_pos)
        if self.silent_display not in self._SILENT_DISPLAYS:
            self.silent_display = self.migrate_value("silent_display", self.silent_display)
        if self.end_display not in self._END_DISPLAYS:
            self.end_display = self.migrate_value("end_display", self.end_display)
        if self.pitch_placeholder not in self._PITCH_PLACEHOLDERS:
            self.pitch_placeholder = self.migrate_value(
                "pitch_placeholder", self.pitch_placeholder
            )
