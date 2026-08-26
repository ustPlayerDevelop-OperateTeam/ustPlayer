# test_video_exporter.py — 视频导出（Layer 3，需 qapp + 临时文件）
"""覆盖 core/video_exporter.py：配置组装、时序换算、.uprd 命名、缺 UST 报错，
以及一个（缺 DLL/ffmpeg 时跳过）的真实渲染冒烟。

真实渲染会调用 ustplayer_renderer.dll 并驱动外部 ffmpeg，故仅在二者都存在时运行。
"""

import os
import shutil
import sys

import pytest

from ustplayer.core.contracts import APP_VERSION, NoteInfo, UstInfo
from ustplayer.core.ustreader import UstFileReader
from ustplayer.core.uplr_io import UplrProjectIO
from ustplayer.core.video_exporter import VideoExporter


def _locate_dll():
    """在仓库 renderer/ 或根目录下查找渲染器 DLL。"""
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    for p in (
        os.path.join(root, "renderer", "ustplayer_renderer.dll"),
        os.path.join(root, "ustplayer_renderer.dll"),
    ):
        if os.path.isfile(p):
            return p
    return ""


# ===================== 纯逻辑单元测试 =====================

def test_frames_for_and_content_secs():
    # tempo 120 → 一拍 0.5s；两音符各 480 tick = 960 tick = 1.0s
    info = UstInfo(tempo=120.0, notes=[NoteInfo(length=480), NoteInfo(length=480)])
    assert VideoExporter._content_secs(info) == 1.0
    # fps=30 → 1.0*30 = 30 帧 + 1s 结束画面(30帧) = 60
    assert VideoExporter._frames_for(1.0, 30) == 60
    # 音频驱动的结束边界
    assert VideoExporter._frames_for(3.5, 30) == 105 + 30
    # 空 UST：至少 1 帧
    assert VideoExporter._frames_for(0.0, 30) == 30


def test_pad_trailing_rest_extends_content():
    info = UstInfo(tempo=120.0, notes=[NoteInfo(length=480)])  # 内容 0.5s
    exporter_dummy = None  # 纯静态方法，无需实例
    # 音频 2.0s > 内容 0.5s → 尾部补休止
    padded = VideoExporter._pad_trailing_rest(info, 2.0)
    assert len(padded.notes) == 2
    rest = padded.notes[-1]
    assert rest.lyric == "R"
    # trailing_tick = (2.0 - 0.5) * 960 = 1440
    assert rest.length == 1440
    # 结束边界不超内容 → 不补
    same = VideoExporter._pad_trailing_rest(info, 0.5)
    assert same is info
    assert len(same.notes) == 1


def test_uprd_path_for():
    assert VideoExporter._uprd_path_for(r"D:\x\song.mp4") == r"D:\x\song.uprd"
    assert VideoExporter._uprd_path_for(r"D:\x\noext") == r"D:\x\noext.uprd"


def test_build_render_config(make_manager):
    m = make_manager()
    m.project.project_name = "曲名工程"
    m.project.song_name = "测试曲"
    m.display.show_bpm = False
    m.display.show_lyric = True
    m.color.bg_color = "#123456"
    m.player.lyric_pos = "bottom"
    m.file.curve_show = True

    info = UstInfo(tempo=120.0, notes=[NoteInfo(length=480, lyric="あ", note_num=69)])
    exporter = VideoExporter(m, UstFileReader(), UplrProjectIO(m))
    cfg = exporter._build_render_config(info, r"D:\x\out.mp4", 1280, 720, 30)

    assert cfg["output_path"] == r"D:\x\out.mp4"
    assert cfg["width"] == 1280 and cfg["height"] == 720 and cfg["fps"] == 30
    # 与渲染器 serde 字段对齐
    assert cfg["ust"]["tempo"] == 120.0
    assert cfg["ust"]["notes"][0]["lyric"] == "あ"
    assert cfg["show"]["bpm"] is False
    assert cfg["show"]["lyric"] is True
    assert cfg["show"]["curve_show"] is True
    assert cfg["project"]["project_name"] == "曲名工程"
    assert cfg["style"]["bg_color"] == "#123456"
    assert cfg["style"]["lyric_pos"] == "bottom"
    assert cfg["style"]["app_version"] == APP_VERSION


