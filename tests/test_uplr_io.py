# test_uplr_io.py — .uplr 工程文件导入/导出（Layer 3，需 qapp + 临时文件）
"""覆盖 core/uplr_io.py：ZIP 导出结构、导出→导入往返、旧版文本导入、
资源解压、缺 Info.json、zip slip 防护、超大 Info.json 防护、重名资源。
"""

import json
import os
import zipfile

import pytest

from ustplayer.core.uplr_io import UplrProjectIO


# ===================== 多目录隔离的 SettingsManager 工厂 =====================

@pytest.fixture
def manager_factory(qapp, monkeypatch, tmp_path):
    """返回工厂 new(subdir) -> SettingsManager，每个子目录相互隔离
    （各自独立的 Settings.json 与 LOCALAPPDATA/.uplr 缓存）。"""
    import ustplayer.core.settings_store as ss_mod
    import ustplayer.core.settings_manager as sm_mod
    from ustplayer.core.settings_manager import SettingsManager

    def _new(subdir):
        d = tmp_path / subdir
        d.mkdir(parents=True, exist_ok=True)
        target = str(d)
        monkeypatch.setattr(ss_mod, "resolve_program_root", lambda: target)
        monkeypatch.setattr(sm_mod, "resolve_program_root", lambda: target)
        monkeypatch.setenv("LOCALAPPDATA", target)
        return SettingsManager()

    return _new


def _write(path, content):
    path.write_text(content, encoding="utf-8")
    return str(path)


# ===================== 导出结构 =====================

def test_export_zip_structure(manager_factory, tmp_path):
    m = manager_factory("src")
    m.project.project_name = "demo"
    ust = _write(tmp_path / "song.ust", "[#SETTING]\n")
    music = tmp_path / "music.wav"
    music.write_bytes(b"WAV")
    m.file.ust_path = ust
    m.project.music_path = str(music)

    uplr = str(tmp_path / "out.uplr")
    UplrProjectIO(m).export_uplr(uplr)

    with zipfile.ZipFile(uplr) as zf:
        names = zf.namelist()
        assert "Info.json" in names
        assert "song.ust" in names
        assert "music.wav" in names
        info = json.loads(zf.read("Info.json"))
        assert info["basic"]["project_name"] == "demo"
        assert info["basic"]["ust_path"] == "song.ust"
        assert info["basic"]["music_path"] == "music.wav"


def test_export_no_resources_minimal_zip(manager_factory, tmp_path):
    m = manager_factory("n")
    m.project.project_name = "empty"
    uplr = str(tmp_path / "empty.uplr")
    UplrProjectIO(m).export_uplr(uplr)

    with zipfile.ZipFile(uplr) as zf:
        assert zf.namelist() == ["Info.json"]
        info = json.loads(zf.read("Info.json"))
        assert info["basic"]["ust_path"] is None
        assert info["basic"]["music_path"] is None
        assert info["else"]["lrc_path"] is None


def test_export_duplicate_resource_names(manager_factory, tmp_path):
    m = manager_factory("d")
    a, b = tmp_path / "a", tmp_path / "b"
    a.mkdir(); b.mkdir()
    f1 = _write(a / "track.txt", "ust")
    f2 = _write(b / "track.txt", "lrc")
    m.file.ust_path = f1
    m.player.lrc_path = f2

    uplr = str(tmp_path / "dup.uplr")
    UplrProjectIO(m).export_uplr(uplr)

    with zipfile.ZipFile(uplr) as zf:
        names = zf.namelist()
        assert "track.txt" in names
        assert "track_2.txt" in names
        info = json.loads(zf.read("Info.json"))
        assert info["basic"]["ust_path"] == "track.txt"
        assert info["else"]["lrc_path"] == "track_2.txt"


# ===================== 导出 → 导入 往返 =====================

def test_export_import_round_trip(manager_factory, tmp_path):
    m1 = manager_factory("src")
    m1.project.project_name = "demo"
    m1.project.song_name = "曲名"
    m1.project.song_author = "作曲"
    m1.project.ust_author = "调音"
    m1.file.encoding = "UTF-8"
    m1.display.show_bpm = False
    m1.display.show_lyric = True
    m1.display.fullscreen = False
    m1.color.bg_color = "#112233"
    m1.color.note_color = "#445566"
    m1.player.lyric_pos = "bottom"
    m1.player.silent_display = "dash"
    m1.player.silent_custom_text = "休"
    m1.player.end_display = "none"
    m1.player.pitch_placeholder = "custom"
    m1.player.pitch_custom_text = "x"

    ust = _write(tmp_path / "song.ust", "[#SETTING]\n")
    lrc = _write(tmp_path / "lyric.lrc", "[00:00.00]歌\n")
    music = tmp_path / "music.wav"; music.write_bytes(b"WAV")
    m1.file.ust_path = ust
    m1.player.lrc_path = lrc
    m1.project.music_path = str(music)

    uplr = str(tmp_path / "proj.uplr")
    UplrProjectIO(m1).export_uplr(uplr)

    # 第二个互不干扰的 manager，导入后应与导出值一致
    m2 = manager_factory("dst")
    UplrProjectIO(m2).import_uplr(uplr)

    assert m2.project.project_name == "demo"
    assert m2.project.song_name == "曲名"
    assert m2.project.song_author == "作曲"
    assert m2.project.ust_author == "调音"
    assert m2.file.encoding == "UTF-8"
    assert m2.display.show_bpm is False
    assert m2.display.show_lyric is True
    assert m2.display.fullscreen is False
    assert m2.color.bg_color == "#112233"
    assert m2.color.note_color == "#445566"
    assert m2.player.lyric_pos == "bottom"
    assert m2.player.silent_display == "dash"
    assert m2.player.silent_custom_text == "休"
    assert m2.player.end_display == "none"
    assert m2.player.pitch_placeholder == "custom"
    assert m2.player.pitch_custom_text == "x"

    # 资源被解压到 m2 的缓存目录，路径已重定向且文件存在
    assert m2.file.ust_path != ust
    assert os.path.exists(m2.file.ust_path)
    assert m2.project.music_path != str(music)
    assert os.path.exists(m2.project.music_path)
    assert m2.player.lrc_path != lrc
    assert os.path.exists(m2.player.lrc_path)


