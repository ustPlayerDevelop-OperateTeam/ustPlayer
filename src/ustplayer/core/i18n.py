# i18n.py — 应用翻译基础设施（Qt Linguist）
"""多语言支持：加载 .qm 翻译文件并对外提供统一的翻译入口。

- `install_translator(locale)`：按语言代码（如 "zh_CN" / "en_US"）加载
  `i18n/ustplayer_<locale>.qm`（路径解析规则与 Settings.json 一致：
  程序根目录优先，回退 %LOCALAPPDATA%\\ustPlayer）；
- `tr()` / `_()`：全局翻译函数——所有用户可见字符串统一经此翻译；
- `language_changed` 信号：语言切换后发出，UI 层据此重译全部静态文本；
- 中文为源语言（源码中直接写中文），.qm 由 lupdate/lrelease 编译生成。
"""

import os
from typing import Optional

from PySide6.QtCore import QCoreApplication, QLocale, QObject, QTranslator, Signal

from ustplayer.core.contracts import resolve_program_root
from ustplayer.core.log import logger

# 当前支持的界面语言：语言代码 → 显示名（显示名本身不翻译，避免鸡生蛋问题）
SUPPORTED_LANGUAGES = {
    "zh_CN": "简体中文",
    "zh_classic": "文言（华夏）",
    "en_US": "English",
}

DEFAULT_LANGUAGE = "zh_CN"

# 全局翻译器引用（QTranslator 需要保持存活，否则翻译立即失效）
_translator: Optional[QTranslator] = None

# 当前生效的语言代码（install_translator 时更新，供播放器等独立窗口读取）
_current_locale: str = DEFAULT_LANGUAGE


def current_locale() -> str:
    """返回当前生效的语言代码（如 "zh_CN" / "en_US"）。"""
    return _current_locale


class LanguageManager(QObject):
    """语言管理单例：持有全局翻译器并广播语言切换信号。"""

    language_changed = Signal(str)

    _instance: Optional["LanguageManager"] = None

    def __new__(cls) -> "LanguageManager":
        if cls._instance is None:
            cls._instance = super().__new__(cls)
        return cls._instance

    def __init__(self):
        if getattr(self, "_initialized", False):
            return
        super().__init__()
        self._initialized = True
        self._current_language = DEFAULT_LANGUAGE

    @property
    def current_language(self) -> str:
        return self._current_language

    def set_language(self, locale: str) -> None:
        """切换界面语言并广播信号（UI 层监听后重译）。"""
        locale = locale if locale in SUPPORTED_LANGUAGES else DEFAULT_LANGUAGE
        install_translator(locale)
        self._current_language = locale
        self.language_changed.emit(locale)


def _qm_path(locale: str) -> str:
    """解析 ustplayer_<locale>.qm 的候选路径（程序根目录优先，回退用户数据目录）。"""
    base = resolve_program_root()
    candidates = [
        os.path.join(base, "i18n", f"ustplayer_{locale}.qm"),
        os.path.join(base, f"ustplayer_{locale}.qm"),
        os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
            "ustPlayer", "i18n", f"ustplayer_{locale}.qm",
        ),
    ]
    for path in candidates:
        if os.path.exists(path):
            return path
    return candidates[0]


def install_translator(locale: str) -> None:
    """安装（或替换）指定语言的翻译器。

    - zh_CN 是源语言，不需要 .qm，直接移除旧翻译器即可；
    - 其他语言加载 i18n/ustplayer_<locale>.qm；文件缺失时仅记日志，
      界面保持中文（源语言）兜底。
    """
    global _translator, _current_locale
    app = QCoreApplication.instance()
    if app is None:
        return

    # 先移除旧翻译器
    if _translator is not None:
        app.removeTranslator(_translator)
        _translator = None

    if locale == DEFAULT_LANGUAGE:
        _current_locale = DEFAULT_LANGUAGE
        logger.debug("界面语言：简体中文（源语言，无需翻译文件）")
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
    """返回与系统显示语言匹配的界面语言代码（跟随系统策略）。"""
    name = QLocale.system().name()  # 如 "zh_CN" / "en_US" / "ja_JP"
    if name in SUPPORTED_LANGUAGES:
        return name
    # 只匹配语言部分（如 "zh_TW" → "zh_CN" 兜底、未支持语言 → 默认中文）。
    # 文言文（zh_classic）不参与自动跟随——它不是任何系统的显示语言。
    lang = name.split("_")[0]
    for code in SUPPORTED_LANGUAGES:
        if code == "zh_classic":
            continue
        if code.startswith(lang):
            return code
    return DEFAULT_LANGUAGE


def tr(source_text: str, context: str = "") -> str:
    """翻译入口：UI 字符串统一经此函数。

    source_text 为源码中的中文原文；context 可传调用方类名提升消歧精度。
    lupdate 对模块级 tr() 调用生成的 context 为空字符串，因此默认用 "" 匹配。
    未安装翻译器时原样返回（中文）。
    """
    if not source_text:
        return source_text
    app = QCoreApplication.instance()
    if app is None or _translator is None:
        return source_text
    translated = QCoreApplication.translate(context, source_text)
    return translated if translated and translated != source_text else source_text


# 常用别名：页面内 `from ustplayer.core.i18n import tr as _` 使用
_ = tr
