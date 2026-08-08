# log.py — 日志配置
"""基于 loguru 的全局日志。

用法:
    from core.log import logger

    logger.info("正常信息")
    logger.debug("调试信息")
    logger.exception("自动附完整堆栈")
"""

import sys
import os

from loguru import logger

logger.remove()

_log_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

logger.add(
    os.path.join(_log_dir, "ustPlayer.log"),
    level="DEBUG",
    rotation="1 MB",
    retention="7 days",
)

logger.add(sys.stdout, level="INFO", colorize=True)
