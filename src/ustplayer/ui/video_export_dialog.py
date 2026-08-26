# video_export_dialog.py — 视频导出对话框（Fluent 风格弹窗）
"""选择输出路径 / 分辨率 / 帧率，后台线程调用 uPlRender 渲染视频并显示进度。

基于 qfluentwidgets.MessageBoxBase：居中圆角卡片 + 遮罩幕 + 随主题的 Fluent 控件。
"""

import os
from typing import Optional

from PySide6.QtCore import QObject, QThread, Signal
from PySide6.QtWidgets import QFileDialog, QHBoxLayout

from qfluentwidgets import (
    BodyLabel,
    ComboBox,
    InfoBar,
    InfoBarPosition,
    LineEdit,
    MessageBoxBase,
    ProgressBar,
    PushButton,
    SpinBox,
    SubtitleLabel,
    SwitchButton,
)

from ustplayer.context import AppContext
from ustplayer.core.i18n import tr


# ---------- 后台渲染工作线程 ----------

class VideoExportWorker(QObject):
    progress = Signal(int)        # 千分比 0..1000
    finished = Signal(str)        # .uprd 工程文件路径
    failed = Signal(str)          # 错误消息

    def __init__(self, ctx: AppContext, output_path: str, width: int, height: int,
                 fps: int, mux_audio: bool):
        super().__init__()
        self._ctx = ctx
        self._output_path = output_path
        self._width = width
        self._height = height
        self._fps = fps
        self._mux_audio = mux_audio
        self._cancel = False

    def cancel(self):
        self._cancel = True

    @property
    def _cancel_check(self):
        return lambda: self._cancel

    def _on_progress(self, value: int):
        self.progress.emit(value)

    def run(self):
        try:
            uprd_path = self._ctx.video_exporter.render(
                self._output_path,
                self._width,
                self._height,
                self._fps,
                self._mux_audio,
                progress_cb=self._on_progress,
                cancel_check=self._cancel_check,
            )
        except Exception as e:  # noqa: BLE001 —— 面向用户逐层展示错误，不中断 UI
            self.failed.emit(str(e))
            return
        self.finished.emit(uprd_path)


# ---------- 分辨率预设 ----------

_RESOLUTION_PRESETS = [
    (1920, 1080, "1920 × 1080"),
    (1280, 720, "1280 × 720"),
    (2560, 1440, "2560 × 1440"),
    (3840, 2160, "3840 × 2160"),
]


