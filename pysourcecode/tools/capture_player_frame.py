"""抓取 1.1.x 播放器的渲染画面，用于与 2.0 的画面逐项对比。

用法（工作目录必须是 pysourcecode/）：
    uv run python tools/capture_player_frame.py <UST 路径> <输出 PNG 路径> [位置秒数]

做法：离屏构造 NoteLyricDisplay，显式把时间轴推进到指定位置，
再用 QWidget.grab() 取当前绘制结果——不依赖真实播放设备，也不弹全屏窗口。
"""

from __future__ import annotations

import os
import sys

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PySide6.QtCore import QSize  # noqa: E402
from PySide6.QtWidgets import QApplication  # noqa: E402

from ustplayer.core.contracts import (  # noqa: E402
    PlayerLaunchParams,
    PlayerStyle,
    ProjectInfo,
    ShowConfig,
)
from ustplayer.core.player import NoteLyricDisplay  # noqa: E402
from ustplayer.core.ustreader import UstFileReader  # noqa: E402

WIDTH = 1920
HEIGHT = 1080


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    ust_path = sys.argv[1]
    out_path = sys.argv[2]
    seconds = float(sys.argv[3]) if len(sys.argv) > 3 else 0.17

    app = QApplication.instance() or QApplication([])

    ust = UstFileReader().parse(ust_path, encoding="Shift-JIS")

    # 与 2.0 探针 UST 相同的显示开关，保证两边可比
    show = ShowConfig(
        bpm=True,
        play_time=True,
        song_name=True,
        song_author=True,
        ust_author=True,
        lyric=True,
        curve_show=True,   # 打开音高曲线：这是最容易看出差异的一项
        note_name=True,
        ust_lyric=True,
        copyright=True,
    )

    params = PlayerLaunchParams(
        ust=ust,
        show=show,
        project=ProjectInfo(
            project_name="播放器探针",
            song_name="测试曲名",
            song_author="曲师",
            ust_author="调音师",
        ),
        style=PlayerStyle(),
    )

    window = NoteLyricDisplay(params)
    window.resize(QSize(WIDTH, HEIGHT))

    # 把时间轴推进到指定位置后再取图：播放器的绘制状态由 _update_state 决定
    if hasattr(window, "_update_state"):
        window._update_state(seconds)
    elif hasattr(window, "_on_tick"):
        window._on_tick()

    app.processEvents()

    pixmap = window.grab()
    ok = pixmap.save(out_path, "PNG")
    print(f"已保存：{out_path}（{pixmap.width()}x{pixmap.height()}，位置 {seconds} 秒，成功={ok}）")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
