# test_settings_color.py — 颜色设置子域（Layer 2，需 qapp）
"""覆盖 core/settings/color.py：ColorSettings 的属性/信号/读写 + 颜色校验回退。"""

from ustplayer.core.settings import ColorSettings

SECTION = "ColorSettings"
FALLBACKS = {
    "bg_color": "#000000",
    "note_color": "#6c6c6c",
    "lyric_color": "#FFFFFF",
    "lyric_text_color": "#FFFFFF",
    "other_text_color": "#FFFFFF",
    "pitch_curve_color": "#FFFFFF",
}
SAMPLE = {k: "#123456" for k in FALLBACKS}


def test_defaults(qapp):
    s = ColorSettings()
    for attr, fb in FALLBACKS.items():
        assert getattr(s, attr) == fb


def test_setters(qapp):
    s = ColorSettings()
    s.bg_color = "#aabbcc"
    s.note_color = "#112233"
    assert s.bg_color == "#aabbcc"
    assert s.note_color == "#112233"


def test_signal_fires_on_change(qapp, slot):
    s = ColorSettings()
    s.bg_color_changed.connect(slot)
    s.bg_color = "#aabbcc"
    assert len(slot.calls) == 1
    assert slot.calls[0] == ("#aabbcc",)


def test_signal_no_duplicate_on_same_value(qapp, slot):
    s = ColorSettings()
    s.bg_color_changed.connect(slot)
    s.bg_color = "#000000"  # 同默认值
    assert len(slot.calls) == 0


def test_read_from(qapp):
    s = ColorSettings()
    s.read_from({SECTION: SAMPLE})
    for attr in FALLBACKS:
        assert getattr(s, attr) == "#123456"


def test_write_to(qapp):
    s = ColorSettings()
    for attr in FALLBACKS:
        setattr(s, attr, "#abcdef")
    config = {}
    s.write_to(config)
    for attr in FALLBACKS:
        assert config[SECTION][attr] == "#abcdef"


def test_write_to_read_from_round_trip(qapp):
    s = ColorSettings()
    s.bg_color = "#111111"
    s.note_color = "#222222"
    s.lyric_color = "#333333"
    s.lyric_text_color = "#444444"
    s.other_text_color = "#555555"
    s.pitch_curve_color = "#666666"
    config = {}
    s.write_to(config)

    s2 = ColorSettings()
    s2.read_from(config)
    assert s2.bg_color == "#111111"
    assert s2.pitch_curve_color == "#666666"


def test_validate_resets_invalid_colors(qapp):
    s = ColorSettings()
    # 把所有颜色改成非法值
    for attr in FALLBACKS:
        setattr(s, attr, "not-a-color")
    s.validate()
    for attr, fb in FALLBACKS.items():
        assert getattr(s, attr) == fb


def test_validate_keeps_valid_colors(qapp):
    s = ColorSettings()
    s.bg_color = "#aabbcc"  # 合法
    s.note_color = "invalid"
    s.validate()
    assert s.bg_color == "#aabbcc"  # 合法值保留
    assert s.note_color == "#6c6c6c"  # 非法值回退
