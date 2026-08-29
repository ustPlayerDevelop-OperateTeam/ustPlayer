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


# ===================== ShowConfig 新字段传递 =====================

class TestShowConfigNewFields:
    def test_default_shows_all_visual_components(self, qapp):
        """默认 ShowConfig：音名/歌字/版权显示，无自定义字体（回归测试）。"""
        d = NoteLyricDisplay(PlayerLaunchParams())
        assert d.show_note_name is True
        assert d.show_ust_lyric is True
        assert d.show_copyright is True
        assert d.font_note == ""
        assert d.font_ust_lyric == ""
        assert d.font_lrc == ""
        assert d.font_other == ""
        assert d.custom_font_paths == []

    def test_custom_show_fields_passed_to_display(self, qapp):
        """ShowConfig 的显示开关、分槽字体与路径列表传递到播放器（回归测试）。"""
        params = PlayerLaunchParams()
        params.show.note_name = False
        params.show.ust_lyric = False
        params.show.copyright = False
        params.show.font_note = "黑体"
        params.show.font_ust_lyric = "华文黑体"
        params.show.font_lrc = "楷体"
        params.show.font_other = "宋体"
        params.show.custom_font_paths = ["C:/fonts/fake.ttf"]
        d = NoteLyricDisplay(params)
        assert d.show_note_name is False
        assert d.show_ust_lyric is False
        assert d.show_copyright is False
        assert d.font_note == "黑体"
        assert d.font_ust_lyric == "华文黑体"
        assert d.font_lrc == "楷体"
        assert d.font_other == "宋体"
        assert d.custom_font_paths == ["C:/fonts/fake.ttf"]


# ===================== 字体注册（_apply_custom_font） =====================

class TestApplyCustomFont:
    def test_invalid_or_empty_path_returns_none(self, qapp):
        """无效/空字体文件路径返回 None（下拉回退默认，不崩溃）。"""
        from ustplayer.ui.player_style_page import PlayerStylePage
        page = PlayerStylePage(__import__("ustplayer.context", fromlist=["AppContext"]).AppContext())
        assert page._apply_custom_font("") is None
        assert page._apply_custom_font("C:/not_exist_font.ttf") is None

    def test_valid_font_file_returns_family(self, qapp):
        """有效的 .ttf 文件能注册并解析出家族名。"""
        import glob
        from ustplayer.context import AppContext
        from ustplayer.ui.player_style_page import PlayerStylePage
        candidates = glob.glob(r"C:\Windows\Fonts\*.ttf")
        if not candidates:
            pytest.skip("本机无字体文件可测")
        page = PlayerStylePage(AppContext())
        assert page._apply_custom_font(candidates[0]) != ""



# ===================== 回归：LRC 多时间戳 / 负八度占位符 =====================

def test_parse_lrc_multiple_timestamps_and_missing_ms(qapp, tmp_path):
    """LRC 同一行多时间戳要全部保留，无毫秒 / 1 位毫秒也能解析。"""
    d = NoteLyricDisplay(PlayerLaunchParams())
    path = tmp_path / "multi.lrc"
    path.write_text(
        "[00:05.00][00:06.00]early\n[00:07]plain\n[00:08.5]tenth\n",
        encoding="utf-8",
    )
    d.lrc_path = str(path)
    d._parse_lrc()
    assert (5.0, "early") in d.lrc_lines
    assert (6.0, "early") in d.lrc_lines
    assert (7.0, "plain") in d.lrc_lines
    assert (8.5, "tenth") in d.lrc_lines


def test_negative_octave_respects_placeholder(display):
    """MIDI 0-11 的负八度也要应用 dash/custom 占位符规则。"""
    display.pitch_placeholder = "dash"
    assert display._get_pitch_text(0) == "C--1"
    display.pitch_placeholder = "custom"
    display.pitch_custom_text = "x"
    assert display._get_pitch_text(0) == "C(x)-1"
