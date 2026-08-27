# test_uplr_io.py — .uplr 工程文件导入/导出（Layer 3，需 qapp + 临时文件）
"""覆盖 core/uplr_io.py：ZIP 导出结构、导出→导入往返、旧版文本导入、
资源解压、缺 Info.json、zip slip 防护、超大 Info.json 防护、重名资源。
"""

import json
import os
import zipfile

import pytest

from ustplayer.core.uplr_io import UplrProjectIO, normalize_uprd_info


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


# ===================== Info.json 资源名穿越防护 =====================

@pytest.mark.parametrize(
    "evil",
    [
        "..\\evil.ust",       # 反斜杠 .. 穿越
        "../evil.ust",        # 正斜杠 .. 穿越
        "..\\..\\evil.ust",   # 多级穿越
        "sub/../../evil.ust", # 子目录内穿越
        "C:\\evil.ust",       # 盘符绝对路径
        "/abs/evil.ust",      # 根绝对路径
    ],
)
def test_import_info_json_resource_traversal_rejected(manager_factory, tmp_path, evil):
    """Info.json 里登记 .. 穿越 / 绝对路径 / 盘符资源名时，导入必须整体拒绝。

    资源成员解压有 zip-slip 防护，但 Info.json 的 ust_path/music_path/lrc_path
    是另一条“软路径”，同样不允许逃出缓存目录。"""
    m = manager_factory("trav")
    p = tmp_path / "trav.uplr"
    info = {"basic": {"ust_path": evil}, "display": {}, "color": {}, "else": {}}
    with zipfile.ZipFile(p, "w") as zf:
        zf.writestr("Info.json", json.dumps(info))
        zf.writestr("song.ust", "[#SETTING]\n")
    with pytest.raises(ValueError, match="不安全路径"):
        UplrProjectIO(m).import_uplr(str(p))


def test_import_zip_traversal_directory_entry_rejected(manager_factory, tmp_path):
    """名为 ../x/ 的目录条目也必须拒绝（先做安全校验，再按目录条目放行）。"""
    m = manager_factory("dirslip")
    p = tmp_path / "dir.uplr"
    with zipfile.ZipFile(p, "w") as zf:
        zf.writestr("Info.json", json.dumps({"basic": {}, "display": {}, "color": {}, "else": {}}))
        zf.writestr("../evil/", "")
    with pytest.raises(ValueError, match="不安全路径"):
        UplrProjectIO(m).import_uplr(str(p))


def test_normalize_uprd_info_removes_extra_and_adds_pitch_curve_color():
    """.uprd 归一化到 .uplr 兼容结构：
    移除 show_phoneme/show_midinote/show_waveform；curve_show 从 else 挪到 display；
    补上缺失的 pitch_curve_color（默认 #FFFFFF）；公共字段与 video 保留。"""
    info = {
        "encoding": "Shift-JIS",
        "basic": {"project_name": None, "ust_path": None, "music_path": None,
                  "song_name": None, "song_author": None, "ust_author": None},
        "display": {"show_bpm": 1, "show_play_time": 1, "show_song_name": 1,
                    "show_song_author": 1, "show_ust_author": 1, "show_phoneme": 0,
                    "show_midinote": 0, "show_waveform": 0, "fullscreen": 1, "show_lyric": 0},
        "color": {"bg_color": "#000000", "note_color": "#6c6c6c", "lyric_color": "#FFFFFF",
                  "lyric_text_color": "#FFFFFF", "other_text_color": "#FFFFFF"},
        "else": {"lyric_pos": "上", "lrc_path": None, "silent_display": "R",
                 "silent_custom_text": None, "end_display": "END", "end_custom_text": None,
                 "curve_show": 0, "pitch_placeholder": "无", "pitch_custom_text": None},
        "video": {"fps": 60, "height": 1920, "width": 1080},
    }
    out = normalize_uprd_info(info)

    d = out["display"]
    # 三个多余开关被移除
    for k in ("show_phoneme", "show_midinote", "show_waveform"):
        assert k not in d
    # 公共字段保留
    assert d["show_bpm"] == 1
    assert d["show_lyric"] == 0
    assert out["basic"]["song_name"] is None
    # curve_show 被移除（display 与 else 都不保留）
    assert "curve_show" not in out["display"]
    assert "curve_show" not in out["else"]
    # 中文枚举值迁移为英文稳定 key（与 migrate_value 一致）
    assert out["else"]["lyric_pos"] == "top"
    assert out["else"]["silent_display"] == "r"
    assert out["else"]["end_display"] == "end"
    assert out["else"]["pitch_placeholder"] == "none"
    # pitch_curve_color 补默认；其余颜色保留
    assert out["color"]["pitch_curve_color"] == "#FFFFFF"
    assert out["color"]["bg_color"] == "#000000"
    # video 保留（渲染器 width/height/fps）
    assert out["video"] == {"fps": 60, "height": 1920, "width": 1080}


# ===================== 工程缓存目录与清除 =====================

