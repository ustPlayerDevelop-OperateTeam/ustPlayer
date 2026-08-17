# other_page.py — "其他" 导航页
"""版权信息、外部工具、使用协议入口、主题/强调色/窗口效果设置。"""

import subprocess
import webbrowser
from typing import Optional

from PySide6.QtWidgets import QWidget, QHBoxLayout
from PySide6.QtGui import QColor

from qfluentwidgets import (
    PushButton, BodyLabel,
    ComboBox, ColorPickerButton, HyperlinkButton,
    InfoBar, InfoBarPosition,
)

from ustplayer.context import AppContext
from ustplayer.core.contracts import APP_AUTHOR, APP_NAME, APP_VERSION
from ustplayer.core.i18n import SUPPORTED_LANGUAGES, tr
from ustplayer.ui.section_card import ScrollPage, SectionCard


class OtherPage(ScrollPage):
    """其他标签页 — 关于软件 / 工具 / 协议 / 主题与强调色。"""

    def __init__(self, ctx: AppContext, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._ctx = ctx
        self._s = ctx.settings
        self._setup_ui()
        self._connect_signals()

    # ===================== 枚举 key ↔ 文案 =====================

    @staticmethod
    def _window_effect_text(key: str) -> str:
        return tr({
            "none": tr("关闭"), "acrylic": tr("亚克力"), "mica": tr("Mica"),
        }.get(key, tr("Mica")))

    @staticmethod
    def _theme_combo_text(mode: str) -> str:
        return tr({
            "auto": tr("跟随系统"), "light": tr("亮色"), "dark": tr("暗色"),
        }.get(mode, tr("跟随系统")))

    @staticmethod
    def _accent_mode_text(mode: str) -> str:
        return tr({
            "auto": tr("跟随系统"), "custom": tr("自定义"),
        }.get(mode, tr("跟随系统")))

    @staticmethod
    def _fill_combo(combo: ComboBox, texts: dict):
        """按「key → 显示文本」填充下拉框：itemData 存 key。"""
        for key, text in texts.items():
            combo.addItem(text, userData=key)

    @staticmethod
    def _set_combo_by_key(combo: ComboBox, key: str):
        idx = combo.findData(key)
        combo.setCurrentIndex(idx if idx >= 0 else 0)

    @staticmethod
    def _language_options() -> dict:
        """语言下拉框选项：跟随系统 + 全部受支持语言（key → 显示名）。"""
        options = {"system": tr("跟随系统")}
        options.update(SUPPORTED_LANGUAGES)
        return options

    # ===================== UI 构建 =====================

    def _setup_ui(self):
        layout = self.page_layout

        # ---- 关于软件卡片 ----
        self.card_about = SectionCard(tr("关于软件"))

        # 版权信息（可点击，用带 clicked 信号的 HyperlinkButton 而非覆写事件）
        copyright_btn = HyperlinkButton("", f"{APP_NAME} - {APP_VERSION} by {APP_AUTHOR}", self)
        copyright_btn.setToolTip(tr("点击访问 Bilibili 主页"))
        copyright_btn.clicked.connect(
            lambda: self._open_url("https://space.bilibili.com/661930756")
        )
        self.card_about.addWidget(copyright_btn)
        layout.addWidget(self.card_about)

        # ---- 外部工具与纠错卡片 ----
        self.card_tools = SectionCard(tr("外部工具与纠错"))

        tool_row = QHBoxLayout()
        tool_row.setSpacing(12)

        uf_btn = PushButton("UtaFormatix")
        uf_btn.clicked.connect(lambda: self._open_url("https://utaformatix.tk/"))
        tool_row.addWidget(uf_btn)

        self.er_btn = PushButton(tr("ERcodes纠错"))
        self.er_btn.clicked.connect(self._open_ercode)
        tool_row.addWidget(self.er_btn)
        tool_row.addStretch()
        self.card_tools.addLayout(tool_row)
        layout.addWidget(self.card_tools)

        # ---- 主题卡片（含窗口背景效果） ----
        self.card_theme = SectionCard(tr("主题"))

        theme_row = QHBoxLayout()
        theme_row.setSpacing(8)
        self.theme_lbl = BodyLabel(tr("应用主题:"))
        theme_row.addWidget(self.theme_lbl)
        self.theme_combo = ComboBox()
        self._fill_combo(self.theme_combo, {
            "auto": tr("跟随系统"), "light": tr("亮色"), "dark": tr("暗色"),
        })
        theme_row.addWidget(self.theme_combo)
        theme_row.addStretch()
        self.card_theme.addLayout(theme_row)

        accent_mode_row = QHBoxLayout()
        accent_mode_row.setSpacing(8)
        self.accent_mode_lbl = BodyLabel(tr("强调色:"))
        accent_mode_row.addWidget(self.accent_mode_lbl)
        self.accent_color_mode_combo = ComboBox()
        self._fill_combo(self.accent_color_mode_combo, {
            "auto": tr("跟随系统"), "custom": tr("自定义"),
        })
        accent_mode_row.addWidget(self.accent_color_mode_combo)
        accent_mode_row.addStretch()
        self.card_theme.addLayout(accent_mode_row)

        accent_custom_row = QHBoxLayout()
        accent_custom_row.setSpacing(8)
        self.accent_custom_label = BodyLabel(tr("自定义颜色:"))
        accent_custom_row.addWidget(self.accent_custom_label)
        self.accent_color_picker = ColorPickerButton(
            QColor(self._s.theme.custom_accent_color), tr("选择强调色"), self
        )
        accent_custom_row.addWidget(self.accent_color_picker)
        accent_custom_row.addStretch()
        self.card_theme.addLayout(accent_custom_row)

        # 窗口背景效果（无 / 亚克力 / Mica，可任意切换）
        effect_row = QHBoxLayout()
        effect_row.setSpacing(8)
        self.effect_lbl = BodyLabel(tr("窗口效果:"))
        effect_row.addWidget(self.effect_lbl)
        self.window_effect_combo = ComboBox()
        self._fill_combo(self.window_effect_combo, {
            "none": self._window_effect_text("none"),
            "acrylic": self._window_effect_text("acrylic"),
            "mica": self._window_effect_text("mica"),
        })
        effect_row.addWidget(self.window_effect_combo)
        effect_row.addStretch()
        self.card_theme.addLayout(effect_row)
        layout.addWidget(self.card_theme)

        # ---- 语言卡片 ----
        self.card_lang = SectionCard(tr("语言 / Language"))

        lang_row = QHBoxLayout()
        lang_row.setSpacing(8)
        self.lang_lbl = BodyLabel(tr("界面语言:"))
        lang_row.addWidget(self.lang_lbl)
        self.lang_combo = ComboBox()
        self._fill_combo(self.lang_combo, self._language_options())
        lang_row.addWidget(self.lang_combo)
        lang_row.addStretch()
        self.card_lang.addLayout(lang_row)
        layout.addWidget(self.card_lang)

        # ---- 协议与许可卡片 ----
        self.card_lic = SectionCard(tr("协议与许可"))

        lic_row = QHBoxLayout()
        lic_row.setSpacing(12)

        self.terms_btn = PushButton(tr("开源协议"))
        self.terms_btn.clicked.connect(self._open_terms)
        lic_row.addWidget(self.terms_btn)

        self.gh_btn = PushButton(tr("GitHub仓库"))
        self.gh_btn.clicked.connect(
            lambda: self._open_url("https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer")
        )
        lic_row.addWidget(self.gh_btn)
        lic_row.addStretch()
        self.card_lic.addLayout(lic_row)
        layout.addWidget(self.card_lic)

        # 彩蛋
        self.easter = BodyLabel(tr("你知道吗：alpha版本在提交至托管时曾被错误地命名为ustPlyaer。orz"))
        self.easter.setWordWrap(True)
        layout.addWidget(self.easter)

        layout.addStretch()

    # ===================== 重译（语言切换时调用） =====================

    def retranslate(self):
        """语言切换后重设全部静态文本（下拉框按 key 重填，选中项保持不变）。"""
        self.card_about.setTitle(tr("关于软件"))
        self.card_tools.setTitle(tr("外部工具与纠错"))
        self.er_btn.setText(tr("ERcodes纠错"))
        self.card_theme.setTitle(tr("主题"))
        self.theme_lbl.setText(tr("应用主题:"))
        self._refill_combo(self.theme_combo, {
            "auto": tr("跟随系统"), "light": tr("亮色"), "dark": tr("暗色"),
        })
        self.accent_mode_lbl.setText(tr("强调色:"))
        self._refill_combo(self.accent_color_mode_combo, {
            "auto": tr("跟随系统"), "custom": tr("自定义"),
        })
        self.accent_custom_label.setText(tr("自定义颜色:"))
        self.accent_color_picker.setText(tr("选择强调色"))
        self.effect_lbl.setText(tr("窗口效果:"))
        self._refill_combo(self.window_effect_combo, {
            "none": self._window_effect_text("none"),
            "acrylic": self._window_effect_text("acrylic"),
            "mica": self._window_effect_text("mica"),
        })
        self.card_lang.setTitle(tr("语言 / Language"))
        self.lang_lbl.setText(tr("界面语言:"))
        self._refill_combo(self.lang_combo, self._language_options())
        self.card_lic.setTitle(tr("协议与许可"))
        self.terms_btn.setText(tr("开源协议"))
        self.gh_btn.setText(tr("GitHub仓库"))
        self.easter.setText(tr("你知道吗：alpha版本在提交至托管时曾被错误地命名为ustPlyaer。orz"))

    def _refill_combo(self, combo: ComboBox, texts: dict):
        """重填下拉框（保留选中 key）。"""
        current_key = combo.currentData()
        combo.blockSignals(True)
        combo.clear()
        self._fill_combo(combo, texts)
        idx = combo.findData(current_key)
        combo.setCurrentIndex(idx if idx >= 0 else 0)
        combo.blockSignals(False)

    # ===================== 信号绑定 =====================

    def _connect_signals(self):
        s = self._s

        # 主题下拉框（itemData 存 key）
        self._set_combo_by_key(self.theme_combo, s.theme.theme_mode)
        self.theme_combo.currentIndexChanged.connect(self._on_theme_combo_changed)
        s.theme.theme_mode_changed.connect(self._on_settings_theme_mode_changed)

        # 强调色模式下拉框
        self._set_combo_by_key(self.accent_color_mode_combo, s.theme.accent_color_mode)
        self.accent_color_mode_combo.currentIndexChanged.connect(
            self._on_accent_color_mode_combo_changed
        )
        s.theme.accent_color_mode_changed.connect(self._on_settings_accent_mode_changed)

        # 自定义颜色选择器
        self.accent_color_picker.setColor(QColor(s.theme.custom_accent_color))
        self.accent_color_picker.colorChanged.connect(self._on_accent_color_pick)
        s.theme.custom_accent_color_changed.connect(self._on_settings_accent_color_changed)

        # 窗口背景效果下拉框
        self._set_combo_by_key(self.window_effect_combo, s.theme.window_effect)
        self.window_effect_combo.currentIndexChanged.connect(self._on_window_effect_changed)
        s.theme.window_effect_changed.connect(self._on_settings_window_effect_changed)

        # 语言下拉框（设置值 "system" / 语言代码，主窗口监听信号触发重译）
        self._set_combo_by_key(self.lang_combo, s.language.language)
        self.lang_combo.currentIndexChanged.connect(self._on_language_changed)
        s.language.language_changed.connect(self._on_settings_language_changed)

        # 初始时根据模式显示/隐藏自定义颜色选择器
        self._update_accent_custom_visible(s.theme.accent_color_mode)

    # ===================== 业务逻辑 =====================

    def _on_theme_combo_changed(self, _index: int):
        """主题下拉框变化 → 更新 settings.theme_mode。"""
        setattr(self._s.theme, "theme_mode", self.theme_combo.currentData())

    def _on_accent_color_mode_combo_changed(self, _index: int):
        """强调色模式变化 → 更新 settings。"""
        mode = self.accent_color_mode_combo.currentData()
        setattr(self._s.theme, "accent_color_mode", mode)
        self._update_accent_custom_visible(mode)

    def _on_accent_color_pick(self, color: QColor):
        """自定义颜色选择 → 更新 settings。"""
        setattr(self._s.theme, "custom_accent_color", color.name())

    def _on_window_effect_changed(self, _index: int):
        """窗口效果变化 → 更新 settings（主窗口监听信号实时应用）。"""
        setattr(self._s.theme, "window_effect", self.window_effect_combo.currentData())

    def _on_language_changed(self, _index: int):
        """语言下拉框变化 → 写入 settings（主窗口监听 language_changed 触发全局重译）。"""
        setattr(self._s.language, "language", self.lang_combo.currentData())
        self._s.write_settings()

    def _on_settings_language_changed(self, v: str):
        """settings 端语言变化 → 同步下拉框。"""
        self.lang_combo.blockSignals(True)
        self._set_combo_by_key(self.lang_combo, v)
        self.lang_combo.blockSignals(False)

    def _update_accent_custom_visible(self, mode: str):
        """自定义模式下显示「自定义颜色」整行（标签 + 取色器），跟随系统时整行隐藏。"""
        visible = mode == "custom"
        self.accent_custom_label.setVisible(visible)
        self.accent_color_picker.setVisible(visible)

    def _on_settings_theme_mode_changed(self, v: str):
        """settings 端主题模式变化 → 同步下拉框（避免 lambda GC 问题）。"""
        self.theme_combo.blockSignals(True)
        self._set_combo_by_key(self.theme_combo, v)
        self.theme_combo.blockSignals(False)

    def _on_settings_accent_mode_changed(self, v: str):
        """settings 端强调色模式变化 → 同步下拉框。"""
        self.accent_color_mode_combo.blockSignals(True)
        self._set_combo_by_key(self.accent_color_mode_combo, v)
        self.accent_color_mode_combo.blockSignals(False)
        self._update_accent_custom_visible(v)

    def _on_settings_accent_color_changed(self, v: str):
        """settings 端自定义强调色变化 → 同步取色器。"""
        self.accent_color_picker.blockSignals(True)
        self.accent_color_picker.setColor(QColor(v))
        self.accent_color_picker.blockSignals(False)

    def _on_settings_window_effect_changed(self, v: str):
        """settings 端窗口效果变化 → 同步下拉框。"""
        self.window_effect_combo.blockSignals(True)
        self._set_combo_by_key(self.window_effect_combo, v)
        self.window_effect_combo.blockSignals(False)

    # ===================== 工具方法 =====================

    def _open_url(self, url: str):
        try:
            webbrowser.open(url, new=2)
        except Exception as e:
            InfoBar.error("ERcode003", tr("打开网页失败：{0}").format(e), 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    @staticmethod
    def _open_with_notepad(path: str):
        """用记事本打开文本文件（无需 shell 中转，避免路径特殊字符被二次解析）。"""
        return subprocess.Popen(
            ["notepad.exe", path],
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP,
        )

    def _open_ercode(self):
        try:
            self._open_with_notepad(self._s.ercode_file_path)
        except Exception as e:
            InfoBar.error("ERcode008", tr("打开ERcode.txt失败：{0}").format(e), 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    def _open_terms(self):
        try:
            self._open_with_notepad(self._s.terms_file_path)
        except Exception as e:
            InfoBar.error("ERcode009", tr("打开LICENSE失败：{0}").format(e), 5000,
                          parent=self.window(), position=InfoBarPosition.TOP_RIGHT)

    # ===================== 同步 =====================

    def _sync_ui_from_settings(self):
        """从 settings 同步所有 UI 控件。"""
        s = self._s
        self._set_combo_by_key(self.theme_combo, s.theme.theme_mode)
        self._set_combo_by_key(self.accent_color_mode_combo, s.theme.accent_color_mode)
        self.accent_color_picker.setColor(QColor(s.theme.custom_accent_color))
        self._set_combo_by_key(self.window_effect_combo, s.theme.window_effect)
        self._set_combo_by_key(self.lang_combo, s.language.language)
        self._update_accent_custom_visible(s.theme.accent_color_mode)

    def sync_all_from_settings(self):
        """导入 uplr 或导航切换后同步 UI（信号驱动的兜底）。"""
        self._sync_ui_from_settings()
