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

    logger.info("正在创建主窗口...")
    ctx = AppContext()
    window = MainWindow(ctx)
    window.show()
    logger.info("主窗口已显示")

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
