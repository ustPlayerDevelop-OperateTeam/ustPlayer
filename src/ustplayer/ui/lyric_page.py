# lyric_page.py — "歌词" 导航页
"""LRC 歌词文件导入与显示控制。"""

from typing import Optional

from PySide6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QFileDialog
from PySide6.QtCore import Qt

from qfluentwidgets import (
    LineEdit, PushButton, CheckBox,
    BodyLabel, StrongBodyLabel, HorizontalSeparator,
)

from ustplayer.context import AppContext


class LyricPage(QWidget):
    """歌词标签页 — LRC 文件路径 + 显示开关。"""

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

        layout.addWidget(StrongBodyLabel("/ 歌词"))

        # 显示歌词复选框
        self.cb_show_lyric = CheckBox("展示歌词")
        layout.addWidget(self.cb_show_lyric)

        layout.addWidget(HorizontalSeparator())

        # LRC 文件导入
        lrc_row = QHBoxLayout()
        lrc_row.setSpacing(8)

        lrc_row.addWidget(BodyLabel("歌词文件（.lrc）:"))
        self.lrc_edit = LineEdit()
        self.lrc_edit.setPlaceholderText("请选择 .lrc 歌词文件...")
        lrc_row.addWidget(self.lrc_edit, 1)
        self.select_btn = PushButton("选择文件")
        lrc_row.addWidget(self.select_btn)
        layout.addLayout(lrc_row)

        layout.addStretch()

    # ===================== 信号绑定 =====================

    def _connect_signals(self):
        self.cb_show_lyric.setChecked(self._s.show_lyric)
        self.lrc_edit.setText(self._s.lrc_path)

        self.cb_show_lyric.checkStateChanged.connect(
            lambda v: setattr(self._s, "show_lyric", v == Qt.CheckState.Checked)
        )
        self.lrc_edit.textChanged.connect(lambda v: setattr(self._s, "lrc_path", v))
        self.select_btn.clicked.connect(self._on_select_lrc)

        # settings → UI（信号驱动实时同步）
        self._s.show_lyric_changed.connect(lambda v: self.cb_show_lyric.setChecked(v))
        self._s.lrc_path_changed.connect(lambda v: self.lrc_edit.setText(v))

    # ===================== 业务逻辑 =====================

    def _on_select_lrc(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self, "选择LRC歌词文件",
            "",
            "LRC歌词文件 (*.lrc);;所有文件 (*.*)",
        )
        if file_path:
            self.lrc_edit.setText(file_path)

    def sync_all_from_settings(self):
        """导入 uplr 后同步 UI（信号驱动的兜底）。"""
        self.cb_show_lyric.setChecked(self._s.show_lyric)
        self.lrc_edit.setText(self._s.lrc_path)
