# test_settings_theme.py — 主题设置子域（Layer 2，需 qapp）
"""覆盖 core/settings/theme.py：ThemeSettings 的属性/信号/读写 + setter 自动纠正非法值。
主题设置不参与 .uplr 导入导出，但参与 Settings.json 持久化。"""

from ustplayer.core.settings import ThemeSettings

SECTION = "ThemeSettings"


def test_defaults(qapp):
    s = ThemeSettings()
    assert s.theme_mode == "auto"
    assert s.accent_color_mode == "auto"
    assert s.custom_accent_color == "#009faa"
    assert s.window_effect == "mica"


def test_setters_valid(qapp):
    s = ThemeSettings()
    s.theme_mode = "light"
    s.accent_color_mode = "custom"
    s.custom_accent_color = "#aabbcc"
    s.window_effect = "acrylic"
    assert s.theme_mode == "light"
    assert s.accent_color_mode == "custom"
    assert s.custom_accent_color == "#aabbcc"
    assert s.window_effect == "acrylic"


def test_setter_auto_corrects_invalid_theme_mode(qapp):
    s = ThemeSettings()
    s.theme_mode = "invalid"
    assert s.theme_mode == "auto"


def test_setter_auto_corrects_invalid_accent_mode(qapp):
    s = ThemeSettings()
    s.accent_color_mode = "invalid"
    assert s.accent_color_mode == "auto"


def test_setter_auto_corrects_invalid_color(qapp):
    s = ThemeSettings()
    s.custom_accent_color = "not-a-color"
    assert s.custom_accent_color == "#009faa"


def test_setter_auto_corrects_invalid_window_effect(qapp):
    s = ThemeSettings()
    s.window_effect = "invalid"
    assert s.window_effect == "mica"


def test_signal_fires_on_change(qapp, slot):
    s = ThemeSettings()
    s.theme_mode_changed.connect(slot)
    s.theme_mode = "dark"
    assert len(slot.calls) == 1
    assert slot.calls[0] == ("dark",)


def test_signal_no_duplicate_on_same_value(qapp, slot):
    s = ThemeSettings()
    s.theme_mode_changed.connect(slot)
    s.theme_mode = "auto"  # 同默认值
    assert len(slot.calls) == 0


def test_read_from_validates(qapp):
    s = ThemeSettings()
    s.read_from({SECTION: {
        "theme_mode": "dark", "accent_color_mode": "custom",
        "custom_accent_color": "#112233", "window_effect": "acrylic",
    }})
    assert s.theme_mode == "dark"
    assert s.accent_color_mode == "custom"
    assert s.custom_accent_color == "#112233"
    assert s.window_effect == "acrylic"


def test_read_from_resets_invalid(qapp):
    s = ThemeSettings()
    s.read_from({SECTION: {
        "theme_mode": "x", "accent_color_mode": "y",
        "custom_accent_color": "bad", "window_effect": "z",
    }})
    assert s.theme_mode == "auto"
    assert s.accent_color_mode == "auto"
    assert s.custom_accent_color == "#009faa"
    assert s.window_effect == "mica"


def test_write_to_read_from_round_trip(qapp):
    s = ThemeSettings()
    s.theme_mode = "dark"
    s.accent_color_mode = "custom"
    s.custom_accent_color = "#abcdef"
    s.window_effect = "none"
    config = {}
    s.write_to(config)

    s2 = ThemeSettings()
    s2.read_from(config)
    assert s2.theme_mode == "dark"
    assert s2.accent_color_mode == "custom"
    assert s2.custom_accent_color == "#abcdef"
    assert s2.window_effect == "none"
