# main_window.py — 主窗口
"""侧边导航 + 堆叠页面，持上下文并编排播放。"""

import os
import sys
import winreg
from typing import Any, cast

from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QIcon, QColor
from PySide6.QtWidgets import QApplication, QWidget

from qfluentwidgets import (
    FluentWindow, NavigationItemPosition, FluentIcon,
    InfoBar, InfoBarPosition, MessageBox, setTheme, Theme, setThemeColor,
)
from qfluentwidgets.common.style_sheet import isDarkTheme

from ustplayer.context import AppContext
from ustplayer.core.contracts import APP_NAME, PlayerLaunchParams
from ustplayer.core.i18n import install_translator, tr
from ustplayer.core.log import logger

from ustplayer.ui.basic_page import BasicPage
from ustplayer.ui.file_page import FilePage
from ustplayer.ui.player_style_page import PlayerStylePage
from ustplayer.ui.lyric_page import LyricPage
from ustplayer.ui.other_page import OtherPage


class MainWindow(FluentWindow):

    def __init__(self, ctx: AppContext):
        super().__init__()
        self._ctx = ctx
        self._settings = ctx.settings
        self._player_window = None
        self._current_interface = None
        self.setWindowTitle(APP_NAME)
        self.resize(900, 620)

        self._setup_theme()
        self._setup_accent_color()

        self._settings.theme.window_effect_changed.connect(self._apply_window_effect)
        self._apply_window_effect()

        icon_path = os.path.join(self._settings.program_root, "icon.ico")
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))

        self._build_pages()
        self._init_navigation()
        self.basic_page.set_play_callback(self._on_play)
        self._current_interface = self.basic_page

        self._settings.language.language_changed.connect(self._on_language_changed)

        self.setAcceptDrops(True)
        for widget in self.findChildren(QWidget):
            widget.setAcceptDrops(False)

        QTimer.singleShot(100, self._load_dropped_uplr)

    def _apply_window_effect(self):
        mode = self._settings.theme.window_effect
        try:
            if mode == "mica":
                self.setMicaEffectEnabled(True)
                if not self.isMicaEffectEnabled():
                    self.windowEffect.removeBackgroundEffect(self.winId())
                    self.setBackgroundColor(
                        QColor(32, 32, 32) if isDarkTheme() else QColor(240, 244, 249)
                    )
            elif mode == "acrylic":
                self.setMicaEffectEnabled(False)
                self.windowEffect.removeBackgroundEffect(self.winId())
                self.windowEffect.setAcrylicEffect(self.winId(), self._acrylic_gradient())
                self.setBackgroundColor(QColor(0, 0, 0, 0))
            else:
                self.setMicaEffectEnabled(False)
                self.windowEffect.removeBackgroundEffect(self.winId())
                self.setBackgroundColor(
                    QColor(32, 32, 32) if isDarkTheme() else QColor(240, 244, 249)
                )
        except Exception as e:
            logger.warning(f"窗口背景效果应用失败（回退纯色背景）: {e}")

    @staticmethod
    def _acrylic_gradient() -> str:
        return "1E1E1E99" if isDarkTheme() else "F2F2F230"

    def showEvent(self, e):
        super().showEvent(e)
        self._apply_window_effect()

    def _onThemeChangedFinished(self):
        super()._onThemeChangedFinished()
        if self._settings.theme.window_effect == "acrylic":
            try:
                self.windowEffect.setAcrylicEffect(self.winId(), self._acrylic_gradient())
            except Exception:
                logger.exception("主题切换后重染亚克力失败")

    def _setup_theme(self):
        self._apply_theme()
        app = QApplication.instance()
        if isinstance(app, QApplication):
            app.styleHints().colorSchemeChanged.connect(self._on_system_theme_changed)
        self._settings.theme.theme_mode_changed.connect(self._on_theme_mode_changed)

    def _apply_theme(self):
        mode = self._settings.theme.theme_mode
        if mode == "auto":
            setTheme(Theme.AUTO)
        elif mode == "light":
            setTheme(Theme.LIGHT)
        elif mode == "dark":
            setTheme(Theme.DARK)
        logger.info(f"主题已应用: {mode}")

    def _on_system_theme_changed(self):
        if self._settings.theme.theme_mode == "auto":
            setTheme(Theme.AUTO)
            logger.info("系统主题已变化，自动刷新主题")

    def _on_theme_mode_changed(self, mode: str):
        logger.info(f"用户切换主题模式: {mode}")
        self._apply_theme()
        self._settings.write_settings()

    def _setup_accent_color(self):
        self._last_windows_accent = None
        self._apply_accent_color()
        self._settings.theme.accent_color_mode_changed.connect(self._on_accent_color_mode_changed)
        self._settings.theme.custom_accent_color_changed.connect(self._on_custom_accent_color_changed)
        self._accent_timer = QTimer(self)
        self._accent_timer.timeout.connect(self._check_accent_color)
        self._accent_timer.start(2000)

    @staticmethod
    def _get_windows_accent_color() -> str | None:
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER,
                r"Software\Microsoft\Windows\DWM",
                0, winreg.KEY_READ,
            )
            value, _ = winreg.QueryValueEx(key, "AccentColor")
            winreg.CloseKey(key)
            r = value & 0xFF
            g = (value >> 8) & 0xFF
            b = (value >> 16) & 0xFF
            return f"#{r:02X}{g:02X}{b:02X}"
        except Exception:
            return None

    def _apply_accent_color(self):
        theme = self._settings.theme
        if theme.accent_color_mode == "auto":
            color = self._get_windows_accent_color()
            if color:
                self._last_windows_accent = color
                setThemeColor(QColor(color))
                logger.info(f"强调色已应用(系统): {color}")
            elif self._last_windows_accent:
                setThemeColor(QColor(self._last_windows_accent))
            else:
                setThemeColor(QColor(theme.custom_accent_color))
                logger.info("无法获取系统强调色，使用默认值")
        else:
            setThemeColor(QColor(theme.custom_accent_color))
            logger.info(f"强调色已应用(自定义): {theme.custom_accent_color}")

    def _check_accent_color(self):
        if self._settings.theme.accent_color_mode != "auto":
            return
        current = self._get_windows_accent_color()
        if current and current != self._last_windows_accent:
            self._last_windows_accent = current
            setThemeColor(QColor(current))
            logger.info(f"系统强调色已变化: {current}")

    def _on_accent_color_mode_changed(self, mode: str):
        logger.info(f"强调色模式切换: {mode}")
        self._apply_accent_color()
        self._settings.write_settings()

    def _on_custom_accent_color_changed(self, color: str):
        if self._settings.theme.accent_color_mode == "custom":
            setThemeColor(QColor(color))
            logger.info(f"自定义强调色已更新: {color}")
        self._settings.write_settings()

    def _build_pages(self):
        self.basic_page = BasicPage(self._ctx)
        self.basic_page.setObjectName("basic_page")
        self.file_page = FilePage(self._ctx)
        self.file_page.setObjectName("file_page")
        self.player_style_page = PlayerStylePage(self._ctx)
        self.player_style_page.setObjectName("player_style_page")
        self.lyric_page = LyricPage(self._ctx)
        self.lyric_page.setObjectName("lyric_page")
        self.other_page = OtherPage(self._ctx)
        self.other_page.setObjectName("other_page")

    def _init_navigation(self):
        self._nav_items = {}
        self._nav_items["basic"] = self.addSubInterface(
            self.basic_page, FluentIcon.HOME, tr("基础"),
            position=NavigationItemPosition.TOP,
        )
        self._nav_items["file"] = self.addSubInterface(
            self.file_page, FluentIcon.DOCUMENT, tr("文件"),
            position=NavigationItemPosition.TOP,
        )
        self._nav_items["player_style"] = self.addSubInterface(
            self.player_style_page, FluentIcon.PALETTE, tr("播放器"),
            position=NavigationItemPosition.TOP,
        )
        self._nav_items["lyric"] = self.addSubInterface(
            self.lyric_page, FluentIcon.MUSIC, tr("歌词"),
            position=NavigationItemPosition.TOP,
        )
        self._nav_items["other"] = self.addSubInterface(
            self.other_page, FluentIcon.INFO, tr("其他"),
            position=NavigationItemPosition.BOTTOM,
        )

    def _on_language_changed(self, _language: str):
        install_translator(self._settings.language.effective_language)
        for page in (
            self.basic_page, self.file_page, self.player_style_page,
            self.lyric_page, self.other_page,
        ):
            fn = getattr(page, "retranslate", None)
            if fn is not None:
                fn()
        self._retranslate_navigation()

    def _retranslate_navigation(self):
        titles = {
            "basic": tr("基础"),
            "file": tr("文件"),
            "player_style": tr("播放器"),
            "lyric": tr("歌词"),
            "other": tr("其他"),
        }
        for key, btn in self._nav_items.items():
            if btn is not None:
                btn.setText(titles[key])

    def _on_play(self):
        ust_path = self._settings.file.ust_path.strip()
        logger.info(f"Play 按钮点击，UST路径: {ust_path}")

        if not ust_path or not os.path.exists(ust_path):
            logger.warning(f"UST 文件无效: {ust_path}")
            InfoBar.error(
                "ERcode001", tr("请选择有效的UST文件！"),
                duration=5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
            return

        try:
            logger.info(f"开始解析 UST，编码={self._settings.file.encoding}")
            core_ust_info = self._ctx.parser.parse(
                ust_path, self._settings.file.encoding
            )
            logger.info(
                f"UST 解析完成 — 版本={core_ust_info.version}, "
                f"BPM={core_ust_info.tempo}, "
                f"音符数={len(core_ust_info.notes)}"
            )

            params = self._settings.build_ust_info(core_ust_info)

            msg = MessageBox(
                tr("提示"),
                tr("按下确认后将启动播放器，鼠标单击后按ESC键退出全屏"), self,
            )
            if msg.exec():
                self._launch_player(params)

        except UnicodeDecodeError:
            logger.exception("UST 编码错误")
            InfoBar.error(
                "ERcode004", tr("解析UST文件失败：使用了错误的编码，请切换编码后重试"),
                duration=5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
        except Exception as e:
            logger.exception("播放准备失败")
            InfoBar.error(
                "ERcode999", tr("播放准备失败：{0}").format(e),
                duration=5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )

    def _launch_player(self, params: PlayerLaunchParams):
        logger.info(
            f"正在启动播放器 — curve_show={params.show.curve_show}, "
            f"bpm={params.show.bpm}, lyric={params.show.lyric}, "
            f"fullscreen={params.style.fullscreen}"
        )
        try:
            self._player_window = self._ctx.player.launch(params)
            logger.info("播放器窗口已显示")
        except Exception as e:
            logger.exception("播放器启动失败")
            InfoBar.error(
                "ERcode005", tr("播放器启动失败：{0}").format(e),
                duration=5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )

    def _load_dropped_uplr(self):
        if len(sys.argv) <= 1:
            return
        dropped = sys.argv[1].strip()
        if not (dropped and os.path.exists(dropped) and dropped.lower().endswith((".uplr", ".uprd"))):
            return
        self._import_uplr_file(dropped)

    def _import_uplr_file(self, path: str):
        try:
            self._ctx.project_io.import_uplr(path)
            self._settings.last_open_dir = os.path.dirname(path)
            self._settings.write_settings()
            InfoBar.success(
                tr("成功"), tr("已成功打开并加载工程：\n{0}").format(path),
                duration=3000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
        except Exception as e:
            InfoBar.error(
                "ERcode006", tr("加载工程文件失败：\n{0}").format(e),
                duration=5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )

    @staticmethod
    def _has_ext_url(mime, ext: str) -> bool:
        suffix = ext.lower() if ext.startswith(".") else f".{ext.lower()}"
        return mime.hasUrls() and any(
            url.toLocalFile().lower().endswith(suffix) for url in mime.urls()
        )

    def _accepts_drag(self, mime) -> bool:
        if self._current_interface is self.basic_page:
            return self._has_ext_url(mime, ".uplr") or self._has_ext_url(mime, ".uprd")
        if self._current_interface is self.file_page:
            return self._has_ext_url(mime, ".ust")
        if self._current_interface is self.lyric_page:
            return self._has_ext_url(mime, ".lrc")
        return False

    def dragEnterEvent(self, e):
        if self._accepts_drag(e.mimeData()):
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        if self._current_interface is self.basic_page:
            for url in e.mimeData().urls():
                path = url.toLocalFile()
                if path and path.lower().endswith((".uplr", ".uprd")) and os.path.exists(path):
                    self._import_uplr_file(path)
                    e.acceptProposedAction()
                    return
        elif self._current_interface is self.file_page:
            for url in e.mimeData().urls():
                path = url.toLocalFile()
                if path and path.lower().endswith(".ust") and os.path.exists(path):
                    self._settings.file.ust_path = path
                    e.acceptProposedAction()
                    return
        elif self._current_interface is self.lyric_page:
            for url in e.mimeData().urls():
                path = url.toLocalFile()
                if path and path.lower().endswith(".lrc") and os.path.exists(path):
                    self._settings.player.lrc_path = path
                    e.acceptProposedAction()
                    return
        super().dropEvent(e)

    def switchTo(self, interface):
        super().switchTo(interface)
        self._current_interface = interface
        if hasattr(interface, "sync_all_from_settings"):
            cast(Any, interface).sync_all_from_settings()

    def closeEvent(self, e):
        self._settings.write_settings()
        super().closeEvent(e)
