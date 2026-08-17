# main_window.py — 主窗口
"""主窗口：侧边导航 + 堆叠页面，持有 AppContext 并编排播放流程。"""

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
    """主窗口 — 侧边导航 + 堆叠页面。"""

    def __init__(self, ctx: AppContext):
        super().__init__()
        self._ctx = ctx
        self._settings = ctx.settings
        self._player_window = None
        self._current_interface = None
        self.setWindowTitle(APP_NAME)
        self.resize(900, 620)

        # 主题必须在 _build_pages 之前设置
        self._setup_theme()
        self._setup_accent_color()

        # 窗口背景效果（无 / 亚克力 / Mica，可在「其他」页切换）
        self._settings.theme.window_effect_changed.connect(self._apply_window_effect)
        self._apply_window_effect()

        icon_path = os.path.join(self._settings.program_root, "icon.ico")
        if os.path.exists(icon_path):
            self.setWindowIcon(QIcon(icon_path))

        self._build_pages()
        self._init_navigation()
        self.basic_page.set_play_callback(self._on_play)
        self._current_interface = self.basic_page

        # 语言切换：重装翻译器 → 全窗口重译（页面静态文本 + 导航标题）
        self._settings.language.language_changed.connect(self._on_language_changed)

        # 关闭所有子控件的拖放，让拖拽事件自然冒泡到主窗口统一处理
        # （否则 LineEdit/TextEdit 会吞掉文件拖放，主窗口收不到）
        self.setAcceptDrops(True)
        for widget in self.findChildren(QWidget):
            widget.setAcceptDrops(False)

        QTimer.singleShot(100, self._load_dropped_uplr)

    # ===================== 窗口背景效果 =====================

    def _apply_window_effect(self):
        """按设置应用窗口背景效果：none=纯色, mica=Win11 Mica, acrylic=亚克力模糊。

        亚克力/纯色 Win10、Win11 均可用；Mica 仅 Win11（其余系统自动回退纯色）。
        """
        mode = self._settings.theme.window_effect
        try:
            if mode == "mica":
                self.setMicaEffectEnabled(True)
                if not self.isMicaEffectEnabled():
                    # 当前系统不支持 Mica（如 Win10）：恢复纯色背景
                    self.windowEffect.removeBackgroundEffect(self.winId())
                    self.setBackgroundColor(
                        QColor(32, 32, 32) if isDarkTheme() else QColor(240, 244, 249)
                    )
            elif mode == "acrylic":
                # 先清除已有 Mica/背景效果，再开亚克力，避免叠加
                self.setMicaEffectEnabled(False)
                self.windowEffect.removeBackgroundEffect(self.winId())
                self.windowEffect.setAcrylicEffect(self.winId(), self._acrylic_gradient())
                # 背景全透明，让 DWM 亚克力透出
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
        """亚克力渐变底色（AARRGGBB），随明暗主题变化。"""
        return "1E1E1E99" if isDarkTheme() else "F2F2F230"

    def showEvent(self, e):
        """窗口完全初始化后按设置重设背景效果（需要有效窗口句柄）。"""
        super().showEvent(e)
        self._apply_window_effect()

    def _onThemeChangedFinished(self):
        """主题切换完成后：Mica 由基类重染，亚克力需按新主题换渐变底色。"""
        super()._onThemeChangedFinished()
        if self._settings.theme.window_effect == "acrylic":
            try:
                self.windowEffect.setAcrylicEffect(self.winId(), self._acrylic_gradient())
            except Exception:
                logger.exception("主题切换后重染亚克力失败")

    # ===================== 主题管理 =====================

    def _setup_theme(self):
        """初始化主题：应用保存的设置，并连接系统/用户主题变化信号。"""
        self._apply_theme()

        app = QApplication.instance()
        # instance() 存根返回 QCoreApplication，实际必为 QApplication，isinstance 收窄类型
        if isinstance(app, QApplication):
            app.styleHints().colorSchemeChanged.connect(
                self._on_system_theme_changed
            )

        self._settings.theme.theme_mode_changed.connect(
            self._on_theme_mode_changed
        )

    def _apply_theme(self):
        """根据 theme_mode 设置 qfluentwidgets 主题（亮/暗/自动）。"""
        mode = self._settings.theme.theme_mode
        if mode == "auto":
            setTheme(Theme.AUTO)
        elif mode == "light":
            setTheme(Theme.LIGHT)
        elif mode == "dark":
            setTheme(Theme.DARK)
        logger.info(f"主题已应用: {mode}")

    def _on_system_theme_changed(self):
        """系统主题变化 — 仅在'跟随系统'模式下刷新。"""
        if self._settings.theme.theme_mode == "auto":
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
        self._settings.theme.accent_color_mode_changed.connect(
            self._on_accent_color_mode_changed
        )
        self._settings.theme.custom_accent_color_changed.connect(
            self._on_custom_accent_color_changed
        )

        # 定时检测 Windows 强调色变化（每 2 秒）
        self._accent_timer = QTimer(self)
        self._accent_timer.timeout.connect(self._check_accent_color)
        self._accent_timer.start(2000)

    @staticmethod
    def _get_windows_accent_color() -> str | None:
        """从注册表读取 Windows 强调色，返回 hex 如 '#0078D7'。"""
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER,
                r"Software\Microsoft\Windows\DWM",
                0, winreg.KEY_READ,
            )
            value, _ = winreg.QueryValueEx(key, "AccentColor")
            winreg.CloseKey(key)
            # 注册表存的是 ABGR，需转成 RGB
            r = value & 0xFF
            g = (value >> 8) & 0xFF
            b = (value >> 16) & 0xFF
            return f"#{r:02X}{g:02X}{b:02X}"
        except Exception:
            return None

    def _apply_accent_color(self):
        """根据 accent_color_mode 应用强调色。"""
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
        """定时检测 Windows 强调色是否变化（仅在 auto 模式下生效）。"""
        if self._settings.theme.accent_color_mode != "auto":
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
        if self._settings.theme.accent_color_mode == "custom":
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
        # 保存导航按钮引用（语言切换时重设标题）
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

    # ===================== 语言切换 =====================

    def _on_language_changed(self, _language: str):
        """语言设置变化 → 重装翻译器并重译全窗口（页面静态文本 + 导航标题）。"""
        install_translator(self._settings.language.effective_language)
        for page in (
            self.basic_page, self.file_page, self.player_style_page,
            self.lyric_page, self.other_page,
        ):
            retranslate = getattr(page, "retranslate", None)
            if retranslate is not None:
                retranslate()
        self._retranslate_navigation()

    def _retranslate_navigation(self):
        """按当前语言重设侧边导航标题。"""
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

    # ===================== 播放逻辑 =====================

    def _on_play(self):
        ust_path = self._settings.file.ust_path.strip()
        logger.info(f"Play 按钮点击，UST路径: {ust_path}")

        if not ust_path or not os.path.exists(ust_path):
            logger.warning(f"UST 文件无效: {ust_path}")
            InfoBar.error(
                "ERcode001", tr("请选择有效的UST文件！"),
                5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
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
                5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
        except Exception as e:
            logger.exception("播放准备失败")
            InfoBar.error(
                "ERcode999", tr("播放准备失败：{0}").format(e),
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
        """处理拖拽到 exe 图标上的 .uplr 文件（从命令行参数获取）。"""
        if len(sys.argv) <= 1:
            return

        dropped = sys.argv[1].strip()
        if not (dropped and os.path.exists(dropped) and dropped.lower().endswith(".uplr")):
            return
        self._import_uplr_file(dropped)

    def _import_uplr_file(self, path: str):
        """导入 .uplr 工程文件并提示结果（拖拽到窗口 / exe 共用）。"""
        try:
            self._ctx.project_io.import_uplr(path)
            self._settings.last_open_dir = os.path.dirname(path)
            self._settings.write_settings()

            # 各页面已通过 settings 信号实时同步，无需手动刷新

            InfoBar.success(
                tr("成功"), tr("已成功打开并加载工程：\n{0}").format(path),
                3000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )
        except Exception as e:
            InfoBar.error(
                "ERcode006", tr("加载工程文件失败：\n{0}").format(e),
                5000, parent=self, position=InfoBarPosition.TOP_RIGHT,
            )

    @staticmethod
    def _has_ext_url(mime, ext: str) -> bool:
        """判断拖拽数据里是否包含指定扩展名（如 ".uplr"）的文件。"""
        suffix = ext.lower() if ext.startswith(".") else f".{ext.lower()}"
        return mime.hasUrls() and any(
            url.toLocalFile().lower().endswith(suffix) for url in mime.urls()
        )

    def _accepts_drag(self, mime) -> bool:
        """按当前页面决定接受的拖拽类型：
        - 基础页：.uplr → 导入工程文件；
        - 文件页：.ust → 自动填入 ust 路径；
        - 歌词页：.lrc → 自动填入 LRC 歌词路径；
        - 其余页面：不接受拖拽。
        """
        if self._current_interface is self.basic_page:
            return self._has_ext_url(mime, ".uplr")
        if self._current_interface is self.file_page:
            return self._has_ext_url(mime, ".ust")
        if self._current_interface is self.lyric_page:
            return self._has_ext_url(mime, ".lrc")
        return False

    def dragEnterEvent(self, e):
        """拖入可接受的文件（基础页 .uplr / 文件页 .ust / 歌词页 .lrc）时接受拖动。"""
        if self._accepts_drag(e.mimeData()):
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        """放下文件：基础页 .uplr → 导入工程；文件页 .ust → 填 ust 路径；
        歌词页 .lrc → 填 LRC 歌词路径；其余页不处理。"""
        if self._current_interface is self.basic_page:
            for url in e.mimeData().urls():
                path = url.toLocalFile()
                if path and path.lower().endswith(".uplr") and os.path.exists(path):
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

    # ===================== 导航切换时同步页面 =====================

    def switchTo(self, interface):
        """覆写父类方法，切换后同步页面数据（信号驱动的兜底）。"""
        super().switchTo(interface)
        self._current_interface = interface
        if hasattr(interface, "sync_all_from_settings"):
            cast(Any, interface).sync_all_from_settings()

    # ===================== 关闭保存 =====================

    def closeEvent(self, e):
        """关闭主窗口时把全部设置写入 Settings.json（重启可恢复）。"""
        self._settings.write_settings()
        super().closeEvent(e)
