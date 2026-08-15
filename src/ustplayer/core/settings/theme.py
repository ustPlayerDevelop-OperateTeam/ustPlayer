# settings/theme.py — 主题设置子域
"""应用主题、强调色与窗口背景效果（用户级 UI 偏好，不参与 uplr 导入导出），
对应 Settings.json 的 [ThemeSettings] 分组。"""

from typing import Optional

from PySide6.QtCore import QObject, Signal

from ustplayer.core.contracts import is_valid_hex_color


class ThemeSettings(QObject):
    """主题设置（亮/暗/自动 + 强调色 + 窗口背景效果）。"""

    theme_mode_changed = Signal(str)
    accent_color_mode_changed = Signal(str)
    custom_accent_color_changed = Signal(str)
    window_effect_changed = Signal(str)

    _THEME_MODES = ("auto", "light", "dark")
    _ACCENT_MODES = ("auto", "custom")
    _WINDOW_EFFECTS = ("none", "mica", "acrylic")

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._theme_mode = "auto"  # auto=跟随系统, light=亮色, dark=暗色
        self._accent_color_mode = "auto"  # auto=跟随系统强调色, custom=自定义
        self._custom_accent_color = "#009faa"  # qfluentwidgets 默认主题色
        # 窗口背景效果：none=无, mica=Win11 Mica, acrylic=亚克力模糊。
        # 默认 mica 与 qfluentwidgets FluentWindow 的默认行为一致（Win11 默认开启）。
        self._window_effect = "mica"

    @property
    def theme_mode(self) -> str:
        return self._theme_mode

    @theme_mode.setter
    def theme_mode(self, v: str):
        if v not in self._THEME_MODES:
            v = "auto"
        if self._theme_mode != v:
            self._theme_mode = v
            self.theme_mode_changed.emit(v)

    @property
    def accent_color_mode(self) -> str:
        return self._accent_color_mode

    @accent_color_mode.setter
    def accent_color_mode(self, v: str):
        if v not in self._ACCENT_MODES:
            v = "auto"
        if self._accent_color_mode != v:
            self._accent_color_mode = v
            self.accent_color_mode_changed.emit(v)

    @property
    def custom_accent_color(self) -> str:
        return self._custom_accent_color

    @custom_accent_color.setter
    def custom_accent_color(self, v: str):
        if self._custom_accent_color != v:
            self._custom_accent_color = v
            self.custom_accent_color_changed.emit(v)

    @property
    def window_effect(self) -> str:
        return self._window_effect

    @window_effect.setter
    def window_effect(self, v: str):
        if v not in self._WINDOW_EFFECTS:
            v = "mica"
        if self._window_effect != v:
            self._window_effect = v
            self.window_effect_changed.emit(v)

    # ===================== 分组读写 =====================

    def read_from(self, config):
        """从 [ThemeSettings] 分组读取。"""
        if "ThemeSettings" not in config:
            return
        cs = config["ThemeSettings"]
        mode = cs.get("theme_mode", "auto")
        self._theme_mode = mode if mode in self._THEME_MODES else "auto"
        amode = cs.get("accent_color_mode", "auto")
        self._accent_color_mode = amode if amode in self._ACCENT_MODES else "auto"
        raw = cs.get("custom_accent_color", "#009faa")
        self._custom_accent_color = raw if is_valid_hex_color(raw) else "#009faa"
        effect = cs.get("window_effect", "mica")
        self._window_effect = effect if effect in self._WINDOW_EFFECTS else "mica"

    def write_to(self, config):
        """写入 [ThemeSettings] 分组。"""
        config["ThemeSettings"] = {
            "theme_mode": self._theme_mode,
            "accent_color_mode": self._accent_color_mode,
            "custom_accent_color": self._custom_accent_color,
            "window_effect": self._window_effect,
        }
