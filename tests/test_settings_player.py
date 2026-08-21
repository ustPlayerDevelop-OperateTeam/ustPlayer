# test_settings_player.py — 播放器样式设置子域（Layer 2，需 qapp）
"""覆盖 core/settings/player.py：PlayerSettings 的属性/信号/读写/校验 + migrate_value 旧值迁移。"""

import pytest

from ustplayer.core.settings import PlayerSettings

SECTION = "PlayerSettings"
LYRIC_SECTION = "LyricSettings"


# ===================== migrate_value（classmethod，无需 qapp） =====================

# (field, value, expected) — 旧中文值 / 已有英文值 / 未知值 都应正确迁移
_MIGRATE_CASES = [
    ("lyric_pos", "top", "top"),
    ("lyric_pos", "上", "top"),
    ("lyric_pos", "下", "bottom"),
    ("lyric_pos", "garbage", "top"),  # 未知值→默认
    ("silent_display", "r", "r"),
    ("silent_display", "R", "r"),
    ("silent_display", "-", "dash"),
    ("silent_display", "自定义文字", "custom"),
    ("silent_display", "什么都不显示", "none"),
    ("end_display", "end", "end"),
    ("end_display", "END", "end"),
    ("end_display", "-", "dash"),
    ("pitch_placeholder", "none", "none"),
    ("pitch_placeholder", "无", "none"),
    ("pitch_placeholder", "-", "dash"),
    ("pitch_placeholder", "自定义文字", "custom"),
]


def test_migrate_value_cases():
    for field, value, expected in _MIGRATE_CASES:
        assert PlayerSettings.migrate_value(field, value) == expected, (
            f"migrate_value({field!r}, {value!r}) 应为 {expected!r}"
        )


def test_migrate_value_invalid_field_raises():
    # 未登记的字段会抛 KeyError（_FIELDS 取值）
    with pytest.raises(KeyError):
        PlayerSettings.migrate_value("nope", "x")


# ===================== 属性 / 信号 / 读写 =====================

def test_defaults(qapp):
    s = PlayerSettings()
    assert s.lyric_pos == "top"
    assert s.silent_display == "r"
    assert s.silent_custom_text == ""
    assert s.end_display == "end"
    assert s.end_custom_text == ""
    assert s.pitch_placeholder == "none"
    assert s.pitch_custom_text == ""
    assert s.lrc_path == ""


def test_setters(qapp):
    s = PlayerSettings()
    s.lyric_pos = "bottom"
    s.silent_display = "dash"
    s.silent_custom_text = "休"
    s.end_display = "custom"
    s.end_custom_text = "完"
    s.pitch_placeholder = "dash"
    s.pitch_custom_text = "x"
    s.lrc_path = "C:/a.lrc"
    assert s.lyric_pos == "bottom"
    assert s.silent_display == "dash"
    assert s.silent_custom_text == "休"
    assert s.end_display == "custom"
    assert s.end_custom_text == "完"
    assert s.pitch_placeholder == "dash"
    assert s.pitch_custom_text == "x"
    assert s.lrc_path == "C:/a.lrc"


def test_signal_fires_on_change(qapp, slot):
    s = PlayerSettings()
    s.lyric_pos_changed.connect(slot)
    s.lyric_pos = "bottom"
    assert len(slot.calls) == 1
    assert slot.calls[0] == ("bottom",)


def test_signal_no_duplicate_on_same_value(qapp, slot):
    s = PlayerSettings()
    s.lyric_pos_changed.connect(slot)
    s.lyric_pos = "top"  # 同默认值
    assert len(slot.calls) == 0


def test_read_from_migrates_legacy_values(qapp):
    s = PlayerSettings()
    s.read_from({SECTION: {
        "lyric_pos": "上", "silent_display": "R", "silent_custom_text": "休",
        "end_display": "END", "end_custom_text": "完",
        "pitch_placeholder": "无", "pitch_custom_text": "x",
    }, LYRIC_SECTION: {"lrc_path": "C:/a.lrc"}})
    assert s.lyric_pos == "top"
    assert s.silent_display == "r"
    assert s.silent_custom_text == "休"
    assert s.end_display == "end"
    assert s.end_custom_text == "完"
    assert s.pitch_placeholder == "none"
    assert s.pitch_custom_text == "x"
    assert s.lrc_path == "C:/a.lrc"


def test_write_to_writes_both_sections(qapp):
    s = PlayerSettings()
    s.lyric_pos = "bottom"
    s.silent_display = "dash"
    s.lrc_path = "C:/a.lrc"
    config = {}
    s.write_to(config)
    assert config[SECTION]["lyric_pos"] == "bottom"
    assert config[SECTION]["silent_display"] == "dash"
    assert config[LYRIC_SECTION]["lrc_path"] == "C:/a.lrc"


def test_write_to_read_from_round_trip(qapp):
    s = PlayerSettings()
    s.lyric_pos = "bottom"
    s.silent_display = "custom"
    s.silent_custom_text = "休"
    s.end_display = "none"
    s.pitch_placeholder = "dash"
    s.lrc_path = "C:/a.lrc"
    config = {}
    s.write_to(config)

    s2 = PlayerSettings()
    s2.read_from(config)
    assert s2.lyric_pos == "bottom"
    assert s2.silent_display == "custom"
    assert s2.silent_custom_text == "休"
    assert s2.end_display == "none"
    assert s2.pitch_placeholder == "dash"
    assert s2.lrc_path == "C:/a.lrc"


# ===================== validate =====================

def test_validate_resets_invalid_enums(qapp):
    s = PlayerSettings()
    # setter 不校验，可直接写入非法值
    s.lyric_pos = "middle"
    s.silent_display = "x"
    s.end_display = "y"
    s.pitch_placeholder = "z"
    s.validate()
    assert s.lyric_pos == "top"
    assert s.silent_display == "r"
    assert s.end_display == "end"
    assert s.pitch_placeholder == "none"


def test_validate_keeps_valid_enums(qapp):
    s = PlayerSettings()
    s.lyric_pos = "bottom"
    s.silent_display = "dash"
    s.end_display = "none"
    s.pitch_placeholder = "custom"
    s.validate()
    assert s.lyric_pos == "bottom"
    assert s.silent_display == "dash"
    assert s.end_display == "none"
    assert s.pitch_placeholder == "custom"
