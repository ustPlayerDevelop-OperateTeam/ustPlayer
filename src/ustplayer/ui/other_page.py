# other_page.py — "其他" 导航页
"""版权信息、外部工具、使用协议入口、主题与强调色设置。"""

import subprocess
import webbrowser
from typing import Optional

from PySide6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout
from PySide6.QtCore import Qt
from PySide6.QtGui import QColor

from qfluentwidgets import (
    PushButton, BodyLabel, StrongBodyLabel, HorizontalSeparator,
    ComboBox, ColorPickerButton,
    InfoBar, InfoBarPosition,
)

from ustplayer.context import AppContext
from ustplayer.core.contracts import APP_COPYRIGHT


class OtherPage(QWidget):
    """其他标签页 — 关于软件 / 工具 / 协议 / 主题与强调色。"""

    def __init__(self, ctx: AppContext, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._ctx = ctx
        self._s = ctx.settings
        self._setup_ui()
        self._connect_signals()

    # ===================== UI 构建 =====================

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.setSpacing(12)

        layout.addWidget(StrongBodyLabel("/ 关于软件"))

        # 版权信息（可点击）
        copyright_lbl = BodyLabel(APP_COPYRIGHT)
        copyright_lbl.setStyleSheet("color: #0066CC;")
        copyright_lbl.setCursor(Qt.PointingHandCursor)
        copyright_lbl.mousePressEvent = lambda e: self._open_url(
            "https://space.bilibili.com/661930756"
        )
        layout.addWidget(copyright_lbl)

        layout.addWidget(HorizontalSeparator())

        # 外部工具与纠错
        layout.addWidget(StrongBodyLabel("/ 外部工具与纠错"))

        tool_row = QHBoxLayout()
        tool_row.setSpacing(12)

        uf_btn = PushButton("UtaFormatix")
        uf_btn.clicked.connect(lambda: self._open_url("https://utaformatix.tk/"))
        tool_row.addWidget(uf_btn)

        er_btn = PushButton("ERcodes纠错")
        er_btn.clicked.connect(self._open_ercode)
        tool_row.addWidget(er_btn)
        tool_row.addStretch()
        layout.addLayout(tool_row)

        layout.addWidget(HorizontalSeparator())

        # ---- 主题设置 ----
        layout.addWidget(StrongBodyLabel("/ 主题"))

        theme_row = QHBoxLayout()
        theme_row.setSpacing(8)
        theme_row.addWidget(BodyLabel("应用主题:"))
        self.theme_combo = ComboBox()
        self.theme_combo.addItems(["跟随系统", "亮色", "暗色"])
        theme_row.addWidget(self.theme_combo)
        theme_row.addStretch()
        layout.addLayout(theme_row)

        # ---- 强调色设置 ----
        accent_mode_row = QHBoxLayout()
        accent_mode_row.setSpacing(8)
        accent_mode_row.addWidget(BodyLabel("强调色:"))
        self.accent_color_mode_combo = ComboBox()
        self.accent_color_mode_combo.addItems(["跟随系统", "自定义"])
        accent_mode_row.addWidget(self.accent_color_mode_combo)
        accent_mode_row.addStretch()
        layout.addLayout(accent_mode_row)

        accent_custom_row = QHBoxLayout()
        accent_custom_row.setSpacing(8)
        accent_custom_row.addWidget(BodyLabel("自定义颜色:"))
        self.accent_color_picker = ColorPickerButton(
            QColor(self._s.custom_accent_color), "选择强调色", self
        )
        accent_custom_row.addWidget(self.accent_color_picker)
        accent_custom_row.addStretch()
        layout.addLayout(accent_custom_row)

        layout.addWidget(HorizontalSeparator())

        # 协议与许可
        layout.addWidget(StrongBodyLabel("/ 协议与许可"))

        lic_row = QHBoxLayout()
        lic_row.setSpacing(12)

        terms_btn = PushButton("开源协议")
        terms_btn.clicked.connect(self._open_terms)
        lic_row.addWidget(terms_btn)

        gh_btn = PushButton("GitHub仓库")
        gh_btn.clicked.connect(
            lambda: self._open_url("https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer")
        )
        lic_row.addWidget(gh_btn)
        lic_row.addStretch()
        layout.addLayout(lic_row)

        layout.addWidget(HorizontalSeparator())

        # 彩蛋
        easter = BodyLabel("你知道吗：alpha版本在提交至托管时曾被错误地命名为ustPlyaer。orz")
        easter.setWordWrap(True)
        layout.addWidget(easter)

        layout.addStretch()

    # ===================== 信号绑定 =====================

    def _connect_signals(self):
        s = self._s

        # 主题下拉框
        self.theme_combo.setCurrentText(self._theme_combo_text(s.theme_mode))
        self.theme_combo.currentTextChanged.connect(self._on_theme_combo_changed)
        s.theme_mode_changed.connect(self._on_settings_theme_mode_changed)

        # 强调色模式下拉框
        self.accent_color_mode_combo.setCurrentText(
            self._accent_mode_text(s.accent_color_mode)
        )
        self.accent_color_mode_combo.currentTextChanged.connect(
            self._on_accent_color_mode_combo_changed
        )
        s.accent_color_mode_changed.connect(self._on_settings_accent_mode_changed)

        # 自定义颜色选择器
        self.accent_color_picker.setColor(QColor(s.custom_accent_color))
        self.accent_color_picker.colorChanged.connect(self._on_accent_color_pick)
        s.custom_accent_color_changed.connect(self._on_settings_accent_color_changed)

        # 初始时根据模式显示/隐藏自定义颜色选择器
        self._update_accent_custom_visible(s.accent_color_mode)

    # ===================== 业务逻辑 =====================

    def _on_theme_combo_changed(self, text: str):
        """主题下拉框变化 → 更新 settings.theme_mode。"""
        mode = self._theme_combo_mode(text)
        setattr(self._s, "theme_mode", mode)

    def _on_accent_color_mode_combo_changed(self, text: str):
        """强调色模式变化 → 更新 settings。"""
        mode = self._accent_mode_value(text)
        setattr(self._s, "accent_color_mode", mode)
        self._update_accent_custom_visible(mode)

    def _on_accent_color_pick(self, color: QColor):
        """自定义颜色选择 → 更新 settings。"""
        setattr(self._s, "custom_accent_color", color.name())

    def _update_accent_custom_visible(self, mode: str):
        """自定义模式下显示颜色选择器。"""
        self.accent_color_picker.setVisible(mode == "custom")

    def _on_settings_theme_mode_changed(self, v: str):
        """settings 端主题模式变化 → 同步下拉框（避免 lambda GC 问题）。"""
        self.theme_combo.blockSignals(True)
        self.theme_combo.setCurrentText(self._theme_combo_text(v))
        self.theme_combo.blockSignals(False)

    def _on_settings_accent_mode_changed(self, v: str):
        """settings 端强调色模式变化 → 同步下拉框。"""
        self.accent_color_mode_combo.blockSignals(True)
        self.accent_color_mode_combo.setCurrentText(self._accent_mode_text(v))
        self.accent_color_mode_combo.blockSignals(False)
        self._update_accent_custom_visible(v)

    def _on_settings_accent_color_changed(self, v: str):
        """settings 端自定义强调色变化 → 同步取色器。"""
        self.accent_color_picker.blockSignals(True)
        self.accent_color_picker.setColor(QColor(v))
        self.accent_color_picker.blockSignals(False)

    # ===================== 辅助方法 =====================

    @staticmethod
    def _theme_combo_text(mode: str) -> str:
        return {"auto": "跟随系统", "light": "亮色", "dark": "暗色"}.get(mode, "跟随系统")

    @staticmethod
    def _theme_combo_mode(text: str) -> str:
        return {"跟随系统": "auto", "亮色": "light", "暗色": "dark"}.get(text, "auto")

    @staticmethod
    def _accent_mode_text(mode: str) -> str:
        return {"auto": "跟随系统", "custom": "自定义"}.get(mode, "跟随系统")

    @staticmethod
    def _accent_mode_value(text: str) -> str:
        return {"跟随系统": "auto", "自定义": "custom"}.get(text, "auto")

    # ===================== 工具方法 =====================

    def _open_url(self, url: str):
        try:
            webbrowser.open(url, new=2)
        except Exception as e:
            InfoBar.error("ERcode003", f"打开网页失败：{e}", 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    @staticmethod
    def _open_with_notepad(path: str):
        """用记事本打开文本文件（无需 shell 中转，避免路径特殊字符被二次解析）。"""
        return subprocess.Popen(
            ["notepad.exe", path],
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP,
        )

    def _open_ercode(self):
        try:
            self._open_with_notepad(self._s.ercode_file_path)
        except Exception as e:
            InfoBar.error("ERcode008", f"打开ERcode.txt失败：{e}", 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    def _open_terms(self):
        try:
            self._open_with_notepad(self._s.terms_file_path)
        except Exception as e:
            InfoBar.error("ERcode009", f"打开LICENSE失败：{e}", 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    # ===================== 同步 =====================

    def _sync_ui_from_settings(self):
        """从 settings 同步所有 UI 控件。"""
        s = self._s
        self.theme_combo.setCurrentText(self._theme_combo_text(s.theme_mode))
        self.accent_color_mode_combo.setCurrentText(
            self._accent_mode_text(s.accent_color_mode)
        )
        self.accent_color_picker.setColor(QColor(s.custom_accent_color))
        self._update_accent_custom_visible(s.accent_color_mode)

    def sync_all_from_settings(self):
        """导入 uplr 或导航切换后同步 UI（信号驱动的兜底）。"""
        self._sync_ui_from_settings()
