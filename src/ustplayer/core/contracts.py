# contracts.py — 模块间统一接口契约
"""模块间互相调用的数据契约与接口定义。

- 数据类：UstInfo / NoteInfo / PlayerLaunchParams 等；
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

APP_NAME = "ustPlayer"
APP_VERSION = "1.1.0 Beta 2"
APP_AUTHOR = "SYEternal_R"
APP_COPYRIGHT = f"Presented with {APP_NAME} - {APP_VERSION}"

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
    """解析程序根目录：打包后为 exe 目录，开发时为入口脚本所在目录。"""
    if getattr(sys, "frozen", False) or "__compiled__" in globals():
        return os.path.dirname(os.path.abspath(sys.executable))
    return os.path.dirname(os.path.abspath(sys.argv[0]))


def ensure_writable_dir(directory: str) -> bool:
    """探测目录是否实际可写：确保目录存在，并真实写入一个临时探针文件验证。

    Windows 的 os.access(W_OK) 只检查只读属性、不检查 ACL，
    对 Program Files 等受限目录会误报“可写”，因此用真实写探针验证。
    """
    try:
        os.makedirs(directory, exist_ok=True)
    except OSError:
        return False
    probe = os.path.join(directory, f".ustplayer_probe_{os.getpid()}.tmp")
    try:
        with open(probe, "w", encoding="utf-8") as f:
            f.write("")
        return True
    except OSError:
        return False
    finally:
        try:
            if os.path.exists(probe):
                os.remove(probe)
        except OSError:
            pass


def as_bool(value, default: bool = False) -> bool:
    """宽松布尔转换：支持 0/1、bool、字符串 true/yes/on/1。"""
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    return str(value).strip().lower() in ("1", "true", "yes", "on")


@dataclass
class NoteInfo:
    """UST 音符数据。"""

    index: str = ""          # 段编号，如 "0000"
    length: int = 0          # 长度（tick）
    lyric: str = ""
    note_num: int = 0        # MIDI 音高
    phoneme: str = ""        # 音素，可为空
    pitch_bend: List[int] = field(default_factory=list)


@dataclass
class UstInfo:
    """UST 解析结果。"""

    version: str = ""
    tempo: float = 120.0     # BPM
    tracks: int = 1
    notes: List[NoteInfo] = field(default_factory=list)


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
    lyric_pos: str = "top"
    fullscreen: bool = True
    lrc_path: str = ""
    music_path: str = ""
    silent_display: str = "r"
    silent_custom_text: str = ""
    end_display: str = "end"
    end_custom_text: str = ""
    pitch_placeholder: str = "none"
    pitch_custom_text: str = ""
    pitch_curve_color: str = "#FFFFFF"


@dataclass
class PlayerLaunchParams:
    """播放器启动参数 — 统一接口。"""

    ust: UstInfo = field(default_factory=UstInfo)
    show: ShowConfig = field(default_factory=ShowConfig)
    project: ProjectInfo = field(default_factory=ProjectInfo)
    style: PlayerStyle = field(default_factory=PlayerStyle)


class UstParser(Protocol):
    """UST 解析器接口。"""

    def parse(self, ust_path: str, encoding: str) -> UstInfo:
        """解析 UST 文件。

        Args:
            ust_path: .ust 文件路径
            encoding: 文件编码

        Raises:
            FileNotFoundError: 文件不存在
            UnicodeDecodeError: 编码错误
        """
        ...


class PlayerLauncher(Protocol):
    """播放器启动器接口。"""

    def launch(self, params: PlayerLaunchParams) -> "QWidget":
        """启动播放器窗口，返回引用（调用方需保持引用防止 GC）。"""
        ...


class ProjectIO(Protocol):
    """.uplr 工程文件导入/导出接口。"""

    def import_uplr(self, input_file: str) -> None:
        """从 .uplr 工程文件导入全部配置。"""
        ...

    def export_uplr(self, output_file: str) -> None:
        """将全部配置与资源导出为 .uplr 工程文件。"""
        ...

    def export_uprd(self, output_file: str, video: dict) -> None:
        """将全部配置与资源 + 视频参数导出为 .uprd 工程文件。"""
        ...

    def cache_base(self) -> str:
        """工程缓存根目录（一般在程序目录下 cache/）。"""
        ...

    def cache_usage(self) -> int:
        """统计工程缓存占用字节数。"""
        ...

    def clear_cache(self) -> None:
        """清空工程缓存。"""
        ...


class VideoExporter(Protocol):
    """视频导出接口（封装 uPlRender 渲染器 DLL）。"""

    def render(
        self,
        output_path: str,
        width: int,
        height: int,
        fps: int,
        mux_audio: bool,
        progress_cb=None,
        cancel_check=None,
    ) -> str:
        """把当前工程渲染为 MP4 视频，并写入对应的 .uprd 工程文件。

        Args:
            output_path: MP4 输出路径（以 .mp4 结尾）。
            width: 画面宽（像素）。
            height: 画面高（像素）。
            fps: 帧率。
            mux_audio: 是否把伴奏（music_path）混入视频。
            progress_cb: 可选进度回调 progress_cb(千分比 0..1000)。
            cancel_check: 可选取消回调，返回 True 时提前终止（抛 RuntimeError）。

        Returns:
            写入的 .uprd 工程文件路径。

        Raises:
            FileNotFoundError: UST 文件缺失。
            RuntimeError: 渲染器 DLL 缺失 / 编码失败 / ffmpeg 失败 / 用户取消。
        """
        ...
