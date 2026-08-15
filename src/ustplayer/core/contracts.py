# contracts.py — 模块间统一接口契约
"""所有模块之间互相调用的数据契约与接口定义。

- 数据类：UstInfo / NoteInfo / PlayerLaunchParams 等，替代裸 dict 在模块间传递；
- 服务接口：UstParser / PlayerLauncher（Protocol），具体实现在 AppContext 中注册；
- 通用工具：颜色校验、程序根目录解析、应用版本常量。
"""

import os
import re
import sys
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, List, Protocol, Tuple

if TYPE_CHECKING:
    from PySide6.QtWidgets import QWidget

# ===================== 应用元信息 =====================

APP_NAME = "ustPlayer"
APP_VERSION = "1.0.0"
APP_BUILD = "v26f19"
APP_AUTHOR = "SYEternal_R"
APP_COPYRIGHT = f"{APP_NAME} - {APP_VERSION} ({APP_BUILD}) by {APP_AUTHOR}"


# ===================== 通用工具 =====================

_HEX_RE = re.compile(r"^#([0-9A-Fa-f]{6})$")


def is_valid_hex_color(hex_color: str) -> bool:
    """判断字符串是否为合法的 #RRGGBB 颜色。"""
    return bool(_HEX_RE.match(str(hex_color).strip()))


def validate_hex_color(hex_color: str, fallback: str = "#FFFFFF") -> str:
    """校验十六进制颜色，无效时返回 fallback。"""
    return hex_color.strip() if is_valid_hex_color(hex_color) else fallback


def hex_to_rgb(hex_color: str) -> Tuple[int, int, int]:
    """#RRGGBB → (R, G, B)，无效时返回白色。"""
    try:
        h = hex_color.lstrip("#")
        return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))
    except Exception:
        return (255, 255, 255)


def resolve_program_root() -> str:
    """解析程序根目录。

    打包后（Nuitka/PyInstaller）为 exe 所在目录；
    开发时为入口脚本（main.py）所在目录。
    """
    if getattr(sys, "frozen", False) or "__compiled__" in globals():
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(os.path.abspath(sys.argv[0]))


def as_bool(value, default: bool = False) -> bool:
    """宽松布尔转换：整数 0/1、bool、字符串 true/yes/on/1。"""
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    return str(value).strip().lower() in ("1", "true", "yes", "on")


# ===================== UST 数据契约 =====================

@dataclass
class NoteInfo:
    """UST 音符数据。"""

    index: str = ""              # 音符段编号，如 "0000"
    length: int = 0              # 音符长度（tick）
    lyric: str = ""              # 歌词
    note_num: int = 0            # MIDI 音高
    phoneme: str = ""            # 音素（UST Phoneme 字段，可为空）
    pitch_bend: List[int] = field(default_factory=list)  # 音高曲线点


@dataclass
class UstInfo:
    """UST 解析结果。"""

    version: str = ""            # 版本号文本，如 "UST Version 1.20"
    tempo: float = 120.0         # 速度 (BPM)
    tracks: int = 1              # 轨道数
    notes: List[NoteInfo] = field(default_factory=list)


# ===================== 播放器参数契约 =====================

@dataclass
class ShowConfig:
    """播放器显示开关。"""

    bpm: bool = True
    play_time: bool = True
    song_name: bool = True
    song_author: bool = True
    ust_author: bool = True
    lyric: bool = True
    curve_show: bool = False


@dataclass
class ProjectInfo:
    """项目信息。"""

    project_name: str = ""
    song_name: str = ""
    song_author: str = ""
    ust_author: str = ""


@dataclass
class PlayerStyle:
    """播放器样式。"""

    bg_color: str = "#000000"
    note_color: str = "#6c6c6c"
    lyric_color: str = "#FFFFFF"
    lyric_text_color: str = "#FFFFFF"
    other_text_color: str = "#FFFFFF"
    lyric_pos: str = "上"
    fullscreen: bool = True
    lrc_path: str = ""
    music_path: str = ""          # 伴奏音频路径（新版 uplr 可打包）
    silent_display: str = "R"
    silent_custom_text: str = ""
    end_display: str = "END"
    end_custom_text: str = ""
    pitch_placeholder: str = "无"
    pitch_custom_text: str = ""
    pitch_curve_color: str = "#FFFFFF"


@dataclass
class PlayerLaunchParams:
    """播放器启动参数 — 统一接口，替代裸 dict。"""

    ust: UstInfo = field(default_factory=UstInfo)
    show: ShowConfig = field(default_factory=ShowConfig)
    project: ProjectInfo = field(default_factory=ProjectInfo)
    style: PlayerStyle = field(default_factory=PlayerStyle)


# ===================== 服务接口（Protocol） =====================

class UstParser(Protocol):
    """UST 解析器接口。"""

    def parse(self, ust_path: str, encoding: str) -> UstInfo:
        """解析 UST 文件。

        Args:
            ust_path: UST 文件路径（.ust）
            encoding: 文件编码

        Raises:
            FileNotFoundError: 文件不存在
            UnicodeDecodeError: 使用了错误的编码
        """
        ...


class PlayerLauncher(Protocol):
    """播放器启动器接口。"""

    def launch(self, params: PlayerLaunchParams) -> "QWidget":
        """启动播放器窗口，返回窗口引用（调用方需保持引用防止 GC）。"""
        ...


class ProjectIO(Protocol):
    """.uplr 工程文件导入/导出接口。"""

    def import_uplr(self, input_file: str) -> None:
        """从 .uplr 工程文件导入全部配置。"""
        ...

    def export_uplr(self, output_file: str) -> None:
        """将全部配置与资源导出为 .uplr 工程文件。"""
        ...
