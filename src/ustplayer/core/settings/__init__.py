# core/settings 包 — 设置子域
"""按领域拆分的设置子域，每个类负责自己的属性 + 信号 + ini 段读写。

由 core/settings_manager.py 的 SettingsManager 组装并对外暴露，
UI 通过 ctx.settings.<子域>.<属性> 访问。
"""

from ustplayer.core.settings.color import ColorSettings
from ustplayer.core.settings.display import DisplaySettings
from ustplayer.core.settings.file import FileSettings
from ustplayer.core.settings.player import PlayerSettings
from ustplayer.core.settings.project import ProjectSettings
from ustplayer.core.settings.theme import ThemeSettings

__all__ = [
    "ColorSettings",
    "DisplaySettings",
    "FileSettings",
    "PlayerSettings",
    "ProjectSettings",
    "ThemeSettings",
]