class VideoExportDialog(MessageBoxBase):
    """Fluent 风格的「导出视频」弹窗（模态，带遮罩幕）。"""

    def __init__(self, ctx: AppContext, parent=None):
        super().__init__(parent)
        self._ctx = ctx
        self._settings = ctx.settings
        self._thread: Optional[QThread] = None
        self._worker: Optional[VideoExportWorker] = None

        self._build_form()

        # 复用基类的底部按钮组：确认 = 开始导出，取消 = 取消
        self.yesButton.setText(tr("开始导出"))
        self.cancelButton.setText(tr("取消"))
        try:
            self.yesButton.clicked.disconnect()  # 解除基类「校验后立即 accept」的默认处理
        except (RuntimeError, TypeError):
            pass
        self.yesButton.clicked.connect(self._on_export)

        # 不允许点遮罩幕关闭（导出期间尤其防止误关）
        self.setClosableOnMaskClicked(False)
        self.cancelButton.clicked.connect(self._on_cancel_clicked)

        self._sync_default_output()
        self._reset_controls(exporting=False)

    # ---------- 表单 ----------

    def _build_form(self):
        self.widget.setMaximumWidth(480)
        self.viewLayout.setSpacing(12)
        self.viewLayout.setContentsMargins(24, 20, 24, 16)

        # 标题行 + 关闭按钮
        title_row = QHBoxLayout()
        title_row.setSpacing(8)
        self.title_label = SubtitleLabel(tr("导出视频"), self.widget)
        title_row.addWidget(self.title_label)
        title_row.addStretch()
        self.close_btn = PushButton("✕", self.widget)
        self.close_btn.setObjectName("closeButton")
        self.close_btn.setFixedSize(30, 30)
        self.close_btn.clicked.connect(self._on_cancel_clicked)
        title_row.addWidget(self.close_btn)
        self.viewLayout.addLayout(title_row)

        # 输出视频路径
        out_row = QHBoxLayout()
        out_row.setSpacing(8)
        self._mk_lbl(out_row, tr("输出视频："))
        self.edit_output = LineEdit(self.widget)
        self.edit_output.setPlaceholderText(tr("选择 .mp4 保存路径"))
        out_row.addWidget(self.edit_output, 1)
        self.browse_btn = PushButton(tr("浏览"), self.widget)
        self.browse_btn.clicked.connect(self._on_browse_output)
        out_row.addWidget(self.browse_btn)
        self.viewLayout.addLayout(out_row)

        # 分辨率
        res_row = QHBoxLayout()
        res_row.setSpacing(8)
        self._mk_lbl(res_row, tr("分辨率："))
        self.combo_res = ComboBox(self.widget)
        for _, _, label in _RESOLUTION_PRESETS:
            self.combo_res.addItem(tr(label))
        self.combo_res.addItem(tr("自定义"))
        self.combo_res.currentIndexChanged.connect(self._on_res_changed)
        res_row.addWidget(self.combo_res, 1)
        self.spin_w = SpinBox(self.widget)
        self.spin_w.setRange(320, 7680)
        self.spin_h = SpinBox(self.widget)
        self.spin_h.setRange(240, 4320)
        self.spin_w.hide()
        self.spin_h.hide()
        res_row.addWidget(self.spin_w)
        res_row.addWidget(self.spin_h)
        self.viewLayout.addLayout(res_row)

        # 帧率
        fps_row = QHBoxLayout()
        fps_row.setSpacing(8)
        self._mk_lbl(fps_row, tr("帧率："))
        self.combo_fps = ComboBox(self.widget)
        for fps in (24, 30, 60):
            self.combo_fps.addItem(f"{fps} fps", userData=fps)
        self.combo_fps.setCurrentIndex(2)  # 默认 60
        fps_row.addWidget(self.combo_fps, 1)
        self.viewLayout.addLayout(fps_row)

        # 混入伴奏
        mux_row = QHBoxLayout()
        mux_row.setSpacing(8)
        self._mk_lbl(mux_row, tr("混入伴奏音频："))
        self.sw_mux = SwitchButton(self.widget)
        self.sw_mux.setChecked(True)
        mux_row.addWidget(self.sw_mux)
        mux_row.addStretch()
        self.viewLayout.addLayout(mux_row)

        # 进度
        self.progress_bar = ProgressBar(self.widget)
        self.progress_bar.setRange(0, 1000)
        self.progress_bar.setValue(0)
        self.viewLayout.addWidget(self.progress_bar)
        self.status_label = BodyLabel(tr("待开始"), self.widget)
        self.viewLayout.addWidget(self.status_label)

        self._set_resolution(1920, 1080)

    @staticmethod
    def _mk_lbl(row, text: str):
        lbl = BodyLabel(text)
        lbl.setMinimumWidth(92)
        row.addWidget(lbl)

    # ---------- 状态同步 ----------

    def _sync_default_output(self):
        project_name = self._settings.project.project_name or tr("未命名")
        default_dir = self._settings.last_export_dir
        self.edit_output.setText(os.path.join(default_dir, f"{project_name}"))

    def _set_resolution(self, w: int, h: int):
        for i, (pw, ph, _) in enumerate(_RESOLUTION_PRESETS):
            if pw == w and ph == h:
                self.combo_res.setCurrentIndex(i)
                return
        self.combo_res.setCurrentIndex(len(_RESOLUTION_PRESETS))
        self.spin_w.setValue(w)
        self.spin_h.setValue(h)

    def _on_res_changed(self, index: int):
        custom = index >= len(_RESOLUTION_PRESETS)
        self.spin_w.setVisible(custom)
        self.spin_h.setVisible(custom)
        if not custom:
            w, h, _ = _RESOLUTION_PRESETS[index]
            self.spin_w.setValue(w)
            self.spin_h.setValue(h)

    def _current_resolution(self) -> tuple:
        index = self.combo_res.currentIndex()
        if index < len(_RESOLUTION_PRESETS):
            return _RESOLUTION_PRESETS[index][0], _RESOLUTION_PRESETS[index][1]
        return self.spin_w.value(), self.spin_h.value()

    def _reset_controls(self, exporting: bool):
        self.yesButton.setEnabled(not exporting)
        self.cancelButton.setEnabled(not exporting)
        self.close_btn.setEnabled(not exporting)
        self.edit_output.setEnabled(not exporting)
        self.combo_res.setEnabled(not exporting)
        self.spin_w.setEnabled(not exporting)
        self.spin_h.setEnabled(not exporting)
        self.combo_fps.setEnabled(not exporting)
        self.sw_mux.setEnabled(not exporting)
        self.browse_btn.setEnabled(not exporting)

    # ---------- 事件 ----------

    def _on_browse_output(self):
        path, _ = QFileDialog.getSaveFileName(
            self, tr("导出视频"), self.edit_output.text(),
            tr("视频文件 (*.mp4);;所有文件 (*.*)"),
        )
        if path:
            self.edit_output.setText(path)

    def _on_export(self):
        output = self.edit_output.text().strip()
        if not output:
            InfoBar.warning(tr("提示"), tr("请先选择输出视频路径"), 3000,
                            parent=self.window(), position=InfoBarPosition.TOP_RIGHT)
            return
        if not output.lower().endswith(".mp4"):
            output += ".mp4"
            self.edit_output.setText(output)
        width, height = self._current_resolution()
        fps = self.combo_fps.currentData() or 60
        mux_audio = self.sw_mux.isChecked()

        self._reset_controls(exporting=True)
        self.progress_bar.setValue(0)
        self.status_label.setText(tr("正在渲染…"))

        thread = QThread(self)
        worker = VideoExportWorker(self._ctx, output, width, height, fps, mux_audio)
        worker.moveToThread(thread)
        thread.started.connect(worker.run)
        worker.progress.connect(self._on_progress)
        worker.finished.connect(self._on_finished)
        worker.failed.connect(self._on_failed)
        self._thread = thread
        self._worker = worker
        thread.start()

    def _on_cancel_clicked(self):
        if self._thread is not None and self._thread.isRunning():
            if self._worker is not None:
                self._worker.cancel()
            self.status_label.setText(tr("正在取消…"))
            self.cancelButton.setEnabled(False)
            return
        self.reject()

    def _on_progress(self, value: int):
        self.progress_bar.setValue(value)

    def _on_finished(self, uprd_path: str):
        self._cleanup_thread()
        self._reset_controls(exporting=False)
        self.progress_bar.setValue(1000)
        self.status_label.setText(tr("完成"))
        self._settings.last_export_dir = os.path.dirname(uprd_path) if uprd_path else os.path.dirname(
            self.edit_output.text()
        )
        self._settings.write_settings()
        self.accept()
        InfoBar.success(
            tr("成功"), tr("视频已导出：{0}\n已保存工程：{1}").format(
                self.edit_output.text(), uprd_path or tr("（无）")
            ),
            5000, parent=self.window(), position=InfoBarPosition.TOP_RIGHT,
        )

    def _on_failed(self, message: str):
        self._cleanup_thread()
        self._reset_controls(exporting=False)
        self.status_label.setText(tr("失败"))
        InfoBar.error(
            "ERcode011", tr("导出视频失败：{0}").format(message),
            7000, parent=self.window(), position=InfoBarPosition.TOP_RIGHT,
        )

    def _cleanup_thread(self):
        if self._thread is not None:
            self._thread.quit()
            self._thread.wait()
            self._thread = None
            self._worker = None

    # 导出期间禁止通过 Esc / 关闭按钮关窗，避免线程残留
    def keyPressEvent(self, e):
        if self._thread is not None and self._thread.isRunning():
            return
        super().keyPressEvent(e)

    def closeEvent(self, e):
        if self._thread is not None and self._thread.isRunning():
            e.ignore()
            return
        super().closeEvent(e)
