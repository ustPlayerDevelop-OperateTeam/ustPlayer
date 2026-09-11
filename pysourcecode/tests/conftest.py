# conftest.py — 全局测试 fixture
"""pytest 全局夹具：QApplication 单例、程序根目录隔离、样本数据。

设计要点：
- QT_QPA_PLATFORM=offscreen 在导入 PySide6 前设置，确保无显示器环境（CI / 无头）
  也能构造 QApplication（player.py 的 QWidget 测试需要它）。
- qapp 为会话级单例，所有 Qt 测试共用，避免反复创建。
- prog_root 把 settings_store / settings_manager 的 resolve_program_root 重定向到
  tmp_path，并设置 LOCALAPPDATA，使 Settings.json / .uplr 缓存只落在临时目录里，
  不污染 pytest 工作目录。
"""

import os

# 必须在创建 QApplication 之前设置（也在导入任何会触发平台插件的代码之前）
os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

import pytest


# ===================== QApplication 单例 =====================

@pytest.fixture(scope="session")
def qapp():
    """会话级 QApplication 单例。用 QApplication（而非 QCoreApplication），
    以同时满足 QObject 子域测试与 player.py 的 QWidget 实例化需求。"""
    from PySide6.QtWidgets import QApplication

    app = QApplication.instance()
    if app is None:
        app = QApplication([])
    yield app


# ===================== 程序根目录 / 本地数据隔离 =====================

@pytest.fixture
def prog_root(monkeypatch, tmp_path):
    """把 resolve_program_root 重定向到 tmp_path，并设置 LOCALAPPDATA。

    供 settings_store / settings_manager / uplr_io 测试使用，确保它们读写
    的 Settings.json、.uplr 缓存目录都落在 tmp_path 下，互不干扰。
    """
    target = str(tmp_path)

    def _fake_root() -> str:
        return target

    # settings_store 与 settings_manager 各自 `from ... import resolve_program_root`，
    # 因此要在各自模块命名空间里替换引用
    import ustplayer.core.settings_store as settings_store_mod
    import ustplayer.core.settings_manager as settings_manager_mod

    monkeypatch.setattr(settings_store_mod, "resolve_program_root", _fake_root)
    monkeypatch.setattr(settings_manager_mod, "resolve_program_root", _fake_root)
    monkeypatch.setenv("LOCALAPPDATA", target)
    return tmp_path


@pytest.fixture
def make_manager(prog_root):
    """工厂：在 prog_root（已隔离的 tmp_path）下构造 SettingsManager。

    返回无参工厂函数；测试中可多次调用（每次都映射到同一 tmp_path）。
    如需第二个互不干扰的 SettingsManager，用 prog_root 子目录并自行 patch。
    """
    from ustplayer.core.settings_manager import SettingsManager

    def _make():
        return SettingsManager()
    return _make


# ===================== 信号记录 =====================

class _SignalRecorder:
    """可连接到 Qt Signal 的可调用对象，把每次发射的参数记入 .calls 列表。

    用带 __call__ 的类（而非给函数挂属性），既保证 PySide6 Signal.connect 接受，
    又让 .calls 是真正的实例属性（避免给 FunctionType 挂属性的类型检查告警）。
    """
    def __init__(self):
        self.calls: list = []

    def __call__(self, *args):
        self.calls.append(args)


@pytest.fixture
def slot():
    """返回一个可连接到 Qt Signal 的记录器。用法：
        s.signal_changed.connect(slot)
        s.value = x
        assert len(slot.calls) == 1
        assert slot.calls[0] == (x,)
    """
    return _SignalRecorder()


# ===================== UST 样本数据（inline 字符串，保证可移植） =====================

@pytest.fixture
def sample_ust_simple() -> str:
    """最小可用 UST：版本 + SETTING + 两个音符。"""
    return (
        "[#VERSION]\n"
        "UST Version1.2\n"
        "[#SETTING]\n"
        "Tempo=120.000\n"
        "Tracks=2\n"
        "[#0000]\n"
        "Length=480\n"
        "Lyric=do\n"
        "NoteNum=60\n"
        "[#0001]\n"
        "Length=480\n"
        "Lyric=re\n"
        "NoteNum=62\n"
    )


@pytest.fixture
def sample_ust_pitch() -> str:
    """带 PitchBend 的 UST。"""
    return (
        "[#SETTING]\n"
        "Tempo=90\n"
        "[#0000]\n"
        "Length=480\n"
        "Lyric=do\n"
        "NoteNum=60\n"
        "PitchBend=0,64,128,0,-64\n"
    )


@pytest.fixture
def ust_file(tmp_path, sample_ust_simple):
    """把 sample_ust_simple 写成 UTF-8 .ust 文件，返回路径字符串。"""
    path = tmp_path / "test.ust"
    path.write_text(sample_ust_simple, encoding="utf-8")
    return str(path)
