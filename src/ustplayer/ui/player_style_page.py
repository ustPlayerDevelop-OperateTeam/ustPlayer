# player_style_page.py — "播放器" 导航页
"""播放器样式配置：颜色选择、歌词位置、静默/结束显示。"""

from typing import Optional

from PySide6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout
from PySide6.QtGui import QColor

from qfluentwidgets import (
    LineEdit, ComboBox, ColorPickerButton,
    BodyLabel,
)

from ustplayer.context import AppContext
from ustplayer.ui.section_card import ScrollPage, SectionCard


class PlayerStylePage(ScrollPage):
    """播放器样式标签页 — 6 个颜色选择 + 歌词位置 + 静默/结束显示。"""

    # 颜色字段列表（行序与 _setup_ui 保持一致；值存于 settings.color 子域）
    _COLOR_ATTRS = [
        "bg_color", "note_color", "lyric_color", "lyric_text_color",
        "other_text_color", "pitch_curve_color",
    ]

    def __init__(self, ctx: AppContext, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._ctx = ctx
        self._s = ctx.settings
        self._setup_ui()
        self._connect_signals()

    # ===================== UI 构建 =====================

    def _setup_ui(self):
        layout = self.page_layout

        # ---- 播放器样式卡片 ----
        card_style = SectionCard("播放器样式")

        # 6 个颜色选择行（ColorPickerButton + LineEdit）
        self._add_color_row(card_style.content_layout, "背景色:", "bg_color", self._s.color.bg_color)
        self._add_color_row(card_style.content_layout, "音名色:", "note_color", self._s.color.note_color)
        self._add_color_row(card_style.content_layout, "歌字色:", "lyric_color", self._s.color.lyric_color)
        self._add_color_row(card_style.content_layout, "歌词色:", "lyric_text_color", self._s.color.lyric_text_color)
        self._add_color_row(card_style.content_layout, "音高线颜色:", "pitch_curve_color", self._s.color.pitch_curve_color)
        self._add_color_row(card_style.content_layout, "其他文字色:", "other_text_color", self._s.color.other_text_color)

        # 歌词位置
        row_lyric = QHBoxLayout()
        row_lyric.setSpacing(8)
        row_lyric.addWidget(BodyLabel("歌词位置:"))
        self.lyric_pos_combo = ComboBox()
        self.lyric_pos_combo.addItems(["上", "下"])
        row_lyric.addWidget(self.lyric_pos_combo)
        row_lyric.addStretch()
        card_style.addLayout(row_lyric)
        layout.addWidget(card_style)

        # ---- 其他显示设置卡片 ----
        card_other = SectionCard("其他显示设置")

        self._add_combo_with_custom(
            card_other.content_layout, "音高间占位符:", "pitch_placeholder",
            ["无", "-", "自定义文字"],
            self._s.player.pitch_placeholder, "pitch_custom",
        )
        self._add_combo_with_custom(
            card_other.content_layout, "静默时显示:", "silent_display",
            ["R", "-", "自定义文字", "什么都不显示"],
            self._s.player.silent_display, "silent_custom",
        )
        self._add_combo_with_custom(
            card_other.content_layout, "结束时显示:", "end_display",
            ["END", "-", "自定义文字", "什么都不显示"],
            self._s.player.end_display, "end_custom",
        )
        layout.addWidget(card_other)

        layout.addStretch()

    def _add_color_row(self, parent: QVBoxLayout, label: str, attr: str, init_color: str):
        """颜色选择行：标签 + LineEdit + ColorPickerButton。

        LineEdit 可手动输入 hex 值，ColorPickerButton 可可视化选色，
        两者双向同步。
        """
        row = QHBoxLayout()
        row.setSpacing(8)

        row.addWidget(BodyLabel(label))

        # hex 输入框
        edit = LineEdit()
        edit.setText(init_color)
        edit.setMaximumWidth(100)
        setattr(self, f"edit_{attr}", edit)
        row.addWidget(edit)

        # Fluent 内置颜色选择按钮
        picker = ColorPickerButton(QColor(init_color), f"选择{label}", self)
        setattr(self, f"picker_{attr}", picker)
        row.addWidget(picker)

        row.addStretch()
        parent.addLayout(row)

    def _add_combo_with_custom(
        self, parent: QVBoxLayout, label: str, attr: str,
        options: list, init_value: str, custom_attr: str,
    ):
        """下拉框 + 可选的自定义文字输入框。"""
        row = QHBoxLayout()
        row.setSpacing(8)

        row.addWidget(BodyLabel(label))

        combo = ComboBox()
        combo.addItems(options)
        combo.setCurrentText(init_value)
        setattr(self, f"combo_{attr}", combo)
        row.addWidget(combo)

        custom_edit = LineEdit()
        custom_edit.setPlaceholderText("自定义文字...")
        custom_edit.setMaximumWidth(150)
        custom_edit.setVisible(init_value == "自定义文字")
        setattr(self, f"edit_{custom_attr}", custom_edit)
        row.addWidget(custom_edit)

        row.addStretch()
        parent.addLayout(row)

    # ===================== 信号绑定 =====================

    def _connect_signals(self):
        s = self._s

        # 颜色：LineEdit ↔ ColorPickerButton ↔ Settings 三向同步（值在 s.color 子域）
        for attr in self._COLOR_ATTRS:
            _edit: LineEdit = getattr(self, f"edit_{attr}")
            _picker: ColorPickerButton = getattr(self, f"picker_{attr}")

            # 用默认参数捕获当前循环值，避免闭包延迟绑定
            def bind_edit(a=attr, p=_picker):
                def on_text(v: str):
                    setattr(s.color, a, v)
                    p.blockSignals(True)
                    p.setColor(QColor(v) if v else QColor("#FFFFFF"))
                    p.blockSignals(False)
                return on_text

            def bind_picker(a=attr, ed=_edit):
                def on_color(c: QColor):
                    h = c.name()
                    setattr(s.color, a, h)
                    ed.blockSignals(True)
                    ed.setText(h)
                    ed.blockSignals(False)
                return on_color

            _edit.textChanged.connect(bind_edit())
            _picker.colorChanged.connect(bind_picker())

            # settings → UI（信号驱动实时同步）
            s_changed = getattr(s.color, f"{attr}_changed")
            s_changed.connect(self._make_color_sync(attr))

        # 歌词位置（值在 s.player 子域）
        self.lyric_pos_combo.currentTextChanged.connect(lambda v: setattr(s.player, "lyric_pos", v))
        self.lyric_pos_combo.setCurrentText(s.player.lyric_pos)
        s.player.lyric_pos_changed.connect(lambda v: self.lyric_pos_combo.setCurrentText(v))

        # 下拉框 + 自定义文字联动
        self._bind_combo_with_custom("pitch_placeholder", "pitch_custom")
        self._bind_combo_with_custom("silent_display", "silent_custom")
        self._bind_combo_with_custom("end_display", "end_custom")

        # settings → UI：下拉框与自定义文字
        s.player.pitch_placeholder_changed.connect(self._make_combo_sync("pitch_placeholder", "pitch_custom"))
        s.player.silent_display_changed.connect(self._make_combo_sync("silent_display", "silent_custom"))
        s.player.end_display_changed.connect(self._make_combo_sync("end_display", "end_custom"))
        s.player.pitch_custom_text_changed.connect(lambda v: getattr(self, "edit_pitch_custom").setText(v))
        s.player.silent_custom_text_changed.connect(lambda v: getattr(self, "edit_silent_custom").setText(v))
        s.player.end_custom_text_changed.connect(lambda v: getattr(self, "edit_end_custom").setText(v))

        # 自定义文字初始化
        edit_pitch = getattr(self, "edit_pitch_custom")
        edit_pitch.setText(s.player.pitch_custom_text)
        edit_pitch.textChanged.connect(lambda v: setattr(s.player, "pitch_custom_text", v))

        edit_silent = getattr(self, "edit_silent_custom")
        edit_silent.setText(s.player.silent_custom_text)
        edit_silent.textChanged.connect(lambda v: setattr(s.player, "silent_custom_text", v))

        edit_end = getattr(self, "edit_end_custom")
        edit_end.setText(s.player.end_custom_text)
        edit_end.textChanged.connect(lambda v: setattr(s.player, "end_custom_text", v))

    def _make_color_sync(self, attr: str):
        """构造 settings 颜色信号 → UI 同步回调。"""
        def on_change(color: str):
            edit: LineEdit = getattr(self, f"edit_{attr}")
            picker: ColorPickerButton = getattr(self, f"picker_{attr}")
            edit.blockSignals(True)
            edit.setText(color)
            edit.blockSignals(False)
            picker.blockSignals(True)
            picker.setColor(QColor(color) if color else QColor("#FFFFFF"))
            picker.blockSignals(False)
        return on_change

    def _make_combo_sync(self, attr: str, custom_attr: str):
        """构造 settings 下拉框信号 → UI 同步回调。"""
        def on_change(value: str):
            combo: ComboBox = getattr(self, f"combo_{attr}")
            custom_edit: LineEdit = getattr(self, f"edit_{custom_attr}")
            combo.blockSignals(True)
            combo.setCurrentText(value)
            combo.blockSignals(False)
            custom_edit.setVisible(value == "自定义文字")
        return on_change

    def _bind_combo_with_custom(self, attr: str, custom_attr: str):
        """下拉框选择变更时，显示/隐藏自定义输入框并同步 settings。"""
        combo: ComboBox = getattr(self, f"combo_{attr}")
        custom_edit: LineEdit = getattr(self, f"edit_{custom_attr}")

        def on_change(value: str):
            setattr(self._s.player, attr, value)
            custom_edit.setVisible(value == "自定义文字")

        combo.currentTextChanged.connect(on_change)
        custom_edit.setVisible(combo.currentText() == "自定义文字")

    # ===================== 同步 =====================

    def sync_all_from_settings(self):
        """导入 uplr 后同步 UI（信号驱动的兜底）。"""
        s = self._s
        for attr in self._COLOR_ATTRS:
            color = getattr(s.color, attr)
            edit: LineEdit = getattr(self, f"edit_{attr}")
            picker: ColorPickerButton = getattr(self, f"picker_{attr}")
            edit.blockSignals(True)
            edit.setText(color)
            edit.blockSignals(False)
            picker.blockSignals(True)
            picker.setColor(QColor(color))
            picker.blockSignals(False)

        self.lyric_pos_combo.setCurrentText(s.player.lyric_pos)
        getattr(self, "combo_pitch_placeholder").setCurrentText(s.player.pitch_placeholder)
        getattr(self, "edit_pitch_custom").setText(s.player.pitch_custom_text)
        getattr(self, "combo_silent_display").setCurrentText(s.player.silent_display)
        getattr(self, "edit_silent_custom").setText(s.player.silent_custom_text)
        getattr(self, "combo_end_display").setCurrentText(s.player.end_display)
        getattr(self, "edit_end_custom").setText(s.player.end_custom_text)
