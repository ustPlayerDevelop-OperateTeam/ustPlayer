# test_player.py — NoteLyricDisplay 文本生成方法（Layer 2，需 offscreen qapp）
"""覆盖 player.py 中 NoteLyricDisplay 的纯逻辑实例方法：
_midi_to_note / _get_pitch_text / _get_silent_text / _get_end_text。

这些方法是实例方法（依赖 self 属性），需实例化 QWidget，因此归 Layer 2，
依赖 offscreen QApplication。format_play_time（模块级纯函数）见 test_player_utils.py。
"""

import pytest

from ustplayer.core.contracts import PlayerLaunchParams
from ustplayer.core.player import NoteLyricDisplay


@pytest.fixture
def display(qapp):
    """默认 PlayerLaunchParams 构造的播放器窗口（不 show，不启动定时器）。"""
    return NoteLyricDisplay(PlayerLaunchParams())


# ===================== _midi_to_note =====================

class TestMidiToNote:
    def test_middle_c(self, display):
        assert display._midi_to_note(60) == "C4"

    def test_c_sharp(self, display):
        assert display._midi_to_note(61) == "C#4"

    def test_a4(self, display):
        assert display._midi_to_note(69) == "A4"

    def test_zero(self, display):
        assert display._midi_to_note(0) == "C-1"

    def test_c0(self, display):
        assert display._midi_to_note(12) == "C0"

    def test_max(self, display):
        assert display._midi_to_note(127) == "G9"

    def test_invalid_returns_str(self, display):
        # 非数值触发 except，返回 str(midi_num)
        assert display._midi_to_note("x") == "x"


# ===================== _get_pitch_text =====================

class TestGetPitchText:
    def test_none_placeholder(self, display):
        display.pitch_placeholder = "none"
        assert display._get_pitch_text(60) == "C4"

    def test_dash_placeholder(self, display):
        display.pitch_placeholder = "dash"
        assert display._get_pitch_text(60) == "C-4"

    def test_custom_placeholder_with_text(self, display):
        display.pitch_placeholder = "custom"
        display.pitch_custom_text = "x"
        assert display._get_pitch_text(60) == "C(x)4"

    def test_custom_placeholder_empty_falls_back(self, display):
        display.pitch_placeholder = "custom"
        display.pitch_custom_text = ""
        # suffix 为空 → 退回 C4（不加括号）
        assert display._get_pitch_text(60) == "C4"

    def test_sharp_returned_as_is(self, display):
        # 升号音名不受占位符影响
        for placeholder in ("none", "dash", "custom"):
            display.pitch_placeholder = placeholder
            display.pitch_custom_text = "x"
            assert display._get_pitch_text(61) == "C#4", placeholder


# ===================== _get_silent_text =====================

class TestGetSilentText:
    def test_r(self, display):
        display.silent_display = "r"
        assert display._get_silent_text() == "R"

    def test_dash(self, display):
        display.silent_display = "dash"
        assert display._get_silent_text() == "-"

    def test_custom(self, display):
        display.silent_display = "custom"
        display.silent_custom_text = "休"
        assert display._get_silent_text() == "休"

    def test_none(self, display):
        display.silent_display = "none"
        assert display._get_silent_text() == ""


# ===================== _get_end_text =====================

class TestGetEndText:
    def test_end(self, display):
        display.end_display = "end"
        assert display._get_end_text() == "END"

    def test_dash(self, display):
        display.end_display = "dash"
        assert display._get_end_text() == "-"

    def test_custom(self, display):
        display.end_display = "custom"
        display.end_custom_text = "完"
        assert display._get_end_text() == "完"

    def test_none(self, display):
        display.end_display = "none"
        assert display._get_end_text() == ""


# ===================== _resolve_end_step（音频驱动的收尾判定） =====================

class TestResolveEndStep:
    """有音频时以『音频播完』为结束边界，内容结束后用空拍文字过渡。"""

    def test_no_audio_before_content(self, display):
        display._audio_ok = False
        display.total_tick = 960
        assert display._resolve_end_step(0) is None

    def test_no_audio_content_done_ends(self, display):
        display._audio_ok = False
        display.total_tick = 960
        assert display._resolve_end_step(960) == "end"

    def test_audio_not_finished_content_ok_is_none(self, display):
        display._audio_ok = True
        display._media_finished = False
        display.total_tick = 960
        assert display._resolve_end_step(0) is None

    def test_audio_not_finished_content_done_silent(self, display):
        display._audio_ok = True
        display._media_finished = False
        display.total_tick = 960
        assert display._resolve_end_step(960) == "silent"

    def test_audio_finished_content_done_ends(self, display):
        display._audio_ok = True
        display._media_finished = True
        display.total_tick = 960
        assert display._resolve_end_step(960) == "end"

    def test_audio_finished_content_not_done_is_none(self, display):
        display._audio_ok = True
        display._media_finished = True
        display.total_tick = 960
        assert display._resolve_end_step(0) is None

