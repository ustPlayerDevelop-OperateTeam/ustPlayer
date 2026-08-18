# settings/language.py — 语言设置子域
"""存 Settings.json 的 [LanguageSettings]。"""

from typing import Optional

from PySide6.QtCore import QObject, Signal

from ustplayer.core.i18n import SUPPORTED_LANGUAGES, system_locale


class LanguageSettings(QObject):
    language_changed = Signal(str)
    MODE_SYSTEM = "system"

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._language = self.MODE_SYSTEM

    @property
    def language(self) -> str:
        return self._language

    @language.setter
    def language(self, v: str):
        v = v if v in SUPPORTED_LANGUAGES or v == self.MODE_SYSTEM else self.MODE_SYSTEM
        if self._language != v:
            self._language = v
            self.language_changed.emit(v)

    @property
    def effective_language(self) -> str:
        if self._language == self.MODE_SYSTEM:
            return system_locale()
        return self._language

    def read_from(self, config):
        if "LanguageSettings" not in config:
            return
        v = config["LanguageSettings"].get("language", self.MODE_SYSTEM)
        self._language = (
            v if v in SUPPORTED_LANGUAGES or v == self.MODE_SYSTEM else self.MODE_SYSTEM
        )

    def write_to(self, config):
        config["LanguageSettings"] = {"language": self._language}
