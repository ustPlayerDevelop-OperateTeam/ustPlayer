# settings/language.py — 语言设置子域
"""界面语言设置，对应 Settings.json 的 [LanguageSettings] 分组。

- `language` 存储值：受支持的语言代码（"zh_CN" / "en_US"），或特殊值
  "system" 表示跟随系统语言（启动时解析为实际语言代码，不持久化为具体代码，
  以便系统语言变化后自动跟随）；
- 与 ThemeSettings 一致，属用户级偏好，不参与 .uplr 导入导出。
"""

from typing import Optional

from PySide6.QtCore import QObject, Signal

from ustplayer.core.i18n import SUPPORTED_LANGUAGES, system_locale


class LanguageSettings(QObject):
    """语言设置（跟随系统 / 手动指定）。"""

    language_changed = Signal(str)

    # 跟随系统模式的哨兵值
    MODE_SYSTEM = "system"

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._language = self.MODE_SYSTEM

    @property
    def language(self) -> str:
        """当前语言设置值（"system" 或具体语言代码）。"""
        return self._language

    @language.setter
    def language(self, v: str):
        v = v if v in SUPPORTED_LANGUAGES or v == self.MODE_SYSTEM else self.MODE_SYSTEM
        if self._language != v:
            self._language = v
            self.language_changed.emit(v)

    @property
    def effective_language(self) -> str:
        """实际生效的语言代码（system 模式解析为系统语言）。"""
        if self._language == self.MODE_SYSTEM:
            return system_locale()
        return self._language

    # ===================== 分组读写 =====================

    def read_from(self, config):
        """从 [LanguageSettings] 分组读取。"""
        if "LanguageSettings" not in config:
            return
        cs = config["LanguageSettings"]
        v = cs.get("language", self.MODE_SYSTEM)
        self._language = (
            v if v in SUPPORTED_LANGUAGES or v == self.MODE_SYSTEM else self.MODE_SYSTEM
        )

    def write_to(self, config):
        """写入 [LanguageSettings] 分组。"""
        config["LanguageSettings"] = {
            "language": self._language,
        }
