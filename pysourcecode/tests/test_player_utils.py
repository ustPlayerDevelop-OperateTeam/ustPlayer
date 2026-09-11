# test_player_utils.py — player.py 模块级纯函数（Layer 1，无 Qt 依赖）
"""仅覆盖 player.py 中真正的模块级纯函数 format_play_time。
NoteLyricDisplay 的实例方法（_midi_to_note / _get_pitch_text 等）见 test_player.py。"""

from ustplayer.core.player import format_play_time


def test_zero():
    assert format_play_time(0) == "00:00:00"


def test_under_one_hour():
    # 61.5s → 01 分 01 秒 50 厘秒
    assert format_play_time(61.5) == "01:01:50"


def test_sub_second_centiseconds():
    # 0.5s → 00 分 00 秒 50 厘秒
    assert format_play_time(0.5) == "00:00:50"


def test_minutes_seconds_centiseconds():
    # 125.25s → 02 分 05 秒 25 厘秒
    assert format_play_time(125.25) == "02:05:25"


def test_over_one_hour_adds_hour_field():
    # 3661s → 01:01:01:00（超过一小时才带小时位）
    assert format_play_time(3661) == "01:01:01:00"


def test_over_one_hour_with_centiseconds():
    assert format_play_time(3661.5) == "01:01:01:50"


def test_large_value():
    # 36000s = 10 小时
    assert format_play_time(36000) == "10:00:00:00"


def test_invalid_input_returns_fallback():
    # 非数值输入触发 except 分支，返回兜底 "00:00:00"（故意传 str 验证兜底）
    assert format_play_time("abc") == "00:00:00"  # pyright: ignore[reportArgumentType]
