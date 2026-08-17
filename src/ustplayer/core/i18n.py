# i18n.py — 多语言支持
"""加载 .qm 翻译，对外提供 tr() 翻译入口。中文是源语言，其它语言的 .qm 由 lupdate/lrelease 编译。"""

import os
from typing import Optional

from PySide6.QtCore import QCoreApplication, QLocale, QTranslator

from ustplayer.core.contracts import resolve_program_root
from ustplayer.core.log import logger

SUPPORTED_LANGUAGES = {
    "zh_CN": "简体中文",
    "zh_classic": "文言（华夏）",
    "en_US": "English",
}

DEFAULT_LANGUAGE = "zh_CN"

# QTranslator 必须保持存活，否则翻译立即失效
_translator: Optional[QTranslator] = None
_current_locale: str = DEFAULT_LANGUAGE


def current_locale() -> str:
    return _current_locale


def _qm_path(locale: str) -> str:
    base = resolve_program_root()
    candidates = [
        os.path.join(base, "i18n", f"ustplayer_{locale}.qm"),
        os.path.join(base, f"ustplayer_{locale}.qm"),
        os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
            "ustPlayer", "i18n", f"ustplayer_{locale}.qm",
        ),
    ]
    return next((p for p in candidates if os.path.exists(p)), candidates[0])


def install_translator(locale: str) -> None:
    global _translator, _current_locale
    app = QCoreApplication.instance()
    if app is None:
        return

    if _translator is not None:
        app.removeTranslator(_translator)
        _translator = None

    # 中文是源语言，无需 .qm
    if locale == DEFAULT_LANGUAGE:
        _current_locale = DEFAULT_LANGUAGE
        return

    path = _qm_path(locale)
    if not os.path.exists(path):
        logger.warning(f"翻译文件不存在，回退中文界面: {path}")
        _current_locale = DEFAULT_LANGUAGE
        return

    translator = QTranslator(app)
    if translator.load(path):
        app.installTranslator(translator)
        _translator = translator
        _current_locale = locale
        logger.info(f"已加载翻译: {path}")
    else:
        logger.warning(f"翻译文件加载失败，回退中文界面: {path}")
        _current_locale = DEFAULT_LANGUAGE


def system_locale() -> str:
    """跟随系统：返回与系统显示语言匹配的语言代码。"""
    name = QLocale.system().name()
    if name in SUPPORTED_LANGUAGES:
        return name
    # 按语言前缀匹配（zh_TW → zh_CN）；zh_classic 是文言文，任何系统都不会用它
    lang = name.split("_")[0]
    for code in SUPPORTED_LANGUAGES:
        if code != "zh_classic" and code.startswith(lang):
            return code
    return DEFAULT_LANGUAGE


def tr(source_text: str, context: str = "") -> str:
    """翻译入口。lupdate 只认名为 tr 的调用，context 默认空串与 .ts 匹配。

    未安装翻译器时原样返回（中文）。"""
    if not source_text:
        return source_text
    app = QCoreApplication.instance()
    if app is None or _translator is None:
        return source_text
    translated = QCoreApplication.translate(context, source_text)
    return translated if translated and translated != source_text else source_text