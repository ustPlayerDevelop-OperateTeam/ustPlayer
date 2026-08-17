# player_style_page.py — "播放器" 导航页
"""播放器样式配置：颜色选择、歌词位置、静默/结束显示。"""

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
    """播放器样式标签页 — 6 个颜色选择 + 歌词位置 + 静默/结束显示。"""

    # 颜色字段列表（行序与 _setup_ui 保持一致；值存于 settings.color 子域）
    _COLOR_ATTRS = [
        "bg_color", "note_color", "lyric_color", "lyric_text_color",
        "other_text_color", "pitch_curve_color",
    ]

    # ===================== 枚举 key ↔ 显示文案 =====================
    # 存储层只认稳定 key（见 core/settings/player.py 的迁移表），
    # 这里仅负责 key → 界面显示文本的映射（显示文本走翻译）。
    @staticmethod
    def _lyric_pos_text(key: str) -> str:
        return tr({
            "top": tr("上"), "bottom": tr("下"),
        }.get(key, tr("上")))

    @staticmethod
    def _pitch_placeholder_text(key: str) -> str:
        return tr({
            "none": tr("无"), "dash": tr("-"), "custom": tr("自定义文字"),
        }.get(key, tr("无")))

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
        self._color_labels = {}   # attr → BodyLabel（颜色行标签）
        self._combo_labels = {}   # attr → BodyLabel（下拉行标签）
        self._setup_ui()
        self._connect_signals()

    # ===================== UI 构建 =====================

    def _setup_ui(self):
        layout = self.page_layout

        # ---- 播放器样式卡片 ----
        self.card_style = SectionCard(tr("播放器样式"))

        # 6 个颜色选择行（ColorPickerButton + LineEdit）
        self._add_color_row(self.card_style.content_layout, tr("背景色:"), "bg_color", self._s.color.bg_color)
        self._add_color_row(self.card_style.content_layout, tr("音名色:"), "note_color", self._s.color.note_color)
        self._add_color_row(self.card_style.content_layout, tr("歌字色:"), "lyric_color", self._s.color.lyric_color)
        self._add_color_row(self.card_style.content_layout, tr("歌词色:"), "lyric_text_color", self._s.color.lyric_text_color)
        self._add_color_row(self.card_style.content_layout, tr("音高线颜色:"), "pitch_curve_color", self._s.color.pitch_curve_color)
        self._add_color_row(self.card_style.content_layout, tr("其他文字色:"), "other_text_color", self._s.color.other_text_color)

        # 歌词位置
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

        # ---- 其他显示设置卡片 ----
        self.card_other = SectionCard(tr("其他显示设置"))

        self._add_combo_with_custom(
            self.card_other.content_layout, tr("音高间占位符:"), "pitch_placeholder",
            {"none": self._pitch_placeholder_text("none"),
             "dash": self._pitch_placeholder_text("dash"),
             "custom": self._pitch_placeholder_text("custom")},
            self._s.player.pitch_placeholder, "pitch_custom",
        )
        self._add_combo_with_custom(
            self.card_other.content_layout, tr("静默时显示:"), "silent_display",
            {"r": self._silent_display_text("r"),
             "dash": self._silent_display_text("dash"),
             "custom": self._silent_display_text("custom"),
             "none": self._silent_display_text("none")},
            self._s.player.silent_display, "silent_custom",
        )
        self._add_combo_with_custom(
            self.card_other.content_layout, tr("结束时显示:"), "end_display",
            {"end": self._end_display_text("end"),
             "dash": self._end_display_text("dash"),
             "custom": self._end_display_text("custom"),
             "none": self._end_display_text("none")},
            self._s.player.end_display, "end_custom",
        )
        layout.addWidget(self.card_other)

        layout.addStretch()

    # ===================== 重译（语言切换时调用） =====================

    def retranslate(self):
        """语言切换后重设全部静态文本（下拉框按 key 重填，选中项保持不变）。"""
        self.card_style.setTitle(tr("播放器样式"))
        for attr, text in (
            ("bg_color", tr("背景色:")),
            ("note_color", tr("音名色:")),
            ("lyric_color", tr("歌字色:")),
            ("lyric_text_color", tr("歌词色:")),
            ("pitch_curve_color", tr("音高线颜色:")),
            ("other_text_color", tr("其他文字色:")),
        ):
            self._color_labels[attr].setText(text)
            picker: ColorPickerButton = getattr(self, f"picker_{attr}")
            picker.setText(tr("选择{0}").format(text))
        self.lyric_pos_lbl.setText(tr("歌词位置:"))
        self._refill_combo(self.lyric_pos_combo, {
            "top": self._lyric_pos_text("top"),
            "bottom": self._lyric_pos_text("bottom"),
        })
        self.card_other.setTitle(tr("其他显示设置"))
        self._retranslate_combo("pitch_placeholder", {
            "none": self._pitch_placeholder_text("none"),
            "dash": self._pitch_placeholder_text("dash"),
            "custom": self._pitch_placeholder_text("custom"),
        })
        self._retranslate_combo("silent_display", {
            "r": self._silent_display_text("r"),
            "dash": self._silent_display_text("dash"),
            "custom": self._silent_display_text("custom"),
            "none": self._silent_display_text("none"),
        })
        self._retranslate_combo("end_display", {
            "end": self._end_display_text("end"),
            "dash": self._end_display_text("dash"),
            "custom": self._end_display_text("custom"),
            "none": self._end_display_text("none"),
        })
        for attr, custom_attr, text in (
            ("pitch_placeholder", "pitch_custom", tr("音高间占位符:")),
            ("silent_display", "silent_custom", tr("静默时显示:")),
            ("end_display", "end_custom", tr("结束时显示:")),
        ):
            self._combo_labels[attr].setText(text)
            getattr(self, f"edit_{custom_attr}").setPlaceholderText(tr("自定义文字..."))

    def _retranslate_combo(self, attr: str, texts: Dict[str, str]):
        """按当前选中 key 重填下拉框文本（选中项保持不变）。"""
        combo: ComboBox = getattr(self, f"combo_{attr}")
        current_key = combo.currentData()
        combo.blockSignals(True)
        combo.clear()
        self._fill_combo(combo, texts)
        idx = combo.findData(current_key)
        combo.setCurrentIndex(idx if idx >= 0 else 0)
        combo.blockSignals(False)

    def _refill_combo(self, combo: ComboBox, texts: Dict[str, str]):
        """重填下拉框（保留选中 key）。"""
        current_key = combo.currentData()
        combo.blockSignals(True)
        combo.clear()
        self._fill_combo(combo, texts)
        idx = combo.findData(current_key)
        combo.setCurrentIndex(idx if idx >= 0 else 0)
        combo.blockSignals(False)

    @staticmethod
    def _fill_combo(combo: ComboBox, texts: Dict[str, str]):
        """按「key → 显示文本」填充下拉框：itemData 存 key，显示走文案。"""
        for key, text in texts.items():
            combo.addItem(text, userData=key)

    def _add_color_row(self, parent: QVBoxLayout, label: str, attr: str, init_color: str):
        """颜色选择行：标签 + LineEdit + ColorPickerButton。

        LineEdit 可手动输入 hex 值，ColorPickerButton 可可视化选色，
        两者双向同步。
        """
        row = QHBoxLayout()
        row.setSpacing(8)

        lbl = BodyLabel(label)
        self._color_labels[attr] = lbl
        row.addWidget(lbl)

        # hex 输入框
        edit = LineEdit()
        edit.setText(init_color)
        edit.setMaximumWidth(100)
        setattr(self, f"edit_{attr}", edit)
        row.addWidget(edit)

        # Fluent 内置颜色选择按钮
        picker = ColorPickerButton(QColor(init_color), tr("选择{0}").format(label), self)
        setattr(self, f"picker_{attr}", picker)
        row.addWidget(picker)

        row.addStretch()
        parent.addLayout(row)

    def _add_combo_with_custom(
        self, parent: QVBoxLayout, label: str, attr: str,
        texts: Dict[str, str], init_key: str, custom_attr: str,
    ):
        """下拉框（key 驱动）+ 可选的自定义文字输入框。

        texts: 「稳定 key → 显示文本」映射；init_key: 当前设置的 key。
        """
        row = QHBoxLayout()
        row.setSpacing(8)

        lbl = BodyLabel(label)
        self._combo_labels[attr] = lbl
        row.addWidget(lbl)

        combo = ComboBox()
        self._fill_combo(combo, texts)
        idx = combo.findData(init_key)
        combo.setCurrentIndex(idx if idx >= 0 else 0)
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

        # 歌词位置（值在 s.player 子域，itemData 为稳定 key）
        self.lyric_pos_combo.currentIndexChanged.connect(
            lambda _: setattr(s.player, "lyric_pos", self.lyric_pos_combo.currentData())
        )
        self._set_combo_by_key(self.lyric_pos_combo, s.player.lyric_pos)
        s.player.lyric_pos_changed.connect(
            lambda v: self._set_combo_by_key(self.lyric_pos_combo, v)
        )
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

    @staticmethod
    def _set_combo_by_key(combo: ComboBox, key: str):
        """按稳定 key 定位下拉框选中项（找不到则回退第 0 项）。"""
        idx = combo.findData(key)
        combo.setCurrentIndex(idx if idx >= 0 else 0)

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
        def on_change(key: str):
            combo: ComboBox = getattr(self, f"combo_{attr}")
            custom_edit: LineEdit = getattr(self, f"edit_{custom_attr}")
            combo.blockSignals(True)
            self._set_combo_by_key(combo, key)
            combo.blockSignals(False)
            custom_edit.setVisible(key == "custom")
        return on_change

    def _bind_combo_with_custom(self, attr: str, custom_attr: str):
        """下拉框选择变更时，显示/隐藏自定义输入框并同步 settings。"""
        combo: ComboBox = getattr(self, f"combo_{attr}")
        custom_edit: LineEdit = getattr(self, f"edit_{custom_attr}")

        def on_change(_):
            key = combo.currentData()
            setattr(self._s.player, attr, key)
            custom_edit.setVisible(key == "custom")

        combo.currentIndexChanged.connect(on_change)
        custom_edit.setVisible(combo.currentData() == "custom")

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