def test_import_zip_extracts_resources_to_cache(manager_factory, tmp_path):
    m1 = manager_factory("s")
    ust = _write(tmp_path / "song.ust", "USTDATA")
    m1.file.ust_path = ust
    uplr = str(tmp_path / "r.uplr")
    UplrProjectIO(m1).export_uplr(uplr)

    m2 = manager_factory("d")
    UplrProjectIO(m2).import_uplr(uplr)
    # 缓存目录应存在且含解压出的 ust
    assert os.path.exists(m2.file.ust_path)
    with open(m2.file.ust_path, encoding="utf-8") as f:
        assert f.read() == "USTDATA"


# ===================== 旧版文本格式导入 =====================

def test_import_text_legacy(manager_factory, tmp_path):
    m = manager_factory("t")
    content = (
        "project_name=demo\n"
        "song_name=曲名\n"
        "ust_author=调音\n"
        "encoding=UTF-8\n"
        "bg_color=#112233\n"
        "lyric_pos=上\n"
        "silent_display=R\n"
        "end_display=END\n"
        "pitch_placeholder=无\n"
        "show_bpm=0\n"
        "show_lyric=1\n"
        "curve_show=yes\n"
    )
    p = _write(tmp_path / "legacy.uplr", content)
    UplrProjectIO(m).import_uplr(p)

    assert m.project.project_name == "demo"
    assert m.project.song_name == "曲名"
    assert m.project.ust_author == "调音"
    assert m.file.encoding == "UTF-8"
    assert m.color.bg_color == "#112233"
    # 旧中文枚举值被迁移
    assert m.player.lyric_pos == "top"
    assert m.player.silent_display == "r"
    assert m.player.end_display == "end"
    assert m.player.pitch_placeholder == "none"
    assert m.display.show_bpm is False
    assert m.display.show_lyric is True
    assert m.file.curve_show is True


def test_import_text_legacy_skips_comments_and_blank(manager_factory, tmp_path):
    m = manager_factory("c")
    content = "# 注释行\n\nproject_name=x\nbadline_without_equals\nsong_name=y\n"
    p = _write(tmp_path / "c.uplr", content)
    UplrProjectIO(m).import_uplr(p)
    assert m.project.project_name == "x"
    assert m.project.song_name == "y"


# ===================== 安全防护 =====================

def test_import_zip_missing_info_raises(manager_factory, tmp_path):
    m = manager_factory("e")
    p = tmp_path / "noinfo.uplr"
    with zipfile.ZipFile(p, "w") as zf:
        zf.writestr("song.ust", "data")
    with pytest.raises(ValueError, match="Info.json"):
        UplrProjectIO(m).import_uplr(str(p))


def test_import_zip_slip_protection(manager_factory, tmp_path):
    m = manager_factory("slip")
    p = tmp_path / "slip.uplr"
    with zipfile.ZipFile(p, "w") as zf:
        zf.writestr("Info.json", json.dumps({"basic": {}, "display": {}, "color": {}, "else": {}}))
        zf.writestr("../evil.txt", "pwned")
    with pytest.raises(ValueError, match="不安全路径"):
        UplrProjectIO(m).import_uplr(str(p))


def test_import_zip_oversized_info_raises(manager_factory, tmp_path):
    m = manager_factory("big")
    p = tmp_path / "big.uplr"
    # Info.json 内容超过 1MB 上限
    huge = "a" * (1024 * 1024 + 100)
    with zipfile.ZipFile(p, "w") as zf:
        zf.writestr("Info.json", huge)
    with pytest.raises(ValueError, match="Info.json 异常过大"):
        UplrProjectIO(m).import_uplr(str(p))


def test_import_zip_absolute_path_rejected(manager_factory, tmp_path):
    m = manager_factory("abs")
    p = tmp_path / "abs.uplr"
    with zipfile.ZipFile(p, "w") as zf:
        zf.writestr("Info.json", json.dumps({"basic": {}, "display": {}, "color": {}, "else": {}}))
        zf.writestr("/etc/evil.txt", "pwned")
    with pytest.raises(ValueError, match="不安全路径"):
        UplrProjectIO(m).import_uplr(str(p))
