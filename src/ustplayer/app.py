# app.py — 应用入口
"""QApplication 创建与主窗口装配。

入口调用链：main.py → ustplayer.app.main() → AppContext + MainWindow。
"""

import os
import sys

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QApplication

from ustplayer.context import AppContext
from ustplayer.core.contracts import APP_NAME
from ustplayer.core.i18n import install_translator
from ustplayer.core.log import logger
from ustplayer.ui.main_window import MainWindow


def main():
    logger.info(f"{APP_NAME} 启动")
    logger.info(f"Python: {sys.version}")
    logger.info(f"工作目录: {os.getcwd()}")
    try:
        from PySide6.QtCore import qVersion
        logger.info(f"Qt 版本: {qVersion()}")
    except Exception:
        pass

    QApplication.setHighDpiScaleFactorRoundingPolicy(
        Qt.HighDpiScaleFactorRoundingPolicy.PassThrough
    )

    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)

    # 设置（含语言偏好）在创建窗口前加载，据此安装界面翻译器
    logger.info("正在加载设置并安装翻译器...")
    ctx = AppContext()
    install_translator(ctx.settings.language.effective_language)
    logger.info(f"界面语言: {ctx.settings.language.effective_language}")

    logger.info("正在创建主窗口...")
    window = MainWindow(ctx)
    window.show()
    logger.info("主窗口已显示")

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
