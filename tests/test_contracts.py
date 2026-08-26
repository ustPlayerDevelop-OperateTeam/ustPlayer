# test_contracts.py — 契约工具函数（Layer 1，无 Qt 依赖）
"""覆盖 core/contracts.py 的纯工具函数：颜色校验、RGB 转换、宽松布尔、程序根目录。"""

import os
import sys

from ustplayer.core.contracts import (
    APP_VERSION,
    as_bool,
    ensure_writable_dir,
    hex_to_rgb,
    is_valid_hex_color,
    resolve_program_root,
    validate_hex_color,
)


# ===================== is_valid_hex_color =====================

class TestIsValidHexColor:
    def test_valid_colors(self):
        assert is_valid_hex_color("#000000") is True
        assert is_valid_hex_color("#FFFFFF") is True
        assert is_valid_hex_color("#a1b2c3") is True
        assert is_valid_hex_color("#FF00ff") is True

    def test_invalid_colors(self):
        # 缺 #、长度不对、非法字符
        assert is_valid_hex_color("000000") is False
        assert is_valid_hex_color("#FFF") is False
        assert is_valid_hex_color("#GGGGGG") is False
        assert is_valid_hex_color("#1234567") is False
        assert is_valid_hex_color("") is False
        assert is_valid_hex_color("#12345G") is False

    def test_none_and_non_string(self):
        # 非 str 会先经 str() 转换，均不匹配（故意传非 str 验证 str() 兜底）
        assert is_valid_hex_color(None) is False  # pyright: ignore[reportArgumentType]
        assert is_valid_hex_color(123) is False  # pyright: ignore[reportArgumentType]

    def test_strips_whitespace(self):
        assert is_valid_hex_color("  #000000  ") is True


# ===================== validate_hex_color =====================

class TestValidateHexColor:
    def test_valid_returns_stripped(self):
        assert validate_hex_color("#000000") == "#000000"
        assert validate_hex_color("  #a1b2c3  ") == "#a1b2c3"

    def test_invalid_returns_default_fallback(self):
        assert validate_hex_color("bad") == "#FFFFFF"
        assert validate_hex_color("") == "#FFFFFF"

    def test_custom_fallback(self):
        assert validate_hex_color("bad", "#111111") == "#111111"
        assert validate_hex_color("#000000", "#111111") == "#000000"


# ===================== hex_to_rgb =====================

class TestHexToRgb:
    def test_primary_colors(self):
        assert hex_to_rgb("#000000") == (0, 0, 0)
        assert hex_to_rgb("#FF0000") == (255, 0, 0)
        assert hex_to_rgb("#00FF00") == (0, 255, 0)
        assert hex_to_rgb("#0000FF") == (0, 0, 255)
        assert hex_to_rgb("#FFFFFF") == (255, 255, 255)

    def test_mixed(self):
        assert hex_to_rgb("#6c6c6c") == (108, 108, 108)
        assert hex_to_rgb("#009faa") == (0, 159, 170)

    def test_without_hash_still_parses(self):
        # lstrip("#") 去掉 # 后照常解析
        assert hex_to_rgb("FF0000") == (255, 0, 0)

    def test_invalid_returns_white(self):
        assert hex_to_rgb("bad") == (255, 255, 255)
        assert hex_to_rgb(None) == (255, 255, 255)  # pyright: ignore[reportArgumentType]  # 故意传非 str 触发 except
        assert hex_to_rgb("") == (255, 255, 255)


# ===================== as_bool =====================

class TestAsBool:
    def test_bool_passthrough(self):
        assert as_bool(True) is True
        assert as_bool(False) is False

    def test_int_zero_nonzero(self):
        assert as_bool(0) is False
        assert as_bool(1) is True
        assert as_bool(2) is True
        assert as_bool(-1) is True

    def test_float(self):
        assert as_bool(0.0) is False
        assert as_bool(3.14) is True

    def test_truthy_strings(self):
        for v in ("1", "true", "TRUE", "yes", "YES", "on", "On"):
            assert as_bool(v) is True, v

    def test_falsy_strings(self):
        for v in ("0", "false", "no", "off", "", "anything"):
            assert as_bool(v) is False, v

    def test_none_uses_default(self):
        assert as_bool(None) is False
        assert as_bool(None, True) is True

    def test_default_with_invalid_string_ignored(self):
        # 字符串不在真值集合 → False，default 仅对 None 生效
        assert as_bool("garbage", True) is False


# ===================== resolve_program_root =====================

class TestResolveProgramRoot:
    def test_dev_mode_returns_argv0_dir(self):
        # 开发态：dirname(abspath(sys.argv[0]))
        expected = os.path.dirname(os.path.abspath(sys.argv[0]))
        assert resolve_program_root() == expected

    def test_frozen_returns_executable_dir(self, monkeypatch):
        # monkeypatch.setattr 在测试结束自动还原（包括原本不存在的属性）
        monkeypatch.setattr(sys, "frozen", True, raising=False)
        expected = os.path.dirname(os.path.abspath(sys.executable))
        assert resolve_program_root() == expected


# ===================== ensure_writable_dir =====================

class TestEnsureWritableDir:
    def test_creates_and_returns_true(self, tmp_path):
        # 目录不存在时自动创建，并确认真实可写
        d = tmp_path / "newdir"
        assert ensure_writable_dir(str(d)) is True
        assert d.is_dir()

    def test_existing_dir_true(self, tmp_path):
        assert ensure_writable_dir(str(tmp_path)) is True

    def test_no_probe_file_left_behind(self, tmp_path):
        assert ensure_writable_dir(str(tmp_path)) is True
        leftovers = [p for p in tmp_path.iterdir() if "probe" in p.name]
        assert leftovers == []

    def test_false_when_parent_is_file(self, tmp_path):
        # 父路径是文件 → makedirs 必败，返回 False 而非抛异常
        f = tmp_path / "file.txt"
        f.write_text("x", encoding="utf-8")
        assert ensure_writable_dir(str(f / "sub")) is False


# ===================== 版本常量 =====================

def test_app_version_constant():
    assert isinstance(APP_VERSION, str)
    assert APP_VERSION  # 非空
