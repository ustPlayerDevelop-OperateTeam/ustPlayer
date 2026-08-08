# file_page.py — "文件" 导航页
"""UST 文件选择、编码切换和内容预览。"""

import os
from typing import Optional

from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QFileDialog,
)
from PySide6.QtCore import Qt

from qfluentwidgets import (
    LineEdit, PushButton, ComboBox, TextEdit, CheckBox,
    BodyLabel, HorizontalSeparator, InfoBar, InfoBarPosition,
)

from ustplayer.core.settings_manager import SettingsManager


class FilePage(QWidget):
    """文件标签页 — UST 路径选择 + 编码 + 内容预览。"""

    def __init__(self, settings: SettingsManager, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._s = settings
        self._setup_ui()
        self._connect_signals()

    # ===================== UI 构建 =====================

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.setSpacing(12)

        # ---- UST 文件选择 ----
        ust_row = QHBoxLayout()
        ust_row.setSpacing(8)

        ust_row.addWidget(BodyLabel("ust:"))
        self.ust_edit = LineEdit()
        self.ust_edit.setPlaceholderText("请选择或拖入 .ust 文件路径...")
        ust_row.addWidget(self.ust_edit, 1)
        self.select_btn = PushButton("选择ust文件")
        ust_row.addWidget(self.select_btn)
        layout.addLayout(ust_row)

        # 音高线勾选框
        self.cb_curve = CheckBox("显示音高线变化")
        layout.addWidget(self.cb_curve)

        # ---- 编码选择 ----
        enc_row = QHBoxLayout()
        enc_row.setSpacing(8)

        enc_row.addWidget(BodyLabel("编码方式:"))
        self.encoding_combo = ComboBox()
        self.encoding_combo.addItems(["UTF-8", "GBK", "Shift-JIS"])
        self.encoding_combo.setCurrentText(self._s.encoding)
        enc_row.addWidget(self.encoding_combo)
        enc_row.addStretch()
        self.encoding_check_btn = PushButton("编码检查")
        enc_row.addWidget(self.encoding_check_btn)
        layout.addLayout(enc_row)

        # ---- 内容预览 ----
        self.preview_edit = TextEdit()
        self.preview_edit.setReadOnly(True)
        self.preview_edit.setPlaceholderText("选择 UST 文件后在此预览...")
        layout.addWidget(self.preview_edit, 1)

    # ===================== 信号绑定 =====================

    def _connect_signals(self):
        self.ust_edit.setText(self._s.ustx_path)
        self.cb_curve.setChecked(self._s.curve_show)

        self.ust_edit.textChanged.connect(lambda v: setattr(self._s, "ustx_path", v))
        self.select_btn.clicked.connect(self._on_select_ust)
        self.encoding_combo.currentTextChanged.connect(self._on_encoding_change)
        self.encoding_check_btn.clicked.connect(self._on_encoding_check)
        self.cb_curve.checkStateChanged.connect(
            lambda v: setattr(self._s, "curve_show", v == Qt.CheckState.Checked)
        )

    # ===================== 业务逻辑 =====================

    def _on_select_ust(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self, "选择ust文件",
            os.path.dirname(self._s.ustx_path) if self._s.ustx_path else "",
            "UST文件 (*.ust);;所有文件 (*.*)",
        )
        if file_path:
            self.ust_edit.setText(file_path)

    def _on_encoding_change(self, encoding: str):
        self._s.encoding = encoding

    def _on_encoding_check(self):
        """手动触发编码检查，使用当前编码重新读取并预览文件。"""
        path = self._s.ustx_path.strip()
        if not path:
            InfoBar.warning("提示", "请先选择 UST 文件", 3000,
                           parent=self.window(), position=InfoBarPosition.TOP_RIGHT)
            return
        if not os.path.exists(path):
            InfoBar.error("ERcode001", "UST 文件不存在", 5000,
                         parent=self.window(), position=InfoBarPosition.TOP_RIGHT)
            return
        self._preview(path)

    def _preview(self, file_path: str):
        try:
            with open(file_path, "r", encoding=self._s.encoding, errors="replace") as f:
                content = f.read()
            self.preview_edit.setPlainText(content)
        except Exception as e:
            InfoBar.error("ERcode002", f"读取文件失败：{e}", 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    def refresh_preview(self):
        """供 main.py 调用，加载 uplr 后刷新预览。"""
        path = self._s.ustx_path.strip()
        if path and os.path.exists(path):
            self._preview(path)

    def sync_all_from_settings(self):
        """导入 uplr 后同步 UI。"""
        self.ust_edit.setText(self._s.ustx_path)
        self.encoding_combo.setCurrentText(self._s.encoding)
        self.cb_curve.setChecked(self._s.curve_show)
        self.refresh_preview()
