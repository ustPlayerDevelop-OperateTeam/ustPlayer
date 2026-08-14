# main_window.py — 主窗口
"""主窗口：侧边导航 + 堆叠页面，持有 AppContext 并编排播放流程。"""

import os
import sys
import winreg

from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QIcon, QColor
from PySide6.QtWidgets import QApplication

from qfluentwidgets import (
    FluentWindow, NavigationItemPosition, FluentIcon,
    InfoBar, InfoBarPosition, MessageBox, setTheme, Theme, setThemeColor,
)

from ustplayer.context import AppContext
from ustplayer.core.contracts import APP_NAME, PlayerLaunchParams
from ustplayer.core.log import logger

from ustplayer.ui.basic_page import BasicPage
from ustplayer.ui.file_page import FilePage
from ustplayer.ui.player_style_page import PlayerStylePage
from ustplayer.ui.lyric_page import LyricPage
from ustplayer.ui.other_page import OtherPage


class MainWindow(FluentWindow):
    """主窗口 — 侧边导航 + 堆叠页面。"""

    def __init__(self, ctx: AppContext):
        super().__init__()
        self._ctx = ctx
        self._settings = ctx.settings
        self._player_window = None
        self.setWindowTitle(APP_NAME)
        self.resize(900, 620)

        # 主题必须在 _build_pages 之前设置
        self._setup_theme()
        self._setup_accent_color()

        icon_path = os.path.join(self._settings.program_root, "icon.ico")
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))

        self._build_pages()
        self._init_navigation()
        self.basic_page.set_play_callback(self._on_play)

        QTimer.singleShot(100, self._load_dropped_uplr)

    # ===================== 主题管理 =====================

    def _setup_theme(self):
        """初始化主题：应用保存的设置，并连接系统/用户主题变化信号。"""
        self._apply_theme()

        app = QApplication.instance()
        if app:
            app.styleHints().colorSchemeChanged.connect(
                self._on_system_theme_changed
            )

        self._settings.theme_mode_changed.connect(
            self._on_theme_mode_changed
        )

    def _apply_theme(self):
        """根据 theme_mode 设置 qfluentwidgets 主题（亮/暗/自动）。"""
        mode = self._settings.theme_mode
        if mode == "auto":
            setTheme(Theme.AUTO)
        elif mode == "light":
            setTheme(Theme.LIGHT)
        elif mode == "dark":
            setTheme(Theme.DARK)
        logger.info(f"主题已应用: {mode}")

    def _on_system_theme_changed(self):
        """系统主题变化 — 仅在'跟随系统'模式下刷新。"""
        if self._settings.theme_mode == "auto":
            setTheme(Theme.AUTO)
            logger.info("系统主题已变化，自动刷新主题")

    def _on_theme_mode_changed(self, mode: str):
        """用户手动切换主题 → 应用并持久化。"""
        logger.info(f"用户切换主题模式: {mode}")
        self._apply_theme()
        self._settings.write_settings()

    # ===================== 强调色管理 =====================

    def _setup_accent_color(self):
        """初始化强调色：从注册表读取 Windows 强调色或使用自定义颜色。"""
        self._last_windows_accent = None
        self._apply_accent_color()

        # 监听用户手动切换强调色模式
        self._settings.accent_color_mode_changed.connect(
            self._on_accent_color_mode_changed
        )
        self._settings.custom_accent_color_changed.connect(
            self._on_custom_accent_color_changed
        )

        # 定时检测 Windows 强调色变化（每 2 秒）
        self._accent_timer = QTimer(self)
        self._accent_timer.timeout.connect(self._check_accent_color)
        self._accent_timer.start(2000)

    @staticmethod
    def _get_windows_accent_color() -> str | None:
        """从注册表读取 Windows 强调色，返回 hex 字符串如 '#0078D7'。"""
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER,
                r"Software\Microsoft\Windows\DWM",
                0, winreg.KEY_READ,
            )
            value, _ = winreg.QueryValueEx(key, "AccentColor")
            winreg.CloseKey(key)
            # ABGR → RGB
            r = value & 0xFF
            g = (value >> 8) & 0xFF
            b = (value >> 16) & 0xFF
            return f"#{r:02X}{g:02X}{b:02X}"
        except Exception:
            return None

    def _apply_accent_color(self):
        """根据 accent_color_mode 应用强调色。"""
        if self._settings.accent_color_mode == "auto":
            color = self._get_windows_accent_color()
            if color:
                self._last_windows_accent = color
                setThemeColor(QColor(color))
                logger.info(f"强调色已应用(系统): {color}")
            elif self._last_windows_accent:
                setThemeColor(QColor(self._last_windows_accent))
            else:
                setThemeColor(QColor(self._settings.custom_accent_color))
                logger.info("无法获取系统强调色，使用默认值")
        else:
            setThemeColor(QColor(self._settings.custom_accent_color))
            logger.info(f"强调色已应用(自定义): {self._settings.custom_accent_color}")

    def _check_accent_color(self):
        """定时检测 Windows 强调色是否变化（仅在 auto 模式下生效）。"""
        if self._settings.accent_color_mode != "auto":
            return
        current = self._get_windows_accent_color()
        if current and current != self._last_windows_accent:
            self._last_windows_accent = current
            setThemeColor(QColor(current))
            logger.info(f"系统强调色已变化: {current}")

    def _on_accent_color_mode_changed(self, mode: str):
        """用户切换强调色模式 → 重新应用并持久化。"""
        logger.info(f"强调色模式切换: {mode}")
        self._apply_accent_color()
        self._settings.write_settings()

    def _on_custom_accent_color_changed(self, color: str):
        """用户更改自定义强调色 → 仅在 custom 模式下生效并持久化。"""
        if self._settings.accent_color_mode == "custom":
            setThemeColor(QColor(color))
            logger.info(f"自定义强调色已更新: {color}")
        self._settings.write_settings()

    # ===================== 页面构建 =====================

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
        self.addSubInterface(
            self.basic_page, FluentIcon.HOME, "基础",
            position=NavigationItemPosition.TOP,
        )
        self.addSubInterface(
            self.file_page, FluentIcon.DOCUMENT, "文件",
            position=NavigationItemPosition.TOP,
        )
        self.addSubInterface(
            self.player_style_page, FluentIcon.PALETTE, "播放器",
            position=NavigationItemPosition.TOP,
        )
        self.addSubInterface(
            self.lyric_page, FluentIcon.MUSIC, "歌词",
            position=NavigationItemPosition.TOP,
        )
        self.addSubInterface(
            self.other_page, FluentIcon.INFO, "其他",
            position=NavigationItemPosition.BOTTOM,
        )

    # ===================== 播放逻辑 =====================

    def _on_play(self):
        ust_path = self._settings.ust_path.strip()
        logger.info(f"Play 按钮点击，UST路径: {ust_path}")

        if not ust_path or not os.path.exists(ust_path):
            logger.warning(f"UST 文件无效: {ust_path}")
            InfoBar.error(
                "ERcode001", "请选择有效的UST文件！",
                5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
            return

        try:
            logger.info(f"开始解析 UST，编码={self._settings.encoding}")
            core_ust_info = self._ctx.parser.parse(
                ust_path, self._settings.encoding
            )
            logger.info(
                f"UST 解析完成 — 版本={core_ust_info.version}, "
                f"BPM={core_ust_info.tempo}, "
                f"音符数={len(core_ust_info.notes)}"
            )

            params = self._settings.build_ust_info(core_ust_info)

            msg = MessageBox("提示",
                             "按下确认后将启动播放器，鼠标单击后按ESC键退出全屏", self)
            if msg.exec():
                self._launch_player(params)

        except UnicodeDecodeError:
            logger.exception("UST 编码错误")
            InfoBar.error(
                "ERcode004", "解析UST文件失败：使用了错误的编码，请切换编码后重试",
                5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
        except Exception as e:
            logger.exception("播放准备失败")
            InfoBar.error(
                "ERcode999", f"播放准备失败：{e}",
                5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )

    def _launch_player(self, params: PlayerLaunchParams):
        """启动播放器并保持引用。"""
        logger.info(
            f"正在启动播放器 — curve_show={params.show.curve_show}, "
            f"bpm={params.show.bpm}, lyric={params.show.lyric}, "
            f"fullscreen={params.style.fullscreen}"
        )
        try:
            self._player_window = self._ctx.player.launch(params)
            logger.info("播放器窗口已显示")
        except Exception:
            logger.exception("播放器启动失败")
            raise

    # ===================== 拖拽 uplr 加载 =====================

    def _load_dropped_uplr(self):
        """处理拖拽到 exe 上的 .uplr 文件（从命令行参数获取）。"""
        if len(sys.argv) <= 1:
            return

        dropped = sys.argv[1].strip()
        if not (dropped and os.path.exists(dropped) and dropped.lower().endswith(".uplr")):
            return

        try:
            self._settings.import_uplr(dropped)
            self._settings.last_open_dir = os.path.dirname(dropped)
            self._settings.write_settings()

            # 各页面已通过 settings 信号实时同步，无需手动刷新

            InfoBar.success(
                "成功", f"已成功打开并加载工程：\n{dropped}",
                3000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
        except Exception as e:
            InfoBar.error(
                "ERcode006", f"加载工程文件失败：\n{e}",
                5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )

    # ===================== 导航切换时同步页面 =====================

    def switchTo(self, interface):
        """覆写父类方法，切换后同步页面数据（信号驱动的兜底）。"""
        super().switchTo(interface)
        if hasattr(interface, "sync_all_from_settings"):
            interface.sync_all_from_settings()
