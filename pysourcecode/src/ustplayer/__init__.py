# ustplayer 包 — ustPlayer
"""中文的、面向 UTAU/UST 工程文件的可视化播放器。

入口: ustplayer.app.main（`uv run main.py` 或 console script `ustplayer`）
"""

from ustplayer.core.contracts import APP_NAME, APP_VERSION

__version__ = APP_VERSION
__app_name__ = APP_NAME

__all__ = ["__version__", "__app_name__"]
