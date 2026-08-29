# test_settings_display.py — 显示设置子域（Layer 2，需 qapp）
"""覆盖 core/settings/display.py：DisplaySettings 的属性/信号/读写（无 validate）。"""

from ustplayer.core.settings import DisplaySettings

SECTION = "DisplaySettings"


def test_defaults(qapp):
    s = DisplaySettings()
    assert s.show_bpm is True
    assert s.show_play_time is True
    assert s.show_song_name is True
    assert s.show_song_author is True
    assert s.show_ust_author is True
    assert s.fullscreen is True
    assert s.show_lyric is False
    assert s.show_note_name is True
    assert s.show_ust_lyric is True
    assert s.show_copyright is True
    assert s.font_note == ""
    assert s.font_ust_lyric == ""
    assert s.font_lrc == ""
    assert s.font_other == ""
    assert s.custom_font_paths == []


def test_new_display_controls_setters_and_signals(qapp, slot):
    """音名/歌字/版权开关与分槽字体：setter 发信号、同值不重发。"""
    s = DisplaySettings()
    s.show_note_name_changed.connect(slot)
    s.show_ust_lyric_changed.connect(slot)
    s.show_copyright_changed.connect(slot)
    s.font_note_changed.connect(slot)
    s.font_other_changed.connect(slot)
    s.custom_font_paths_changed.connect(slot)

    s.show_note_name = False
    s.show_ust_lyric = False
    s.show_copyright = False
    s.font_note = "黑体"
    s.font_other = "华文黑体"
    s.custom_font_paths = ["C:/f.ttf"]
    assert s.show_note_name is False
    assert s.show_ust_lyric is False
    assert s.show_copyright is False
    assert s.font_note == "黑体"
    assert s.font_other == "华文黑体"
    assert s.custom_font_paths == ["C:/f.ttf"]
    assert len(slot.calls) == 6

    s.show_note_name = False  # 同值不重发
    assert len(slot.calls) == 6


def test_new_display_controls_read_write(qapp):
    s = DisplaySettings()
    s.read_from({SECTION: {
        "show_note_name": "0", "show_ust_lyric": "1", "show_copyright": "0",
        "font_note": "黑体", "font_other": None, "custom_font_paths": ["a.ttf", 3, "b.ttf"],
    }})
    assert s.show_note_name is False
    assert s.show_ust_lyric is True
    assert s.show_copyright is False
    assert s.font_note == "黑体"
    assert s.font_other == ""
    assert s.custom_font_paths == ["a.ttf", "b.ttf"]

    cfg = {}
    s.show_copyright = True
    s.font_lrc = "楷体"
    s.custom_font_paths = ["Segoe.ttf"]
    s.write_to(cfg)
    assert cfg[SECTION]["show_note_name"] == "0"
    assert cfg[SECTION]["show_ust_lyric"] == "1"
    assert cfg[SECTION]["show_copyright"] == "1"
    assert cfg[SECTION]["font_note"] == "黑体"
    assert cfg[SECTION]["font_lrc"] == "楷体"
    assert cfg[SECTION]["custom_font_paths"] == ["Segoe.ttf"]


def test_setters(qapp):
    s = DisplaySettings()
    s.show_bpm = False
    s.show_lyric = True
    s.fullscreen = False
    assert s.show_bpm is False
    assert s.show_lyric is True
    assert s.fullscreen is False


def test_signal_fires_on_change(qapp, slot):
    s = DisplaySettings()
    s.show_bpm_changed.connect(slot)
    s.show_bpm = False
    assert len(slot.calls) == 1
    assert slot.calls[0] == (False,)


def test_signal_no_duplicate_on_same_value(qapp, slot):
    s = DisplaySettings()
    s.show_bpm_changed.connect(slot)
    s.show_bpm = True  # 同默认值，不发信号
    assert len(slot.calls) == 0


def test_read_from_as_bool_compat(qapp):
    s = DisplaySettings()
    s.read_from({SECTION: {
        "show_bpm": "0", "show_play_time": "1", "show_song_name": "yes",
        "show_song_author": "no", "show_ust_author": "on", "fullscreen": "0",
        "show_lyric": "1",
    }})
    assert s.show_bpm is False
    assert s.show_play_time is True
    assert s.show_song_name is True
    assert s.show_song_author is False
    assert s.show_ust_author is True
    assert s.fullscreen is False
    assert s.show_lyric is True


def test_write_to_stores_bool_as_str(qapp):
    s = DisplaySettings()
    s.show_bpm = False
    s.show_lyric = True
    config = {}
    s.write_to(config)
    assert config[SECTION]["show_bpm"] == "0"
    assert config[SECTION]["show_lyric"] == "1"
    assert config[SECTION]["show_play_time"] == "1"


def test_write_to_read_from_round_trip(qapp):
    s = DisplaySettings()
    s.show_bpm = False
    s.show_play_time = False
    s.show_song_name = False
    s.show_song_author = False
    s.show_ust_author = False
    s.fullscreen = False
    s.show_lyric = True
    config = {}
    s.write_to(config)

    s2 = DisplaySettings()
    s2.read_from(config)
    assert s2.show_bpm is False
    assert s2.show_play_time is False
    assert s2.show_song_name is False
    assert s2.show_song_author is False
    assert s2.show_ust_author is False
    assert s2.fullscreen is False
    assert s2.show_lyric is True
