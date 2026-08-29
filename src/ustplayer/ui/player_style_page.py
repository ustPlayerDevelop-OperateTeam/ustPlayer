# player_style_page.py — 播放器样式配置页
"""颜色、歌词位置、静默/结束显示。"""

import os
from typing import Dict, Optional

from PySide6.QtCore import QSize, QTimer
from PySide6.QtGui import QColor, QFont, QFontDatabase, QFontMetrics
from PySide6.QtWidgets import QFileDialog, QHBoxLayout, QVBoxLayout, QWidget

from qfluentwidgets import (
    BodyLabel,
    ColorPickerButton,
    ComboBox,
    InfoBar,
    InfoBarPosition,
    LineEdit,
)
from qfluentwidgets.components.widgets.combo_box import ComboBoxMenu
from qfluentwidgets.components.widgets.menu import MenuAnimationType

from ustplayer.context import AppContext
from ustplayer.core.i18n import tr
from ustplayer.ui.section_card import ScrollPage, SectionCard


class _FontComboMenu(ComboBoxMenu):
    """字体下拉菜单：创建菜单项时按字体名设置该项字体，实现下拉预览。

    给每个 item setFont() 即可让该项以该字体渲染预览；选中竖条由库
    IndicatorMenuItemDelegate 用 themeColor() 自动绘制，暗色适配与风格一致。
    """

    def _createActionItem(self, action, before=None):
        item = super()._createActionItem(action, before)
        name = action.text()
        # “自定义…”项按当前界面语言显示，不能拿硬编码中文比较
        if name and name != tr("自定义…"):
            font = QFont(name, 11)
            item.setFont(font)
            # 用预览字体的 metrics 重算宽度，避免字体名较宽时被裁剪
            fm = QFontMetrics(font)
            w = 40 + fm.horizontalAdvance(name)
            if w > item.sizeHint().width():
                item.setSizeHint(QSize(w, item.sizeHint().height()))
        return item

    def exec(self, pos, ani=True, aniType=MenuAnimationType.DROP_DOWN):
        # 子类化后 PySide6 shiboken 会将实例 exec 解析回 C++ QMenu.exec
        # （静态+实例双重方法），导致 aniType 关键字参数不被接受、下拉框无法弹出。
        # 显式重写 exec 转发到 ComboBoxMenu.exec（Python 函数）绕过此问题。
        return ComboBoxMenu.exec(self, pos, ani, aniType)


class _FontComboBox(ComboBox):
    """字体选择下拉框：使用自定义菜单以启用每项字体预览。"""

    def _createComboMenu(self):
        return _FontComboMenu(self)


