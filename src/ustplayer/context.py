# context.py — 应用上下文（统一接口门面）
"""组装所有核心服务，模块之间通过 AppContext 互相调用，避免直接依赖具体实现。

依赖方向：
    UI 层 → AppContext（唯一入口）→ core 实现
    main.py/app.py 只负责创建 AppContext 与主窗口。
"""

from ustplayer.core.contracts import PlayerLauncher, ProjectIO, UstParser
from ustplayer.core.player import NotePlayerLauncher
from ustplayer.core.settings_manager import SettingsManager
from ustplayer.core.ustreader import UstFileReader
from ustplayer.core.uplr_io import UplrProjectIO


class AppContext:
    """应用服务容器 — 所有模块的唯一组装点。

    UI 层只依赖本上下文暴露的实例与接口（contracts.Protocol），
    不直接 import core 具体实现。
    """

    def __init__(self):
        # 配置管理（含 Qt 信号；实现设置域接口）
        self.settings = SettingsManager()
        # UST 解析器（实现 UstParser 契约）
        self.parser: UstParser = UstFileReader()
        # 播放器启动器（实现 PlayerLauncher 契约）
        self.player: PlayerLauncher = NotePlayerLauncher()
        # .uplr 工程文件导入/导出（实现 ProjectIO 契约）
        self.project_io: ProjectIO = UplrProjectIO(self.settings)
