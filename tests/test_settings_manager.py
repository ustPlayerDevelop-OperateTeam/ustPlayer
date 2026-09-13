# test_settings_manager.py — 设置门面（Layer 2，需 qapp + prog_root 隔离）
"""覆盖 core/settings_manager.py：组装默认值、读写往返、sanitize 校验、build_ust_info 映射。"""

from ustplayer.core.contracts import UstInfo
from ustplayer.core.settings_manager import SettingsManager


# ===================== 组装与默认值 =====================

def test_init_defaults(make_manager):
    m = make_manager()
    # 各子域均存在
    for attr in ("project", "file", "display", "color", "player", "theme", "language"):
        assert getattr(m, attr) is not None

    # 默认值正确
    assert m.file.encoding == "Shift-JIS"
    assert m.display.show_bpm is True
    assert m.color.bg_color == "#000000"
    assert m.player.lyric_pos == "top"
    assert m.language.language == "system"


def test_construction_empty_config_does_not_persist(make_manager, prog_root):
    # 首次运行（无配置 / 无旧版 ini）不落盘 Settings.json——read_settings 在空配置时
    # 提前 return，不调用 write_settings，避免生成无意义的空配置文件
    make_manager()
    assert not (prog_root / "Settings.json").exists()


def test_write_settings_persists(make_manager, prog_root):
    m = make_manager()
    m.project.project_name = "demo"
    m.write_settings()
    assert (prog_root / "Settings.json").exists()


# ===================== 读写往返 =====================

def test_read_write_round_trip(make_manager):
    m = make_manager()
    m.project.project_name = "demo"
    m.project.song_name = "曲名"
    m.color.bg_color = "#112233"
    m.display.show_bpm = False
    m.player.lyric_pos = "bottom"
    m.file.encoding = "UTF-8"
    m.language.language = "en_US"
    m.write_settings()

    # 同一 tmp_path 下新构造的 manager 应读到这些值
    m2 = make_manager()
    assert m2.project.project_name == "demo"
    assert m2.project.song_name == "曲名"
    assert m2.color.bg_color == "#112233"
    assert m2.display.show_bpm is False
    assert m2.player.lyric_pos == "bottom"
    assert m2.file.encoding == "UTF-8"
    assert m2.language.language == "en_US"


# ===================== sanitize =====================

def test_sanitize_corrects_invalid(make_manager):
    m = make_manager()
    m.file.encoding = "Latin-1"
    m.color.bg_color = "bad"
    m.color.note_color = "also-bad"
    m.player.lyric_pos = "middle"
    m.player.silent_display = "x"
    m.sanitize()
    assert m.file.encoding == "Shift-JIS"
    assert m.color.bg_color == "#000000"
    assert m.color.note_color == "#6c6c6c"
    assert m.player.lyric_pos == "top"
    assert m.player.silent_display == "r"


# ===================== build_ust_info =====================

def test_build_ust_info_mapping(make_manager):
    m = make_manager()
    m.display.show_bpm = False
    m.display.show_lyric = True
    m.file.curve_show = True
    m.project.project_name = "p"
    m.project.song_name = "s"
    m.project.song_author = "sa"
    m.project.ust_author = "ua"
    m.color.bg_color = "#111111"
    m.color.pitch_curve_color = "#222222"
    m.player.lyric_pos = "bottom"
    m.player.silent_display = "dash"
    m.player.pitch_placeholder = "dash"

    ust = UstInfo(tempo=100.0, tracks=2, notes=[])
    params = m.build_ust_info(ust)

    # ust 原样透传
    assert params.ust is ust

    # show 映射
    assert params.show.bpm is False
    assert params.show.lyric is True
    assert params.show.curve_show is True

    # project 映射
    assert params.project.project_name == "p"
    assert params.project.song_name == "s"
    assert params.project.song_author == "sa"
    assert params.project.ust_author == "ua"

    # style 映射
    assert params.style.bg_color == "#111111"
    assert params.style.pitch_curve_color == "#222222"
    assert params.style.lyric_pos == "bottom"
    assert params.style.silent_display == "dash"
    assert params.style.pitch_placeholder == "dash"
    assert params.style.fullscreen is True  # display.fullscreen 默认