def test_render_requires_ust(make_manager, tmp_path):
    m = make_manager()  # ust_path 为空
    exporter = VideoExporter(m, UstFileReader(), UplrProjectIO(m))
    with pytest.raises(FileNotFoundError):
        exporter.render(str(tmp_path / "out.mp4"), 320, 240, 30, False)


# ===================== 可取消子进程与半成品清理 =====================

def test_run_cancellable_returns_exit_code():
    code, _tail = VideoExporter._run_cancellable(
        [sys.executable, "-c", "raise SystemExit(3)"], None, 60
    )
    assert code == 3


def test_run_cancellable_cancel_kills_and_raises():
    with pytest.raises(RuntimeError, match="导出已取消"):
        VideoExporter._run_cancellable(
            [sys.executable, "-c", "import time; time.sleep(30)"],
            lambda: True,
            60,
        )


def test_render_cancel_cleans_partial_artifacts(make_manager, tmp_path, monkeypatch):
    """渲染阶段取消后，已写出的 .uprd 与半成品 MP4 必须被清理，不留孤儿文件。"""
    m = make_manager()
    ust = tmp_path / "song.ust"
    ust.write_text(
        "[#SETTING]\nTempo=120\nTracks=1\n[#0000]\nLength=480\nLyric=あ\nNoteNum=69\n",
        encoding="utf-8",
    )
    m.file.ust_path = str(ust)
    m.file.encoding = "UTF-8"

    out = str(tmp_path / "out.mp4")
    exporter = VideoExporter(m, UstFileReader(), UplrProjectIO(m))

    def _boom(*_args, **_kwargs):
        raise RuntimeError("导出已取消")

    monkeypatch.setattr(exporter, "_drive_renderer", _boom)
    with pytest.raises(RuntimeError, match="导出已取消"):
        exporter.render(out, 320, 240, 30, False)

    assert not os.path.exists(out)
    assert not os.path.exists(str(tmp_path / "out.uprd"))
    assert not os.path.exists(out + ".mux.tmp.mp4")


def test_mux_audio_missing_ffmpeg_raises(make_manager, tmp_path, monkeypatch):
    """无 ffmpeg 时混流抛出带“音频混流失败”前缀的 RuntimeError（UI 据此映射 ERcode011）。"""
    m = make_manager()
    exporter = VideoExporter(m, UstFileReader(), UplrProjectIO(m))
    monkeypatch.setattr(shutil, "which", lambda name: None)
    video = str(tmp_path / "v.mp4")
    audio = str(tmp_path / "a.wav")
    open(audio, "wb").close()
    with pytest.raises(RuntimeError, match="音频混流失败"):
        exporter._mux_audio(video, audio)


# ===================== 真实渲染冒烟（有 DLL + ffmpeg 才跑） =====================

@pytest.mark.skipif(
    not _locate_dll() or shutil.which("ffmpeg") is None,
    reason="需要 ustplayer_renderer.dll 与 ffmpeg 才能在本地/CI 渲染",
)
def test_render_smoke_creates_files(qapp, make_manager, tmp_path):
    import ustplayer.core.settings_manager as sm_mod
    import ustplayer.core.settings_store as ss_mod
    from ustplayer.core.settings_manager import SettingsManager

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    monkeypatch = pytest.MonkeyPatch()
    try:
        # 把 program_root 指向仓库根，让渲染器从 renderer/ 子目录加载 DLL
        monkeypatch.setattr(sm_mod, "resolve_program_root", lambda: root)
        monkeypatch.setattr(ss_mod, "resolve_program_root", lambda: root)
        m = SettingsManager()
        m.file.ust_path = str(tmp_path / "song.ust")
        with open(m.file.ust_path, "w", encoding="Shift-JIS") as f:
            f.write("[#SETTING]\nTempo=120\nTracks=1\n"
                    "[#0000]\nLength=480\nLyric=あ\nNoteNum=69\n")
        m.file.encoding = "Shift-JIS"
        m.project.project_name = "smoke"

        out = str(tmp_path / "smoke.mp4")
        uprd = VideoExporter(m, UstFileReader(), UplrProjectIO(m)).render(
            out, 320, 240, 10, mux_audio=False
        )
        assert os.path.exists(out) and os.path.getsize(out) > 0
        assert os.path.exists(uprd) and uprd.endswith(".uprd")
    finally:
        monkeypatch.undo()
