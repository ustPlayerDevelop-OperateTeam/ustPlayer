# video_exporter.py — 视频导出（封装 uPlRender 渲染器）
"""把 UST 工程渲染为 MP4 视频，并同步写入对应的 .uprd 工程文件。

流程：解析 UST → 由当前设置组装渲染配置（RenderConfig JSON）→ 写入 .uprd →
加载 `ustplayer_renderer.dll` 逐帧渲染 → 编码 MP4；可选再把伴奏音乐混入视频。
"""

import dataclasses
import json
import math
import os
import shutil
import subprocess
from typing import Callable, Optional

from ustplayer.core.contracts import APP_VERSION, NoteInfo, ProjectIO, UstInfo, UstParser
from ustplayer.core.log import logger
from ustplayer.core.renderer_ffi import RendererContext, RendererError, RendererLoader
from ustplayer.core.settings_manager import SettingsManager

# 伴奏混入失败时抛出的用户可读前缀，便于 UI 识别（ERcode011）
_AUDIO_MUX_FAILED = "音频混流失败"


class VideoExporter:
    """实现 contracts.VideoExporter —— 通过 uPlRender DLL 渲染当前工程为视频。"""

    # 一拍 = 480 tick（与 ustPlayer / uPlRender 的约定一致）
    _TICKS_PER_QUARTER = 480

    def __init__(self, settings: SettingsManager, parser: UstParser, project_io: ProjectIO):
        self._settings = settings
        self._parser = parser
        self._project_io = project_io

    # ===================== 对外入口 =====================

    def render(
        self,
        output_path: str,
        width: int,
        height: int,
        fps: int,
        mux_audio: bool,
        progress_cb: Optional[Callable[[int], None]] = None,
        cancel_check: Optional[Callable[[], bool]] = None,
    ) -> str:
        """渲染当前工程为 MP4，并写入对应的 .uprd 工程文件。

        Returns:
            写入的 .uprd 工程文件路径。
        """
        output_path = self._ensure_mp4(output_path)
        core_ust = self._parse_ust()

        # 以「音频播完」为结束边界（无音频则按音符 tick 总长）：
        # 音符 tick 结束后、音频仍未播完的区间显示“空拍/静默文字”，音频播完后显示结束文字并停 1 秒。
        end_secs = self._render_end_secs(core_ust)
        render_ust = self._pad_trailing_rest(core_ust, end_secs)
        config = self._build_render_config(render_ust, output_path, width, height, fps)
        total_frames = self._frames_for(end_secs, fps)

        # 1) 写入 .uprd 工程文件（配置 + 资源 + video 段）
        uprd_path = self._uprd_path_for(output_path)
        self._project_io.export_uprd(uprd_path, {"width": width, "height": height, "fps": fps})
        logger.info(f"已写入 .uprd 工程: {uprd_path}")

        # 2) 驱动渲染器 DLL 生成（无声）视频
        self._drive_renderer(config, render_ust, total_frames, progress_cb, cancel_check)

        # 3) 可选：把伴奏音乐混入视频
        if mux_audio and self._music_path:
            self._mux_audio(output_path, self._music_path)

        return uprd_path

    # ===================== 数据准备 =====================

    def _ensure_mp4(self, output_path: str) -> str:
        path = output_path.strip()
        if not path.lower().endswith(".mp4"):
            path += ".mp4"
        return path

    def _parse_ust(self) -> UstInfo:
        ust_path = self._settings.file.ust_path.strip()
        if not ust_path or not os.path.exists(ust_path):
            raise FileNotFoundError(
                f"UST 文件不存在或未选择：{ust_path or '（空）'}"
            )
        return self._parser.parse(ust_path, self._settings.file.encoding)

    @property
    def _music_path(self) -> str:
        return self._settings.project.music_path.strip()

    def _lrc_text(self) -> Optional[str]:
        lrc_path = self._settings.player.lrc_path.strip()
        if not lrc_path or not os.path.exists(lrc_path):
            return None
        for enc in ("utf-8-sig", "utf-8", "gbk", "gb2312", "shift-jis"):
            try:
                with open(lrc_path, "r", encoding=enc) as f:
                    return f.read()
            except (UnicodeDecodeError, OSError):
                continue
        with open(lrc_path, "r", encoding="utf-8", errors="replace") as f:
            return f.read()

    def _build_render_config(
        self, core_ust: UstInfo, output_path: str, width: int, height: int, fps: int
    ) -> dict:
        """把当前设置 + UST 解析结果组装成渲染器需要的 RenderConfig JSON（dict）。"""
        params = self._settings.build_ust_info(core_ust)
        cfg = dataclasses.asdict(params)
        # 渲染器在 style.app_version 里画版权行；此处补充应用版本
        style = dict(cfg.get("style") or {})
        style["app_version"] = APP_VERSION
        cfg["style"] = style
        cfg["width"] = int(width)
        cfg["height"] = int(height)
        cfg["fps"] = int(fps)
        cfg["output_path"] = output_path
        return cfg

    @staticmethod
    def _uprd_path_for(output_path: str) -> str:
        stem, _ = os.path.splitext(output_path)
        return f"{stem}.uprd"

    # ===================== 渲染驱动 =====================

    def _drive_renderer(
        self,
        config: dict,
        render_ust: UstInfo,
        total_frames: int,
        progress_cb: Optional[Callable[[int], None]],
        cancel_check: Optional[Callable[[], bool]],
    ):
        loader = RendererLoader(self._settings.program_root)
        lib = loader.load()
        config_json = json.dumps(config, ensure_ascii=False)
        ust_json = json.dumps(dataclasses.asdict(render_ust), ensure_ascii=False)

        with RendererContext(lib) as ctx:
            ctx.set_config(config_json)
            ctx.set_ust_text(ust_json)
            lrc_text = self._lrc_text()
            if lrc_text:
                ctx.set_lrc_text(lrc_text)

            ctx.begin_export()
            logger.info(f"开始渲染视频: {config['output_path']} (fps={config['fps']}, 总帧={total_frames})")
            for i in range(total_frames):
                if cancel_check and cancel_check():
                    raise RuntimeError("导出已取消")
                elapsed = i / config["fps"]
                ctx.render_frame(elapsed)
                if progress_cb and (i % 5 == 0 or i == total_frames - 1):
                    progress_cb(int((i + 1) / total_frames * 1000) if total_frames else 1000)
            ctx.end_export()

        logger.info("视频渲染完成（无声 MP4）")

    # ===================== 音频驱动的结束边界 =====================

    def _render_end_secs(self, core_ust: UstInfo) -> float:
        """渲染结束边界（秒）：有伴奏时以“音频播完”为准（且不短于音符内容总长），
        无伴奏时按音符 tick 内容总长。"""
        content_secs = VideoExporter._content_secs(core_ust)
        audio_secs = self._audio_duration_secs()
        if audio_secs > 0:
            return max(content_secs, audio_secs)
        return content_secs

    @staticmethod
    def _content_secs(core_ust: UstInfo) -> float:
        """音符 tick 内容的时长（秒），与 timing.rs 一致：每音符长度下限 1 tick。"""
        tick_per_second = core_ust.tempo * VideoExporter._TICKS_PER_QUARTER / 60.0
        if tick_per_second <= 0:
            return 0.0
        total_tick = sum(max(max(n.length, 0), 1) for n in core_ust.notes)
        return total_tick / tick_per_second

    @staticmethod
    def _pad_trailing_rest(core_ust: UstInfo, end_secs: float) -> UstInfo:
        """在音符末尾补一个尾部休止音符（lyric=R），覆盖 [内容结束, 音频结束] 的空拍区间，
        使渲染器在该区间显示“空拍/静默文字”，并在音频结束时进入结束文字。"""
        content_secs = VideoExporter._content_secs(core_ust)
        if end_secs <= content_secs:
            return core_ust
        tick_per_second = core_ust.tempo * VideoExporter._TICKS_PER_QUARTER / 60.0
        if tick_per_second <= 0:
            return core_ust
        trailing_ticks = int(round((end_secs - content_secs) * tick_per_second))
        if trailing_ticks <= 0:
            return core_ust
        rest = NoteInfo(index="EXPORT_PAD", length=trailing_ticks, lyric="R", note_num=60)
        return UstInfo(
            version=core_ust.version,
            tempo=core_ust.tempo,
            tracks=core_ust.tracks,
            notes=[*core_ust.notes, rest],
        )

    @staticmethod
    def _frames_for(end_secs: float, fps: int) -> int:
        """总帧 = 结束边界(秒)*fps + 1 秒结束画面。"""
        base = int(math.ceil(end_secs * fps))
        return max(base + fps, 1)

    def _audio_duration_secs(self) -> float:
        """用 ffprobe 读取伴奏音频时长（秒）；无伴奏 / 无 ffprobe / 失败时返回 0。"""
        mus = self._music_path
        if not mus or not os.path.exists(mus):
            return 0.0
        if shutil.which("ffprobe") is None:
            return 0.0
        tmp = f"{mus}.dur.tmp"
        cmd = [
            "ffprobe",
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            "-o", tmp,
            mus,
        ]
        try:
            result = subprocess.run(
                cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=60
            )
            if result.returncode != 0:
                return 0.0
            with open(tmp, "r", encoding="utf-8") as f:
                text = f.read().strip()
            return float(text) if text else 0.0
        except Exception as e:
            logger.warning(f"ffprobe 读取音频时长失败: {e}")
            return 0.0
        finally:
            if os.path.exists(tmp):
                try:
                    os.remove(tmp)
                except OSError:
                    pass

    # ===================== 伴奏混流 =====================

    def _mux_audio(self, video_path: str, audio_path: str):
        """用 ffmpeg 把伴奏音频混入无声 MP4（视频轨道复制，音频转 AAC）。"""
        if not os.path.exists(audio_path):
            logger.warning(f"伴奏文件不存在，跳过混流: {audio_path}")
            return
        tmp_path = f"{video_path}.mux.tmp.mp4"
        cmd = [
            "ffmpeg", "-y",
            "-i", video_path,
            "-i", audio_path,
            "-c:v", "copy",
            "-c:a", "aac",
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-movflags", "+faststart",
            tmp_path,
        ]
        try:
            result = subprocess.run(
                cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=3600
            )
        except FileNotFoundError as e:
            raise RuntimeError(f"{_AUDIO_MUX_FAILED}：未找到 ffmpeg") from e
        except Exception as e:
            raise RuntimeError(f"{_AUDIO_MUX_FAILED}：{e}") from e
        if result.returncode != 0:
            raise RuntimeError(f"{_AUDIO_MUX_FAILED}：ffmpeg 退出码 {result.returncode}")
        os.replace(tmp_path, video_path)
        logger.info(f"已混入伴奏音频: {video_path}")


# 供 UI / 类型检测引用错误码（与 ERcode.txt 同步登记）
def renderer_error_message(exc: Exception) -> str:
    """把渲染相关异常转成面向用户的中文提示。"""
    if isinstance(exc, RendererError):
        return f"渲染器错误（{exc.code}）：{exc}"
    if isinstance(exc, FileNotFoundError):
        return str(exc)
    if isinstance(exc, RuntimeError):
        return str(exc)
    return str(exc)