def test_cache_dir_under_program_root_usage_and_clear(manager_factory, tmp_path):
    m1 = manager_factory("cachesrc")
    ust = _write(tmp_path / "song.ust", "[#SETTING]\n")
    music = tmp_path / "music.wav"; music.write_bytes(b"WAV")
    m1.file.ust_path = ust
    m1.project.music_path = str(music)
    uplr = str(tmp_path / "cacheproj.uplr")
    UplrProjectIO(m1).export_uplr(uplr)

    m2 = manager_factory("cachedst")
    io2 = UplrProjectIO(m2)
    # cache_base 落在程序根（prog_root 已被 fixture 指向 tmp 子目录）下的 cache
    cache_base = io2.cache_base()
    assert os.path.basename(cache_base) == "cache"
    # 导入前占用 0
    assert io2.cache_usage() == 0

    io2.import_uplr(uplr)
    # 解压产物在 cache_base 下，且占用 > 0
    assert os.path.exists(m2.file.ust_path)
    assert os.path.commonpath([cache_base, os.path.abspath(m2.file.ust_path)]) == cache_base
    usage = io2.cache_usage()
    assert usage > 0

    io2.clear_cache()
    assert io2.cache_usage() == 0
    assert not os.path.exists(m2.file.ust_path)

def test_export_uprd_structure_and_roundtrip(manager_factory, tmp_path):
    m1 = manager_factory("src")
    m1.project.project_name = "demo"
    m1.project.song_name = "曲名"
    m1.file.encoding = "UTF-8"
    m1.display.show_bpm = False
    m1.display.show_lyric = True
    m1.color.bg_color = "#112233"
    m1.player.lyric_pos = "bottom"
    m1.player.silent_display = "dash"
    m1.file.curve_show = True

    ust = _write(tmp_path / "song.ust", "[#SETTING]\n")
    lrc = _write(tmp_path / "lyric.lrc", "[00:00.00]歌\n")
    music = tmp_path / "music.wav"; music.write_bytes(b"WAV")
    m1.file.ust_path = ust
    m1.player.lrc_path = lrc
    m1.project.music_path = str(music)

    uprd = str(tmp_path / "proj.uprd")
    UplrProjectIO(m1).export_uprd(uprd, {"width": 1280, "height": 720, "fps": 30})

    with zipfile.ZipFile(uprd) as zf:
        names = zf.namelist()
        assert "Info.json" in names
        assert "song.ust" in names
        info = json.loads(zf.read("Info.json"))
        # video 段写入
        assert info["video"] == {"width": 1280, "height": 720, "fps": 30}
        # 资源路径以包内文件名记录
        assert info["basic"]["ust_path"] == "song.ust"
        assert info["else"]["lrc_path"] == "lyric.lrc"
        # curve_show 在 else 段；枚举为英文稳定 key
        assert info["else"]["curve_show"] == 1
        assert info["display"]["show_lyric"] == 1
        assert info["else"]["lyric_pos"] == "bottom"

    # 导入（与 .uplr 同走 ZIP 路径）应还原设置
    m2 = manager_factory("dst")
    UplrProjectIO(m2).import_uplr(uprd)
    assert m2.project.project_name == "demo"
    assert m2.project.song_name == "曲名"
    assert m2.file.encoding == "UTF-8"
    assert m2.display.show_bpm is False
    assert m2.display.show_lyric is True
    assert m2.color.bg_color == "#112233"
    assert m2.player.lyric_pos == "bottom"
    assert m2.player.silent_display == "dash"
    assert m2.file.curve_show is True
    # 资源被解压到缓存目录
    assert os.path.exists(m2.file.ust_path)


def test_import_failure_rolls_back_settings_and_cache(manager_factory, tmp_path):
    """导入失败（Info.json 资源名穿越）后，已触碰的设置必须回滚、半成品缓存必须清理
    ——否则部分篡改的值会被 closeEvent 的 write_settings 持久化（回归测试）。"""
    m = manager_factory("rollback")
    # 预先设置一组"用户已有"的值
    m.project.project_name = "我的工程"
    m.file.ust_path = "C:/old/song.ust"
    m.display.show_bpm = False
    m.color.bg_color = "#123456"
    m.player.silent_display = "dash"

    evil = "..\\evil.ust"
    p = tmp_path / "evil.uplr"
    info = {
        "basic": {"project_name": "恶意工程", "ust_path": evil},
        "display": {}, "color": {}, "else": {},
    }
    with zipfile.ZipFile(p, "w") as zf:
        zf.writestr("Info.json", json.dumps(info))
        zf.writestr("song.ust", "[#SETTING]\n")

    io = UplrProjectIO(m)
    with pytest.raises(ValueError, match="不安全路径"):
        io.import_uplr(str(p))

    # 状态未被污染：已被部分赋值的属性必须恢复原值
    assert m.project.project_name == "我的工程"
    assert m.file.ust_path == "C:/old/song.ust"
    assert m.display.show_bpm is False
    assert m.color.bg_color == "#123456"
    assert m.player.silent_display == "dash"
    # 已解压的半成品缓存也被清理
    assert not os.path.exists(io._uplr_cache_dir(str(p)))

