# test_i18n.py — 国际化（Layer 2，需 qapp）
"""覆盖 core/i18n.py：tr 入口、system_locale、install_translator（中文源语言 / .qm 缺失回退）。

install_translator 会改 i18n 模块全局态（_translator / _current_locale），故用 autouse
夹具在每个测试前后调用 install_translator("zh_CN") 还原到中文源语言（同时清除已装翻译器）。
"""

import pytest

import ustplayer.core.i18n as i18n
from ustplayer.core.i18n import (
    DEFAULT_LANGUAGE,
    SUPPORTED_LANGUAGES,
    current_locale,
    install_translator,
    system_locale,
    tr,
)


@pytest.fixture(autouse=True)
def reset_i18n(qapp):
    """每个测试前后还原到中文源语言（无翻译器）。"""
    install_translator(DEFAULT_LANGUAGE)
    yield
    install_translator(DEFAULT_LANGUAGE)


# ===================== tr 入口 =====================

def test_tr_returns_source_when_no_translator(qapp):
    # 默认状态（zh_CN 源语言，无 _translator）→ 原样返回
    assert tr("中文原文") == "中文原文"


def test_tr_empty_string_passthrough(qapp):
    assert tr("") == ""


def test_tr_with_context_arg(qapp):
    # context 参数不报错，原样返回（无翻译器）
    assert tr("中文", "SomeContext") == "中文"


# ===================== system_locale =====================

def test_system_locale_in_supported(qapp):
    # 跟随系统，结果必在支持列表内（或默认中文）
    assert system_locale() in SUPPORTED_LANGUAGES


# ===================== install_translator =====================

def test_install_zh_cn_no_qm_loaded(qapp):
    install_translator("zh_CN")
    assert current_locale() == "zh_CN"
    assert i18n._translator is None  # 中文是源语言，不加载 .qm


def test_install_missing_qm_falls_back(qapp, monkeypatch, tmp_path):
    # 把 i18n 的 resolve_program_root 重定向到空目录，确保找不到 .qm
    monkeypatch.setattr(i18n, "resolve_program_root", lambda: str(tmp_path))
    install_translator("en_US")
    # .qm 不存在 → 回退中文
    assert current_locale() == DEFAULT_LANGUAGE
    assert i18n._translator is None


def test_install_invalid_locale_uses_qm_lookup(qapp, monkeypatch, tmp_path):
    # 非中文、非支持语言也会走 _qm_path 查找；找不到回退中文
    monkeypatch.setattr(i18n, "resolve_program_root", lambda: str(tmp_path))
    install_translator("klingon")  # 不在 SUPPORTED_LANGUAGES
    assert current_locale() == DEFAULT_LANGUAGE


# ===================== 常量 =====================

def test_supported_languages_contains_expected():
    assert "zh_CN" in SUPPORTED_LANGUAGES
    assert "en_US" in SUPPORTED_LANGUAGES
    assert "zh_classic" in SUPPORTED_LANGUAGES


def test_default_language():
    assert DEFAULT_LANGUAGE == "zh_CN"
