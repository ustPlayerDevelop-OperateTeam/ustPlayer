# test_settings_language.py — 语言设置子域（Layer 2，需 qapp）
"""覆盖 core/settings/language.py：LanguageSettings 的属性/信号/读写 + effective_language。
语言偏好不入 .uplr，仅存 Settings.json 的 [LanguageSettings]。"""

from ustplayer.core.i18n import DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES
from ustplayer.core.settings import LanguageSettings

SECTION = "LanguageSettings"


def test_defaults(qapp):
    s = LanguageSettings()
    assert s.language == "system"


def test_set_valid_language(qapp):
    s = LanguageSettings()
    s.language = "en_US"
    assert s.language == "en_US"
    s.language = "zh_classic"
    assert s.language == "zh_classic"


def test_set_invalid_language_falls_back(qapp):
    s = LanguageSettings()
    s.language = "fr_FR"  # 不在支持列表
    assert s.language == "system"


def test_set_system_stays(qapp):
    s = LanguageSettings()
    s.language = "en_US"
    s.language = "system"
    assert s.language == "system"


def test_effective_language_when_system(qapp):
    s = LanguageSettings()
    # system → 跟随系统，结果应在支持列表内
    assert s.effective_language in SUPPORTED_LANGUAGES


def test_effective_language_when_explicit(qapp):
    s = LanguageSettings()
    s.language = "en_US"
    assert s.effective_language == "en_US"


def test_signal_fires_on_change(qapp, slot):
    s = LanguageSettings()
    s.language_changed.connect(slot)
    s.language = "en_US"
    assert len(slot.calls) == 1
    assert slot.calls[0] == ("en_US",)


def test_signal_no_duplicate_on_same_value(qapp, slot):
    s = LanguageSettings()
    s.language_changed.connect(slot)
    s.language = "system"  # 同默认值
    assert len(slot.calls) == 0


def test_read_from_valid(qapp):
    s = LanguageSettings()
    s.read_from({SECTION: {"language": "en_US"}})
    assert s.language == "en_US"


def test_read_from_invalid_falls_back(qapp):
    s = LanguageSettings()
    s.read_from({SECTION: {"language": "klingon"}})
    assert s.language == "system"


def test_write_to_read_from_round_trip(qapp):
    s = LanguageSettings()
    s.language = "en_US"
    config = {}
    s.write_to(config)
    assert config[SECTION]["language"] == "en_US"

    s2 = LanguageSettings()
    s2.read_from(config)
    assert s2.language == "en_US"


def test_default_language_is_zh_cn():
    # 中文是源语言
    assert DEFAULT_LANGUAGE == "zh_CN"
    assert "zh_CN" in SUPPORTED_LANGUAGES