class PlayerStylePage(ScrollPage):
    _COLOR_ATTRS = [
        "bg_color", "note_color", "lyric_color", "lyric_text_color",
        "pitch_curve_color", "other_text_color",
    ]

    # 内置字体（每项以下拉中自身字体预览）；「自定义…」为文件导入入口
    _BUILTIN_FONTS = ["等线", "微软雅黑", "黑体", "楷体", "宋体"]
    _CUSTOM_FONT_KEY = "自定义…"

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
        self._font_labels = {}  # 字体槽位 → BodyLabel（重译用）
        self._path_to_family: Dict[str, str] = {}  # 自定义字体路径 → 家族名（避免重复注册）
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

        # 字体（按用途分槽，仿颜色选项）：音名 / 歌字 / 歌词 / 信息文字
        # 每行 = 标签 + _FontComboBox（内置字体预览 + 「自定义…」文件导入）
        for attr, label in self._FONT_ATTRS:
            combo = self._add_font_row(self.card_style.content_layout, label, attr)
            setattr(self, f"combo_font_{attr}", combo)
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
        for attr, text in (
            ("font_note", tr("音名字体:")), ("font_ust_lyric", tr("歌字字体:")),
            ("font_lrc", tr("歌词字体:")), ("font_other", tr("其他文字字体:")),
        ):
            self._font_labels[attr].setText(text)
        self._refresh_all_font_combos()
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

        # 自定义字体（按用途分槽）：textActivated 仅用户手动选择触发（避免 sync 时误写）
        for attr, _label in self._FONT_ATTRS:
            combo = getattr(self, f"combo_font_{attr}")
            combo.textActivated.connect(
                lambda t, c=combo, a=attr: self._on_font_changed(c, a, t)
            )
            getattr(s.display, f"{attr}_changed").connect(
                lambda _v, a=attr: self._fill_font_combo(getattr(self, f"combo_font_{a}"), a)
            )
        s.display.custom_font_paths_changed.connect(lambda v: self._refresh_all_font_combos())

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

    # ===================== 自定义字体（内置 + 文件导入，按用途分槽） =====================

    # 字体槽位（与 DisplaySettings 属性一一对应；仿颜色选项按用途分类）
    # 注意：标签必须用 tr("...") 字面量——lupdate 只提取字面量，变量传参不会被收录
    _FONT_ATTRS = [
        ("font_note", tr("音名字体:")),
        ("font_ust_lyric", tr("歌字字体:")),
        ("font_lrc", tr("歌词字体:")),
        ("font_other", tr("其他文字字体:")),
    ]

    def _fill_font_combo(self, combo: ComboBox, attr: str):
        """重建单个字体下拉（保留选中值）：内置字体 + 已导入字体 + 自定义… 入口。

        每项字体预览由 _FontComboMenu 的 _createActionItem 按字体名自动设置。
        """
        current = getattr(self._s.display, attr) or self._BUILTIN_FONTS[0]
        combo.blockSignals(True)
        combo.clear()
        combo.addItems(self._BUILTIN_FONTS)
        # 先放「自定义…」入口（稳定 userData，不依赖显示语言），
        # 再把已导入字体插入到它的前面。
        combo.addItem(tr("自定义…"), userData=self._CUSTOM_FONT_KEY)
        for path in self._s.display.custom_font_paths:
            family = self._apply_custom_font(path)
            if family and combo.findText(family) < 0:
                combo.insertItem(combo.count() - 1, family)
        combo.setCurrentText(current if combo.findText(current) >= 0 else self._BUILTIN_FONTS[0])
        combo.blockSignals(False)

    def _refresh_all_font_combos(self):
        """按各槽位当前值重建全部字体下拉（设置变化 / 导入字体路径变化时调用）。"""
        for attr, _label in self._FONT_ATTRS:
            self._fill_font_combo(getattr(self, f"combo_font_{attr}"), attr)

    def _add_font_row(self, parent, label: str, attr: str) -> ComboBox:
        """添加一个字体选择行（仿颜色行：标签 + _FontComboBox），返回 combo。"""
        row = QHBoxLayout()
        row.setSpacing(8)
        lbl = BodyLabel(tr(label))
        self._font_labels[attr] = lbl
        row.addWidget(lbl)
        combo = _FontComboBox()
        self._fill_font_combo(combo, attr)
        row.addWidget(combo, 1)
        parent.addLayout(row)
        return combo

    def _on_font_changed(self, combo: ComboBox, attr: str, text: str):
        """字体选择变更。选「自定义…」时弹文件框加载字体。

        textActivated 仅用户手动选择触发，规避 sync 时 setCurrentText 误写；
        attr 指明当前槽位（音名/歌字/歌词/信息）。
        """
        if combo.currentData() == self._CUSTOM_FONT_KEY:
            combo.blockSignals(True)
            combo.setCurrentText(getattr(self._s.display, attr) or self._BUILTIN_FONTS[0])
            combo.blockSignals(False)
            QTimer.singleShot(0, lambda: self._open_font_dialog(attr))
            return
        setattr(self._s.display, attr, text)
        # 缺失字体会被 Qt 静默回退，此处检测并提示
        if not QFontDatabase.hasFamily(text):
            InfoBar.warning(
                tr("提示"), tr("字体「{0}」未在本机安装，将使用回退字体显示").format(text),
                duration=5000, parent=self.window(), position=InfoBarPosition.TOP_RIGHT,
            )

    def _open_font_dialog(self, attr: str):
        """文件框选择字体文件（.ttf/.otf）并应用到 attr 槽位。

        Windows 原生文件对话框打开 C:\\Windows\\Fonts 时文件列表为空
        （该目录是 shell 虚拟文件夹），故用非原生对话框。
        """
        fonts_dir = os.path.join(os.environ.get("SystemRoot", "C:\\Windows"), "Fonts")
        file_path, _ = QFileDialog.getOpenFileName(
            self, tr("选择字体文件"), fonts_dir,
            tr("字体文件 (*.ttf *.otf);;所有文件 (*.*)"),
            options=QFileDialog.Option.DontUseNativeDialog,
        )
        if not file_path:
            return
        self._load_custom_font(file_path, attr)

    def _apply_custom_font(self, file_path: str) -> Optional[str]:
        """注册字体文件并返回家族名（失败返回 None）。

        addApplicationFont 全局生效；_path_to_family 缓存路径→家族名，
        已加载过的路径不重复注册。
        """
        if not file_path or not os.path.isfile(file_path):
            return None
        if file_path in self._path_to_family:
            return self._path_to_family[file_path]
        font_id = QFontDatabase.addApplicationFont(file_path)
        if font_id == -1:
            return None
        families = QFontDatabase.applicationFontFamilies(font_id)
        if not families:
            return None
        family = families[0]
        self._path_to_family[file_path] = family
        return family

    def _load_custom_font(self, file_path: str, attr: str):
        """加载字体文件并选中到 attr 槽位；路径记入设置（随工程文件往返）。"""
        family = self._apply_custom_font(file_path)
        if family is None:
            InfoBar.warning(
                tr("提示"), tr("无法加载字体文件：{0}").format(file_path), duration=5000,
                parent=self.window(), position=InfoBarPosition.TOP_RIGHT,
            )
            return
        s = self._s
        setattr(s.display, attr, family)
        if file_path not in s.display.custom_font_paths:
            s.display.custom_font_paths = s.display.custom_font_paths + [file_path]

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
        self._refresh_all_font_combos()
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
