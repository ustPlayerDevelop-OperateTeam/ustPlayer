# log.py — 日志配置
"""基于 loguru 的全局日志。

日志文件优先写入程序根目录（与 Settings.ini 一致）；
若根目录不可写（如安装于只读目录），回退到 %LOCALAPPDATA%\\ustPlayer。

用法:
    from ustplayer.core.log import logger

    logger.info("正常信息")
    logger.debug("调试信息")
    logger.exception("自动附完整堆栈")
"""

import os
import sys

from loguru import logger

from ustplayer.core.contracts import resolve_program_root

logger.remove()


def _resolve_log_dir() -> str:
    """确定日志目录：程序根目录优先，不可写时回退用户数据目录。"""
    root = resolve_program_root()
    if os.access(root, os.W_OK):
        return root

    fallback = os.path.join(
        os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ustPlayer"
    )
    try:
        os.makedirs(fallback, exist_ok=True)
    except OSError:
        fallback = os.path.expanduser("~")
    return fallback


_log_dir = _resolve_log_dir()

logger.add(
    os.path.join(_log_dir, "ustPlayer.log"),
    level="DEBUG",
    rotation="1 MB",
    retention="7 days",
)

# 打包后的 GUI 程序无控制台，sys.stdout 为 None，直接 add 会抛 TypeError
if sys.stdout is not None:
    logger.add(sys.stdout, level="INFO", colorize=True)
