# basic_page.py — "基础" 导航页
"""项目信息、显示选项和播放控制。"""

import os
from typing import Callable, Optional

from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QFileDialog,
)

from qfluentwidgets import (
    LineEdit, PushButton, PrimaryPushButton, SwitchButton,
    BodyLabel, InfoBar, InfoBarPosition,
)

from ustplayer.context import AppContext
from ustplayer.core.i18n import tr
from ustplayer.ui.section_card import ScrollPage, SectionCard


class BasicPage(ScrollPage):
    """基础页 — 项目信息 + 显示选项 + Play。"""

    # 控件由 _setup_ui 动态创建，此处声明类型以便静态检查识别
    edit_project_name: LineEdit
    edit_song_name: LineEdit
    edit_song_author: LineEdit
    edit_ust_author: LineEdit
    edit_music_path: LineEdit
    sw_show_bpm: SwitchButton
    sw_show_play_time: SwitchButton
    sw_show_song_name: SwitchButton
    sw_show_song_author: SwitchButton
    sw_show_ust_author: SwitchButton

    # 需要随语言切换重译的控件（在 _setup_ui 中创建并保存引用）
    card_project: SectionCard
    card_display: SectionCard
    music_lbl: BodyLabel
    import_btn: PushButton
    export_btn: PushButton
    music_btn: PushButton
    play_btn: PrimaryPushButton
    _field_labels: dict  # attr → BodyLabel（字段名标签）
    _switch_labels: dict  # attr → BodyLabel（显示选项开关标签）

    def __init__(self, ctx: AppContext, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._ctx = ctx
        self._s = ctx.settings
        self._play_callback: Optional[Callable[[], None]] = None
        self._field_labels = {}
        self._switch_labels = {}
        self._setup_ui()
        self._connect_signals()

    def set_play_callback(self, callback: Callable[[], None]):
        self._play_callback = callback

    # ===================== UI 构建 =====================

    def _setup_ui(self):
        layout = self.page_layout

        # ---- 项目卡片 ----
        self.card_project = SectionCard(tr("项目"))

        self._add_field(self.card_project.content_layout, tr("项目名："), "project_name")
        self._add_field(self.card_project.content_layout, tr("曲名&曲师："), "song_name")
        self._add_field(self.card_project.content_layout, tr("MIDI作者："), "song_author")
        self._add_field(self.card_project.content_layout, tr("调音师："), "ust_author")

        # 伴奏音乐（可选，随新版 uplr 打包）
        music_row = QHBoxLayout()
        music_row.setSpacing(8)
        self.music_lbl = BodyLabel(tr("音乐："))
        self.music_lbl.setMinimumWidth(90)
        music_row.addWidget(self.music_lbl)
        self.edit_music_path = LineEdit()
        self.edit_music_path.setPlaceholderText(tr("请选择音频（可选）"))
        music_row.addWidget(self.edit_music_path, 1)
        self.music_btn = PushButton(tr("选择"))
        music_row.addWidget(self.music_btn)
        self.card_project.addLayout(music_row)

        # 导入/保存工程按钮（放在音乐选择框下方）
        btn_row = QHBoxLayout()
        btn_row.setSpacing(12)
        self.import_btn = PushButton(tr("导入项目"))
        self.export_btn = PushButton(tr("保存项目"))
        btn_row.addWidget(self.import_btn)
        btn_row.addWidget(self.export_btn)
        btn_row.addStretch()
        self.card_project.addLayout(btn_row)
        layout.addWidget(self.card_project)

        # ---- 显示选项卡片（Switch 双列网格） ----
        self.card_display = SectionCard(tr("显示选项"))
        switches = [
            ("show_bpm",         tr("显示BPM")),
            ("show_play_time",   tr("显示播放时间")),
            ("show_song_name",   tr("显示曲目信息")),
            ("show_song_author", tr("显示MIDI作者")),
            ("show_ust_author",  tr("显示调音师")),
        ]
        cols = 2
        for i in range(0, len(switches), cols):
            row = QHBoxLayout()
            row.setSpacing(0)
            batch = switches[i:i + cols]
            for attr, label in batch:
                cell = QHBoxLayout()
                cell.setContentsMargins(0, 2, 0, 2)
                sw = SwitchButton()
                cell.addWidget(sw)
                lbl = BodyLabel(label)
                self._switch_labels[attr] = lbl
                cell.addWidget(lbl)
                cell.addStretch()
                row.addLayout(cell)
                setattr(self, f"sw_{attr}", sw)
            # 补空列保持对齐
            for _ in range(cols - len(batch)):
                row.addStretch(1)
            self.card_display.addLayout(row)
        layout.addWidget(self.card_display)

        layout.addStretch()

        # ---- Play 按钮 ----
        self.play_btn = PrimaryPushButton(tr("播放 Play"))
        self.play_btn.setMinimumHeight(40)
        layout.addWidget(self.play_btn)

    def _add_field(self, parent_layout: QVBoxLayout, label: str, attr: str):
        row = QHBoxLayout()
        row.setSpacing(8)
        lbl = BodyLabel(label)
        lbl.setMinimumWidth(90)
        self._field_labels[attr] = lbl
        row.addWidget(lbl)
        edit = LineEdit()
        edit.setPlaceholderText(self._field_placeholder(label))
        row.addWidget(edit, 1)
        setattr(self, f"edit_{attr}", edit)
        parent_layout.addLayout(row)

    @staticmethod
    def _field_placeholder(label: str) -> str:
        """字段输入框占位符：去掉标签尾部的中/英文冒号后套「请输入{0}」。"""
        return tr("请输入{0}").format(label.rstrip("：:"))

    # ===================== 重译（语言切换时调用） =====================

    def retranslate(self):
        """语言切换后重设全部静态文本。"""
        self.card_project.setTitle(tr("项目"))
        for attr, text in (
            ("project_name", tr("项目名：")),
            ("song_name", tr("曲名&曲师：")),
            ("song_author", tr("MIDI作者：")),
            ("ust_author", tr("调音师：")),
        ):
            self._field_labels[attr].setText(text)
            getattr(self, f"edit_{attr}").setPlaceholderText(self._field_placeholder(text))
        self.music_lbl.setText(tr("音乐："))
        self.edit_music_path.setPlaceholderText(tr("请选择音频（可选）"))
        self.music_btn.setText(tr("选择"))
        self.import_btn.setText(tr("导入项目"))
        self.export_btn.setText(tr("保存项目"))
        self.card_display.setTitle(tr("显示选项"))
        for attr, text in (
            ("show_bpm", tr("显示BPM")),
            ("show_play_time", tr("显示播放时间")),
            ("show_song_name", tr("显示曲目信息")),
            ("show_song_author", tr("显示MIDI作者")),
            ("show_ust_author", tr("显示调音师")),
        ):
            self._switch_labels[attr].setText(text)
        self.play_btn.setText(tr("播放 Play"))

    # ===================== 信号绑定 =====================

    def _connect_signals(self):
        s = self._s

        # 初始值 → UI
        self.edit_project_name.setText(s.project.project_name)
        self.edit_song_name.setText(s.project.song_name)
        self.edit_song_author.setText(s.project.song_author)
        self.edit_ust_author.setText(s.project.ust_author)
        self.edit_music_path.setText(s.project.music_path)
        self.sw_show_bpm.setChecked(s.display.show_bpm)
        self.sw_show_play_time.setChecked(s.display.show_play_time)
        self.sw_show_song_name.setChecked(s.display.show_song_name)
        self.sw_show_song_author.setChecked(s.display.show_song_author)
        self.sw_show_ust_author.setChecked(s.display.show_ust_author)

        # UI → settings
        self.edit_project_name.textChanged.connect(lambda v: setattr(s.project, "project_name", v))
        self.edit_song_name.textChanged.connect(lambda v: setattr(s.project, "song_name", v))
        self.edit_song_author.textChanged.connect(lambda v: setattr(s.project, "song_author", v))
        self.edit_ust_author.textChanged.connect(lambda v: setattr(s.project, "ust_author", v))
        self.edit_music_path.textChanged.connect(lambda v: setattr(s.project, "music_path", v))
        self.sw_show_bpm.checkedChanged.connect(lambda v: setattr(s.display, "show_bpm", v))
        self.sw_show_play_time.checkedChanged.connect(lambda v: setattr(s.display, "show_play_time", v))
        self.sw_show_song_name.checkedChanged.connect(lambda v: setattr(s.display, "show_song_name", v))
        self.sw_show_song_author.checkedChanged.connect(lambda v: setattr(s.display, "show_song_author", v))
        self.sw_show_ust_author.checkedChanged.connect(lambda v: setattr(s.display, "show_ust_author", v))

        # settings → UI（信号驱动实时同步，导入 uplr 时自动生效）
        s.project.project_name_changed.connect(lambda v: self.edit_project_name.setText(v))
        s.project.song_name_changed.connect(lambda v: self.edit_song_name.setText(v))
        s.project.song_author_changed.connect(lambda v: self.edit_song_author.setText(v))
        s.project.ust_author_changed.connect(lambda v: self.edit_ust_author.setText(v))
        s.project.music_path_changed.connect(lambda v: self.edit_music_path.setText(v))
        s.display.show_bpm_changed.connect(lambda v: self.sw_show_bpm.setChecked(v))
        s.display.show_play_time_changed.connect(lambda v: self.sw_show_play_time.setChecked(v))
        s.display.show_song_name_changed.connect(lambda v: self.sw_show_song_name.setChecked(v))
        s.display.show_song_author_changed.connect(lambda v: self.sw_show_song_author.setChecked(v))
        s.display.show_ust_author_changed.connect(lambda v: self.sw_show_ust_author.setChecked(v))

        # 按钮
        self.import_btn.clicked.connect(self._on_import)
        self.export_btn.clicked.connect(self._on_export)
        self.music_btn.clicked.connect(self._on_select_music)
        self.play_btn.clicked.connect(self._on_play)

    # ===================== 业务逻辑 =====================

    def _on_import(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self, tr("打开工程文件"), self._s.last_open_dir,
            tr("ustPlayer工程文件 (*.uplr);;所有文件 (*.*)"),
        )
        if not file_path:
            return
        try:
            self._ctx.project_io.import_uplr(file_path)
            self._s.last_open_dir = os.path.dirname(file_path)
            self._s.write_settings()
            # 各页面已通过 settings 信号实时同步，无需手动刷新
            InfoBar.success(tr("成功"), tr("已加载工程：{0}").format(file_path), 3000,
                            parent=self.window(), position=InfoBarPosition.TOP_RIGHT)
        except Exception as e:
            InfoBar.error("ERcode006", tr("加载工程文件失败：{0}").format(e), 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    def _on_export(self):
        file_path, _ = QFileDialog.getSaveFileName(
            self, tr("导出你的工程文件"),
            os.path.join(self._s.last_export_dir, self._s.project.project_name or tr("未命名")),
            tr("ustPlayer工程文件 (*.uplr);;所有文件 (*.*)"),
        )
        if not file_path:
            return
        # 对话框不会自动补扩展名，手动补上，保证文件可被再次导入
        if not file_path.lower().endswith(".uplr"):
            file_path += ".uplr"
        try:
            self._ctx.project_io.export_uplr(file_path)
            self._s.last_export_dir = os.path.dirname(file_path)
            self._s.write_settings()
            InfoBar.success(tr("成功"), tr("工程已导出到：{0}").format(file_path), 3000,
                            parent=self.window(), position=InfoBarPosition.TOP_RIGHT)
        except Exception as e:
            InfoBar.error("ERcode010", tr("导出失败：{0}").format(e), 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    def _on_play(self):
        if self._play_callback:
            self._play_callback()

    def _on_select_music(self):
        file_path, _ = QFileDialog.getOpenFileName(
            self, tr("选择伴奏音乐"),
            os.path.dirname(self._s.project.music_path) if self._s.project.music_path else "",
            tr("音频文件 (*.flac *.mp3 *.wav *.ogg *.m4a);;所有文件 (*.*)"),
        )
        if file_path:
            self.edit_music_path.setText(file_path)

    def _sync_ui_from_settings(self):
        s = self._s
        self.edit_project_name.setText(s.project.project_name)
        self.edit_song_name.setText(s.project.song_name)
        self.edit_song_author.setText(s.project.song_author)
        self.edit_ust_author.setText(s.project.ust_author)
        self.edit_music_path.setText(s.project.music_path)
        self.sw_show_bpm.setChecked(s.display.show_bpm)
        self.sw_show_play_time.setChecked(s.display.show_play_time)
        self.sw_show_song_name.setChecked(s.display.show_song_name)
        self.sw_show_song_author.setChecked(s.display.show_song_author)
        self.sw_show_ust_author.setChecked(s.display.show_ust_author)

    def sync_all_from_settings(self):
        self._sync_ui_from_settings()
