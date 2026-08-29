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
    BodyLabel, InfoBar, InfoBarPosition,
)

from ustplayer.context import AppContext
from ustplayer.core.i18n import tr


class FilePage(QWidget):
    """文件标签页 — UST 路径选择 + 编码 + 内容预览。"""

    # 需要随语言切换重译的控件（在 _setup_ui 中创建并保存引用）
    ust_lbl: BodyLabel
    enc_lbl: BodyLabel
    select_btn: PushButton
    encoding_check_btn: PushButton
    cb_curve: CheckBox
    ust_edit: LineEdit
    encoding_combo: ComboBox
    preview_edit: TextEdit

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

        # UST 文件选择
        ust_row = QHBoxLayout()
        ust_row.setSpacing(8)

        self.ust_lbl = BodyLabel(tr("ust:"))
        ust_row.addWidget(self.ust_lbl)
        self.ust_edit = LineEdit()
        self.ust_edit.setPlaceholderText(tr("请选择或拖入 .ust 文件路径..."))
        ust_row.addWidget(self.ust_edit, 1)
        self.select_btn = PushButton(tr("选择ust文件"))
        ust_row.addWidget(self.select_btn)
        layout.addLayout(ust_row)

        # 音高线勾选框
        self.cb_curve = CheckBox(tr("显示音高线变化"))
        layout.addWidget(self.cb_curve)

        # 编码选择
        enc_row = QHBoxLayout()
        enc_row.setSpacing(8)

        self.enc_lbl = BodyLabel(tr("编码方式:"))
        enc_row.addWidget(self.enc_lbl)
        self.encoding_combo = ComboBox()
        self.encoding_combo.addItems(["UTF-8", "GBK", "Shift-JIS"])
        self.encoding_combo.setCurrentText(self._s.file.encoding)
        enc_row.addWidget(self.encoding_combo)
        enc_row.addStretch()
        self.encoding_check_btn = PushButton(tr("编码检查"))
        enc_row.addWidget(self.encoding_check_btn)
        layout.addLayout(enc_row)

        # 内容预览
        self.preview_edit = TextEdit()
        self.preview_edit.setReadOnly(True)
        self.preview_edit.setPlaceholderText(tr("选择 UST 文件后在此预览..."))
        layout.addWidget(self.preview_edit, 1)

    # ===================== 重译（语言切换时调用） =====================

    def retranslate(self):
        """语言切换后重设全部静态文本。"""
        self.ust_lbl.setText(tr("ust:"))
        self.ust_edit.setPlaceholderText(tr("请选择或拖入 .ust 文件路径..."))
        self.select_btn.setText(tr("选择ust文件"))
        self.cb_curve.setText(tr("显示音高线变化"))
        self.enc_lbl.setText(tr("编码方式:"))
        self.encoding_check_btn.setText(tr("编码检查"))
        self.preview_edit.setPlaceholderText(tr("选择 UST 文件后在此预览..."))

    # ===================== 信号绑定 =====================

    def _connect_signals(self):
        self.ust_edit.setText(self._s.file.ust_path)
        self.cb_curve.setChecked(self._s.file.curve_show)

        self.ust_edit.textChanged.connect(lambda v: setattr(self._s.file, "ust_path", v))
        self.select_btn.clicked.connect(self._on_select_ust)
        self.encoding_combo.currentTextChanged.connect(self._on_encoding_change)
        self.encoding_check_btn.clicked.connect(self._on_encoding_check)
        self.cb_curve.checkStateChanged.connect(
            lambda v: setattr(self._s.file, "curve_show", v == Qt.CheckState.Checked)
        )

        # settings → UI（信号驱动实时同步）
        self._s.file.ust_path_changed.connect(self._on_ust_path_changed)
        self._s.file.encoding_changed.connect(
            lambda v: self.encoding_combo.setCurrentText(v)
        )
        self._s.file.curve_show_changed.connect(lambda v: self.cb_curve.setChecked(v))

    # ===================== 业务逻辑 =====================

    def _on_ust_path_changed(self, path: str):
        """设置端 UST 路径变化 → 更新输入框并自动刷新预览。"""
        self.ust_edit.setText(path)
        self.refresh_preview()

    def _on_select_ust(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self, tr("选择ust文件"),
            os.path.dirname(self._s.file.ust_path) if self._s.file.ust_path else "",
            tr("UST文件 (*.ust);;所有文件 (*.*)"),
        )
        if file_path:
            self.ust_edit.setText(file_path)

    def _on_encoding_change(self, encoding: str):
        self._s.file.encoding = encoding
        self.refresh_preview()

    def _on_encoding_check(self):
        """手动触发编码检查：以当前编码严格模式试读，验证编码是否正确。"""
        path = self._s.file.ust_path.strip()
        if not path:
            InfoBar.warning(tr("提示"), tr("请先选择 UST 文件"), duration=3000,
                            parent=self.window(), position=InfoBarPosition.TOP_RIGHT)
            return
        if not os.path.exists(path):
            InfoBar.error("ERcode001", tr("UST 文件不存在"), duration=5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)
            return
        self._preview(path, strict=True)

    def _preview(self, file_path: str, strict: bool = False):
        """读取并预览 UST 内容。

        strict=False: 以 errors="replace" 宽松显示（供常规预览，坏字节显示为替换符）；
        strict=True: 以 errors="strict" 严格验证编码，失败时提示切换编码。
        """
        encoding = self._s.file.encoding
        try:
            with open(file_path, "r", encoding=encoding,
                      errors="strict" if strict else "replace") as f:
                content = f.read()
            self.preview_edit.setPlainText(content)
            if strict:
                InfoBar.success(
                    tr("编码正确"), tr("使用 {0} 可以正常读取该文件").format(encoding), duration=3000,
                    parent=self.window(), position=InfoBarPosition.TOP_RIGHT,
                )
        except UnicodeDecodeError:
            # 仅严格模式会抛出：宽松模式用 replace 替换坏字节，不中断预览
            self.preview_edit.setPlainText("")
            if strict:
                InfoBar.error(
                    "ERcode004",
                    tr("当前编码 {0} 无法读取该文件，请尝试其他编码").format(encoding),
                    duration=5000, parent=self.window(), position=InfoBarPosition.TOP_RIGHT,
                )
        except Exception as e:
            self.preview_edit.clear()
            InfoBar.error("ERcode002", tr("读取文件失败：{0}").format(e), duration=5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    def refresh_preview(self):
        """刷新预览（路径变化或导入 uplr 后自动调用）。"""
        path = self._s.file.ust_path.strip()
        if path and os.path.exists(path):
            self._preview(path)
        else:
            # 路径清空或文件已删除时不能继续展示旧内容，避免误导用户
            self.preview_edit.clear()

    def sync_all_from_settings(self):
        """导入 uplr 后同步 UI（信号驱动的兜底）。"""
        self.ust_edit.setText(self._s.file.ust_path)
        self.encoding_combo.setCurrentText(self._s.file.encoding)
        self.cb_curve.setChecked(self._s.file.curve_show)
        self.refresh_preview()
