# settings_manager.py — 应用设置门面（组装各设置子域）
"""SettingsManager 只负责组装与编排，不再持有具体配置属性：

- 设置子域（属性 + 信号 + 分组读写）分布在 core/settings/ 包：
  ProjectSettings / FileSettings / DisplaySettings / ColorSettings / PlayerSettings / ThemeSettings；
- Settings.json 文件存取（含旧版 Settings.ini 自动迁移）由 core/settings_store.py（SettingsStore）承担；
- .uplr 导入/导出由 core/uplr_io.py（UplrProjectIO）承担。

UI 通过 ctx.settings.<子域>.<属性> 访问，如 ctx.settings.display.show_bpm。
"""

import os
from typing import Dict, Optional

from PySide6.QtCore import QObject

from ustplayer.core.contracts import (
    PlayerLaunchParams,
    PlayerStyle,
    ProjectInfo,
    ShowConfig,
    UstInfo,
    resolve_program_root,
)
from ustplayer.core.log import logger
from ustplayer.core.settings import (
    ColorSettings,
    DisplaySettings,
    FileSettings,
    LanguageSettings,
    PlayerSettings,
    ProjectSettings,
    ThemeSettings,
)
from ustplayer.core.settings_store import SettingsStore


class SettingsManager(QObject):
    """应用设置门面 — 组装六个设置子域并编排 ini 读写/校验/参数组装。"""

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)

        self.program_root = resolve_program_root()
        self._store = SettingsStore()
        self.settings_path = self._store.settings_path

        self.terms_file_path = os.path.join(self.program_root, "LICENSE")
        self.ercode_file_path = os.path.join(self.program_root, "ERcode.txt")

        default_desktop = os.path.join(os.path.expanduser("~"), "Desktop")
        self.last_open_dir = default_desktop
        self.last_export_dir = default_desktop

        self.project = ProjectSettings(self)
        self.file = FileSettings(self)
        self.display = DisplaySettings(self)
        self.color = ColorSettings(self)
        self.player = PlayerSettings(self)
        self.theme = ThemeSettings(self)
        self.language = LanguageSettings(self)

        self._config: Dict[str, Dict[str, str]] = {}
        self.read_settings()

    # ===================== Settings.json 读写 =====================

    def read_settings(self):
        """读取设置并恢复全部配置，路径失效时回退桌面。"""
        default_desktop = os.path.join(os.path.expanduser("~"), "Desktop")
        try:
            self._config = self._store.load()
            if not self._config:
                self.last_open_dir = default_desktop
                self.last_export_dir = default_desktop
                return

            if "PathSettings" in self._config:
                self.last_open_dir = self._config["PathSettings"].get(
                    "last_open_dir", default_desktop
                )
                self.last_export_dir = self._config["PathSettings"].get(
                    "last_export_dir", default_desktop
                )
                if not os.path.isdir(self.last_open_dir):
                    self.last_open_dir = default_desktop
                if not os.path.isdir(self.last_export_dir):
                    self.last_export_dir = default_desktop

            self.project.read_from(self._config)
            self.file.read_from(self._config)
            self.display.read_from(self._config)
            self.color.read_from(self._config)
            self.player.read_from(self._config)
            self.theme.read_from(self._config)
            self.language.read_from(self._config)

            self.sanitize()
            self.write_settings()
        except Exception as e:
            self.last_open_dir = default_desktop
            self.last_export_dir = default_desktop
            logger.exception(f"读取配置文件失败：{e}")

    def write_settings(self):
        """将所有设置写入配置文件，退出时保存以便重启恢复。"""
        try:
            self._config["PathSettings"] = {
                "last_open_dir": self.last_open_dir,
                "last_export_dir": self.last_export_dir,
            }
            self.project.write_to(self._config)
            self.file.write_to(self._config)
            self.display.write_to(self._config)
            self.color.write_to(self._config)
            self.player.write_to(self._config)
            self.theme.write_to(self._config)
            self.language.write_to(self._config)

            self._store.save(self._config)
        except Exception as e:
            logger.exception(f"写入配置文件失败：{e}")

    # ===================== 校验 =====================

    def sanitize(self):
        """校验各子域的枚举/颜色值，非法时回退默认。"""
        self.file.validate()
        self.color.validate()
        self.player.validate()

    # ===================== 构建播放器启动参数 =====================

    def build_ust_info(self, core_ust_info: UstInfo) -> PlayerLaunchParams:
        """将解析结果与当前设置组装为播放器启动参数（统一接口）。"""
        return PlayerLaunchParams(
            ust=core_ust_info,
            show=ShowConfig(
                bpm=self.display.show_bpm,
                play_time=self.display.show_play_time,
                song_name=self.display.show_song_name,
                song_author=self.display.show_song_author,
                ust_author=self.display.show_ust_author,
                lyric=self.display.show_lyric,
                curve_show=self.file.curve_show,
            ),
            project=ProjectInfo(
                project_name=self.project.project_name,
                song_name=self.project.song_name,
                song_author=self.project.song_author,
                ust_author=self.project.ust_author,
            ),
            style=PlayerStyle(
                bg_color=self.color.bg_color,
                note_color=self.color.note_color,
                lyric_color=self.color.lyric_color,
                lyric_text_color=self.color.lyric_text_color,
                other_text_color=self.color.other_text_color,
                lyric_pos=self.player.lyric_pos,
                fullscreen=self.display.fullscreen,
                lrc_path=self.player.lrc_path,
                music_path=self.project.music_path,
                silent_display=self.player.silent_display,
                silent_custom_text=self.player.silent_custom_text,
                end_display=self.player.end_display,
                end_custom_text=self.player.end_custom_text,
                pitch_placeholder=self.player.pitch_placeholder,
                pitch_custom_text=self.player.pitch_custom_text,
                pitch_curve_color=self.color.pitch_curve_color,
            ),
        )
