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
import tempfile
import time
from typing import Any, Callable, Optional

from ustplayer.core.contracts import APP_VERSION, NoteInfo, ProjectIO, UstInfo, UstParser
from ustplayer.core.log import logger
from ustplayer.core.renderer_ffi import RendererContext, RendererError, RendererLoader
from ustplayer.core.settings_manager import SettingsManager

# 伴奏混入失败时抛出的用户可读前缀，便于 UI 识别（ERcode011）
_AUDIO_MUX_FAILED = "音频混流失败"

# 子进程轮询间隔（秒）：取消请求最迟在此延迟后生效
_PROC_POLL_INTERVAL = 0.2


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
        end_secs = self._render_end_secs(core_ust, cancel_check)
        render_ust = self._pad_trailing_rest(core_ust, end_secs)
        config = self._build_render_config(render_ust, output_path, width, height, fps)
        total_frames = self._frames_for(end_secs, fps)

        # 1) 写 .uprd → 2) 渲染 → 3) 可选混流；任一步失败/取消都清理半成品产物，
        # 避免残留打不开的 MP4 与指向无效视频的 .uprd
        uprd_path = self._uprd_path_for(output_path)
        try:
            self._project_io.export_uprd(uprd_path, {"width": width, "height": height, "fps": fps})
            logger.info(f"已写入 .uprd 工程: {uprd_path}")

            self._drive_renderer(config, render_ust, total_frames, progress_cb, cancel_check)

            if mux_audio and self._music_path:
                self._mux_audio(output_path, self._music_path, cancel_check)
        except BaseException:
            logger.warning(f"导出中止，清理半成品文件: {output_path}")
            self._remove_quiet(output_path)
            self._remove_quiet(output_path + ".mux.tmp.mp4")
            self._remove_quiet(uprd_path)
            raise

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

    @staticmethod
    def _remove_quiet(path: str) -> None:
        """尽力删除半成品文件；删除失败仅记日志，不掩盖导出流程的原异常。"""
        try:
            if os.path.exists(path):
                os.remove(path)
        except OSError as e:
            logger.warning(f"清理半成品文件失败: {path} ({e})")

    # ===================== 可取消的子进程执行 =====================

    @staticmethod
    def _run_cancellable(
        cmd: list,
        cancel_check: Optional[Callable[[], bool]],
        timeout_secs: float,
        stderr_file: Any = None,
    ) -> "tuple[int, str]":
        """Popen 启动子进程并轮询等待：期间持续响应 cancel_check 与超时。

        替代阻塞式 subprocess.run —— 否则进入 ffprobe/ffmpeg 阶段后，
        用户取消要等子进程自然结束（混流最长 1 小时）才能生效。
        stderr_file 可选传入以写模式打开的文件对象（如 tempfile.TemporaryFile），
        失败时可读取其内容辅助排查。
        返回 (退出码, stderr 尾部文本)；未捕获 stderr 时尾部为空字符串。
        """
        proc = subprocess.Popen(
            cmd,
            stdout=subprocess.DEVNULL,
            stderr=stderr_file or subprocess.DEVNULL,
        )
        deadline = time.monotonic() + timeout_secs
        while True:
            code = proc.poll()
            if code is not None:
                break
            if time.monotonic() >= deadline:
                proc.kill()
                proc.wait()
                raise TimeoutError(f"{os.path.basename(cmd[0])} 处理超时")
            if cancel_check and cancel_check():
                proc.kill()
                proc.wait()
                raise RuntimeError("导出已取消")
            time.sleep(_PROC_POLL_INTERVAL)
        tail = ""
        if stderr_file is not None:
            try:
                stderr_file.seek(0)
                tail = stderr_file.read()[-2000:].strip()
            except OSError:
                tail = ""
        return code, tail

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

    def _render_end_secs(self, core_ust: UstInfo, cancel_check: Optional[Callable[[], bool]] = None) -> float:
        """渲染结束边界（秒）：有伴奏时以“音频播完”为准（且不短于音符内容总长），
        无伴奏时按音符 tick 内容总长。"""
        content_secs = VideoExporter._content_secs(core_ust)
        audio_secs = self._audio_duration_secs(cancel_check)
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

    def _audio_duration_secs(self, cancel_check: Optional[Callable[[], bool]] = None) -> float:
        """用 ffprobe 读取伴奏音频时长（秒）；无伴奏 / 无 ffprobe / 失败时返回 0。"""
        mus = self._music_path
        if not mus or not os.path.exists(mus):
            return 0.0
        exe = shutil.which("ffprobe")
        if exe is None:
            return 0.0
        tmp = f"{mus}.dur.tmp"
        cmd = [
            exe,
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            "-o", tmp,
            mus,
        ]
        try:
            code, _tail = VideoExporter._run_cancellable(cmd, cancel_check, 60)
            if code != 0:
                logger.warning(f"ffprobe 读取音频时长失败（退出码 {code}）")
                return 0.0
            with open(tmp, "r", encoding="utf-8") as f:
                text = f.read().strip()
            return float(text) if text else 0.0
        except RuntimeError:
            raise  # 用户取消必须向上传播，不能被吞成“时长未知”
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

    def _mux_audio(self, video_path: str, audio_path: str, cancel_check: Optional[Callable[[], bool]] = None):
        """用 ffmpeg 把伴奏音频混入无声 MP4（视频轨道复制，音频转 AAC）。

        stderr 落临时文件，失败时把尾部内容附进错误信息便于排查。"""
        if not os.path.exists(audio_path):
            logger.warning(f"伴奏文件不存在，跳过混流: {audio_path}")
            return
        exe = shutil.which("ffmpeg")
        if exe is None:
            raise RuntimeError(f"{_AUDIO_MUX_FAILED}：未找到 ffmpeg")
        tmp_path = f"{video_path}.mux.tmp.mp4"
        cmd = [
            exe, "-y",
            "-i", video_path,
            "-i", audio_path,
            "-c:v", "copy",
            "-c:a", "aac",
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-movflags", "+faststart",
            tmp_path,
        ]
        with tempfile.TemporaryFile(mode="w+", encoding="utf-8", errors="replace") as errf:
            try:
                code, tail = VideoExporter._run_cancellable(
                    cmd, cancel_check, 3600, stderr_file=errf
                )
            except FileNotFoundError as e:
                raise RuntimeError(f"{_AUDIO_MUX_FAILED}：无法启动 ffmpeg") from e
            if code != 0:
                detail = f"：{tail}" if tail else ""
                raise RuntimeError(f"{_AUDIO_MUX_FAILED}：ffmpeg 退出码 {code}{detail}")
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
