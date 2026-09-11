# context.py — 应用上下文（统一接口门面）
"""组装所有核心服务，模块间通过 AppContext 互相调用，避免直接依赖具体实现。

依赖方向：UI 层 → AppContext（唯一入口）→ core 实现。
main.py/app.py 只负责创建 AppContext 与主窗口。
"""

from ustplayer.core.contracts import (
    PlayerLauncher,
    ProjectIO,
    UstParser,
    VideoExporter as VideoExporterProtocol,
)
from ustplayer.core.player import NotePlayerLauncher
from ustplayer.core.settings_manager import SettingsManager
from ustplayer.core.ustreader import UstFileReader
from ustplayer.core.uplr_io import UplrProjectIO
from ustplayer.core.video_exporter import VideoExporter


class AppContext:
    """应用服务容器 — 所有模块的唯一组装点。

    UI 层只依赖本上下文暴露的实例与接口（contracts.Protocol），不直接 import core 实现。
    """

    def __init__(self):
        self.settings = SettingsManager()
        self.parser: UstParser = UstFileReader()
        self.player: PlayerLauncher = NotePlayerLauncher()
        self.project_io: ProjectIO = UplrProjectIO(self.settings)
        self.video_exporter: VideoExporterProtocol = VideoExporter(
            self.settings, self.parser, self.project_io
        )
