# lyric_page.py — "歌词" 导航页
"""LRC 歌词文件导入与显示控制。"""

import os
from typing import Optional

from PySide6.QtWidgets import QWidget, QHBoxLayout, QFileDialog
from PySide6.QtCore import Qt

from qfluentwidgets import (
    LineEdit, PushButton, CheckBox,
    BodyLabel,
)

from ustplayer.context import AppContext
from ustplayer.core.i18n import tr
from ustplayer.ui.section_card import ScrollPage, SectionCard


class LyricPage(ScrollPage):
    """歌词标签页 — LRC 文件路径 + 显示开关。"""

    # 需要随语言切换重译的控件（在 _setup_ui 中创建并保存引用）
    card_lyric: SectionCard
    lrc_lbl: BodyLabel
    cb_show_lyric: CheckBox
    lrc_edit: LineEdit
    select_btn: PushButton

    def __init__(self, ctx: AppContext, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._ctx = ctx
        self._s = ctx.settings
        self._setup_ui()
        self._connect_signals()

    def _setup_ui(self):
        layout = self.page_layout

        self.card_lyric = SectionCard(tr("歌词"))
        self.cb_show_lyric = CheckBox(tr("展示歌词"))
        self.card_lyric.addWidget(self.cb_show_lyric)

        lrc_row = QHBoxLayout()
        lrc_row.setSpacing(8)

        self.lrc_lbl = BodyLabel(tr("歌词文件（.lrc）:"))
        lrc_row.addWidget(self.lrc_lbl)
        self.lrc_edit = LineEdit()
        self.lrc_edit.setPlaceholderText(tr("请选择 .lrc 歌词文件..."))
        lrc_row.addWidget(self.lrc_edit, 1)
        self.select_btn = PushButton(tr("选择文件"))
        lrc_row.addWidget(self.select_btn)
        self.card_lyric.addLayout(lrc_row)
        layout.addWidget(self.card_lyric)

        layout.addStretch()

    def retranslate(self):
        """语言切换后重设全部静态文本。"""
        self.card_lyric.setTitle(tr("歌词"))
        self.cb_show_lyric.setText(tr("展示歌词"))
        self.lrc_lbl.setText(tr("歌词文件（.lrc）:"))
        self.lrc_edit.setPlaceholderText(tr("请选择 .lrc 歌词文件..."))
        self.select_btn.setText(tr("选择文件"))

    def _connect_signals(self):
        self.cb_show_lyric.setChecked(self._s.display.show_lyric)
        self.lrc_edit.setText(self._s.player.lrc_path)

        self.cb_show_lyric.checkStateChanged.connect(
            lambda v: setattr(self._s.display, "show_lyric", v == Qt.CheckState.Checked)
        )
        self.lrc_edit.textChanged.connect(lambda v: setattr(self._s.player, "lrc_path", v))
        self.select_btn.clicked.connect(self._on_select_lrc)

        # settings → UI（信号驱动实时同步）
        self._s.display.show_lyric_changed.connect(lambda v: self.cb_show_lyric.setChecked(v))
        self._s.player.lrc_path_changed.connect(lambda v: self.lrc_edit.setText(v))

    def _on_select_lrc(self):
        # 起始目录优先用当前歌词所在目录，其次上次打开的目录
        start_dir = (
            os.path.dirname(self._s.player.lrc_path)
            if self._s.player.lrc_path else self._s.last_open_dir
        )
        file_path, _ = QFileDialog.getOpenFileName(
            self, tr("选择LRC歌词文件"),
            start_dir,
            tr("LRC歌词文件 (*.lrc);;所有文件 (*.*)"),
        )
        if file_path:
            self.lrc_edit.setText(file_path)

    def sync_all_from_settings(self):
        """导入 uplr 后同步 UI（信号驱动的兜底）。"""
        self.cb_show_lyric.setChecked(self._s.display.show_lyric)
        self.lrc_edit.setText(self._s.player.lrc_path)
