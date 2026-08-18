# player_style_page.py — 播放器样式配置页
"""颜色、歌词位置、静默/结束显示。"""

from typing import Dict, Optional

from PySide6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout
from PySide6.QtGui import QColor

from qfluentwidgets import (
    LineEdit, ComboBox, ColorPickerButton,
    BodyLabel,
)

from ustplayer.context import AppContext
from ustplayer.core.i18n import tr
from ustplayer.ui.section_card import ScrollPage, SectionCard


class PlayerStylePage(ScrollPage):
    _COLOR_ATTRS = [
        "bg_color", "note_color", "lyric_color", "lyric_text_color",
        "pitch_curve_color", "other_text_color",
    ]

    @staticmethod
    def _lyric_pos_text(key: str) -> str:
        return tr({"top": tr("上"), "bottom": tr("下")}.get(key, tr("上")))

    @staticmethod
    def _pitch_placeholder_text(key: str) -> str:
        return tr({"none": tr("无"), "dash": tr("-"), "custom": tr("自定义文字")}.get(key, tr("无")))

    @staticmethod
    def _silent_display_text(key: str) -> str:
        return tr({
            "r": tr("R"), "dash": tr("-"), "custom": tr("自定义文字"),
            "none": tr("什么都不显示"),
        }.get(key, tr("R")))

    @staticmethod
    def _end_display_text(key: str) -> str:
        return tr({
            "end": tr("END"), "dash": tr("-"), "custom": tr("自定义文字"),
            "none": tr("什么都不显示"),
        }.get(key, tr("END")))

    def __init__(self, ctx: AppContext, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._ctx = ctx
        self._s = ctx.settings
        self._color_labels = {}
        self._combo_labels = {}
        self._setup_ui()
        self._connect_signals()

    def _setup_ui(self):
        layout = self.page_layout
        self.card_style = SectionCard(tr("播放器样式"))

        for label, attr in (
            (tr("背景色:"), "bg_color"),
            (tr("音名色:"), "note_color"),
            (tr("歌字色:"), "lyric_color"),
            (tr("歌词色:"), "lyric_text_color"),
            (tr("音高线颜色:"), "pitch_curve_color"),
            (tr("其他文字色:"), "other_text_color"),
        ):
            self._add_color_row(self.card_style.content_layout, label, attr, getattr(self._s.color, attr))

        row_lyric = QHBoxLayout()
        row_lyric.setSpacing(8)
        self.lyric_pos_lbl = BodyLabel(tr("歌词位置:"))
        row_lyric.addWidget(self.lyric_pos_lbl)
        self.lyric_pos_combo = ComboBox()
        self._fill_combo(self.lyric_pos_combo, {
            "top": self._lyric_pos_text("top"),
            "bottom": self._lyric_pos_text("bottom"),
        })
        row_lyric.addWidget(self.lyric_pos_combo)
        row_lyric.addStretch()
        self.card_style.addLayout(row_lyric)
        layout.addWidget(self.card_style)

        self.card_other = SectionCard(tr("其他显示设置"))
        self._add_combo_with_custom(
            self.card_other.content_layout, tr("音高间占位符:"), "pitch_placeholder",
            {"none": self._pitch_placeholder_text("none"), "dash": self._pitch_placeholder_text("dash"),
             "custom": self._pitch_placeholder_text("custom")},
            self._s.player.pitch_placeholder, "pitch_custom",
        )
        self._add_combo_with_custom(
            self.card_other.content_layout, tr("静默时显示:"), "silent_display",
            {"r": self._silent_display_text("r"), "dash": self._silent_display_text("dash"),
             "custom": self._silent_display_text("custom"), "none": self._silent_display_text("none")},
            self._s.player.silent_display, "silent_custom",
        )
        self._add_combo_with_custom(
            self.card_other.content_layout, tr("结束时显示:"), "end_display",
            {"end": self._end_display_text("end"), "dash": self._end_display_text("dash"),
             "custom": self._end_display_text("custom"), "none": self._end_display_text("none")},
            self._s.player.end_display, "end_custom",
        )
        layout.addWidget(self.card_other)
        layout.addStretch()

    def retranslate(self):
        self.card_style.setTitle(tr("播放器样式"))
        for attr, text in (
            ("bg_color", tr("背景色:")), ("note_color", tr("音名色:")),
            ("lyric_color", tr("歌字色:")), ("lyric_text_color", tr("歌词色:")),
            ("pitch_curve_color", tr("音高线颜色:")), ("other_text_color", tr("其他文字色:")),
        ):
            self._color_labels[attr].setText(text)
            picker = getattr(self, f"picker_{attr}")
            picker.setText(tr("选择{0}").format(text))
        self.lyric_pos_lbl.setText(tr("歌词位置:"))
        self._refill_combo(self.lyric_pos_combo, {
            "top": self._lyric_pos_text("top"), "bottom": self._lyric_pos_text("bottom"),
        })
        self.card_other.setTitle(tr("其他显示设置"))
        for attr, texts in (
            ("pitch_placeholder", {"none": self._pitch_placeholder_text("none"), "dash": self._pitch_placeholder_text("dash"), "custom": self._pitch_placeholder_text("custom")}),
            ("silent_display", {"r": self._silent_display_text("r"), "dash": self._silent_display_text("dash"), "custom": self._silent_display_text("custom"), "none": self._silent_display_text("none")}),
            ("end_display", {"end": self._end_display_text("end"), "dash": self._end_display_text("dash"), "custom": self._end_display_text("custom"), "none": self._end_display_text("none")}),
        ):
            self._retranslate_combo(attr, texts)
            custom_attr = {"pitch_placeholder": "pitch_custom", "silent_display": "silent_custom", "end_display": "end_custom"}[attr]
            self._combo_labels[attr].setText({
                "pitch_placeholder": tr("音高间占位符:"),
                "silent_display": tr("静默时显示:"),
                "end_display": tr("结束时显示:"),
            }[attr])
            getattr(self, f"edit_{custom_attr}").setPlaceholderText(tr("自定义文字..."))

    def _retranslate_combo(self, attr: str, texts: Dict[str, str]):
        combo = getattr(self, f"combo_{attr}")
        current = combo.currentData()
        combo.blockSignals(True)
        combo.clear()
        self._fill_combo(combo, texts)
        combo.setCurrentIndex(combo.findData(current) if combo.findData(current) >= 0 else 0)
        combo.blockSignals(False)

    def _refill_combo(self, combo: ComboBox, texts: Dict[str, str]):
        current = combo.currentData()
        combo.blockSignals(True)
        combo.clear()
        self._fill_combo(combo, texts)
        combo.setCurrentIndex(combo.findData(current) if combo.findData(current) >= 0 else 0)
        combo.blockSignals(False)

    @staticmethod
    def _fill_combo(combo: ComboBox, texts: Dict[str, str]):
        for key, text in texts.items():
            combo.addItem(text, userData=key)

    def _add_color_row(self, parent: QVBoxLayout, label: str, attr: str, init_color: str):
        row = QHBoxLayout()
        row.setSpacing(8)
        lbl = BodyLabel(label)
        self._color_labels[attr] = lbl
        row.addWidget(lbl)
        edit = LineEdit()
        edit.setText(init_color)
        edit.setMaximumWidth(100)
        setattr(self, f"edit_{attr}", edit)
        row.addWidget(edit)
        picker = ColorPickerButton(QColor(init_color), tr("选择{0}").format(label), self)
        setattr(self, f"picker_{attr}", picker)
        row.addWidget(picker)
        row.addStretch()
        parent.addLayout(row)

    def _add_combo_with_custom(self, parent, label, attr, texts, init_key, custom_attr):
        row = QHBoxLayout()
        row.setSpacing(8)
        lbl = BodyLabel(label)
        self._combo_labels[attr] = lbl
        row.addWidget(lbl)
        combo = ComboBox()
        self._fill_combo(combo, texts)
        combo.setCurrentIndex(combo.findData(init_key) if combo.findData(init_key) >= 0 else 0)
        setattr(self, f"combo_{attr}", combo)
        row.addWidget(combo)
        custom_edit = LineEdit()
        custom_edit.setPlaceholderText(tr("自定义文字..."))
        custom_edit.setMaximumWidth(150)
        custom_edit.setVisible(init_key == "custom")
        setattr(self, f"edit_{custom_attr}", custom_edit)
        row.addWidget(custom_edit)
        row.addStretch()
        parent.addLayout(row)

    def _connect_signals(self):
        s = self._s
        for attr in self._COLOR_ATTRS:
            edit = getattr(self, f"edit_{attr}")
            picker = getattr(self, f"picker_{attr}")

            def bind_edit(a=attr, p=picker):
                def on_text(v: str):
                    setattr(s.color, a, v)
                    p.blockSignals(True)
                    p.setColor(QColor(v) if v else QColor("#FFFFFF"))
                    p.blockSignals(False)
                return on_text

            def bind_picker(a=attr, ed=edit):
                def on_color(c: QColor):
                    h = c.name()
                    setattr(s.color, a, h)
                    ed.blockSignals(True)
                    ed.setText(h)
                    ed.blockSignals(False)
                return on_color

            edit.textChanged.connect(bind_edit())
            picker.colorChanged.connect(bind_picker())
            getattr(s.color, f"{attr}_changed").connect(self._make_color_sync(attr))

        self.lyric_pos_combo.currentIndexChanged.connect(
            lambda _: setattr(s.player, "lyric_pos", self.lyric_pos_combo.currentData())
        )
        self._set_combo_by_key(self.lyric_pos_combo, s.player.lyric_pos)
        s.player.lyric_pos_changed.connect(lambda v: self._set_combo_by_key(self.lyric_pos_combo, v))

        self._bind_combo_with_custom("pitch_placeholder", "pitch_custom")
        self._bind_combo_with_custom("silent_display", "silent_custom")
        self._bind_combo_with_custom("end_display", "end_custom")

        s.player.pitch_placeholder_changed.connect(self._make_combo_sync("pitch_placeholder", "pitch_custom"))
        s.player.silent_display_changed.connect(self._make_combo_sync("silent_display", "silent_custom"))
        s.player.end_display_changed.connect(self._make_combo_sync("end_display", "end_custom"))
        s.player.pitch_custom_text_changed.connect(lambda v: getattr(self, "edit_pitch_custom").setText(v))
        s.player.silent_custom_text_changed.connect(lambda v: getattr(self, "edit_silent_custom").setText(v))
        s.player.end_custom_text_changed.connect(lambda v: getattr(self, "edit_end_custom").setText(v))

        edit_pitch = getattr(self, "edit_pitch_custom")
        edit_pitch.setText(s.player.pitch_custom_text)
        edit_pitch.textChanged.connect(lambda v: setattr(s.player, "pitch_custom_text", v))

        edit_silent = getattr(self, "edit_silent_custom")
        edit_silent.setText(s.player.silent_custom_text)
        edit_silent.textChanged.connect(lambda v: setattr(s.player, "silent_custom_text", v))

        edit_end = getattr(self, "edit_end_custom")
        edit_end.setText(s.player.end_custom_text)
        edit_end.textChanged.connect(lambda v: setattr(s.player, "end_custom_text", v))

    @staticmethod
    def _set_combo_by_key(combo: ComboBox, key: str):
        idx = combo.findData(key)
        combo.setCurrentIndex(idx if idx >= 0 else 0)

    def _make_color_sync(self, attr: str):
        def on_change(color: str):
            edit = getattr(self, f"edit_{attr}")
            picker = getattr(self, f"picker_{attr}")
            edit.blockSignals(True)
            edit.setText(color)
            edit.blockSignals(False)
            picker.blockSignals(True)
            picker.setColor(QColor(color) if color else QColor("#FFFFFF"))
            picker.blockSignals(False)
        return on_change

    def _make_combo_sync(self, attr: str, custom_attr: str):
        def on_change(key: str):
            combo = getattr(self, f"combo_{attr}")
            custom_edit = getattr(self, f"edit_{custom_attr}")
            combo.blockSignals(True)
            self._set_combo_by_key(combo, key)
            combo.blockSignals(False)
            custom_edit.setVisible(key == "custom")
        return on_change

    def _bind_combo_with_custom(self, attr: str, custom_attr: str):
        combo = getattr(self, f"combo_{attr}")
        custom_edit = getattr(self, f"edit_{custom_attr}")

        def on_change(_):
            setattr(self._s.player, attr, combo.currentData())
            custom_edit.setVisible(combo.currentData() == "custom")

        combo.currentIndexChanged.connect(on_change)
        custom_edit.setVisible(combo.currentData() == "custom")

    def sync_all_from_settings(self):
        s = self._s
        for attr in self._COLOR_ATTRS:
            color = getattr(s.color, attr)
            edit = getattr(self, f"edit_{attr}")
            picker = getattr(self, f"picker_{attr}")
            edit.blockSignals(True)
            edit.setText(color)
            edit.blockSignals(False)
            picker.blockSignals(True)
            picker.setColor(QColor(color))
            picker.blockSignals(False)

        self._set_combo_by_key(self.lyric_pos_combo, s.player.lyric_pos)
        getattr(self, "combo_pitch_placeholder").setCurrentIndex(
            max(getattr(self, "combo_pitch_placeholder").findData(s.player.pitch_placeholder), 0)
        )
        getattr(self, "edit_pitch_custom").setText(s.player.pitch_custom_text)
        getattr(self, "combo_silent_display").setCurrentIndex(
            max(getattr(self, "combo_silent_display").findData(s.player.silent_display), 0)
        )
        getattr(self, "edit_silent_custom").setText(s.player.silent_custom_text)
        getattr(self, "combo_end_display").setCurrentIndex(
            max(getattr(self, "combo_end_display").findData(s.player.end_display), 0)
        )
        getattr(self, "edit_end_custom").setText(s.player.end_custom_text)