# test_settings_store.py — Settings.json 文件存取（Layer 1，无 Qt 依赖）
"""覆盖 core/settings_store.py：JSON 读写、原子写入、旧版 ini 迁移、损坏文件、路径解析。"""

import configparser
import os

from ustplayer.core.settings_store import SettingsStore


# ===================== 基本读写 =====================

def test_load_empty_when_no_file(prog_root):
    store = SettingsStore()
    assert store.load() == {}


def test_save_and_load_round_trip(prog_root):
    store = SettingsStore()
    config = {"ProjectSettings": {"project_name": "demo", "song_name": "曲"}}
    store.save(config)
    assert store.load() == config


def test_save_atomic_replace_no_tmp_left(prog_root):
    store = SettingsStore()
    store.save({"A": {"k": "v"}})
    # 临时文件已被 os.replace 替换掉，不应残留
    assert not (prog_root / "Settings.json.tmp").exists()
    assert (prog_root / "Settings.json").exists()


# ===================== 旧版 ini 迁移 =====================

def test_migrate_legacy_ini(prog_root):
    # 构造一个旧版 Settings.ini
    ini_path = prog_root / "Settings.ini"
    parser = configparser.ConfigParser()
    parser["ProjectSettings"] = {"project_name": "legacy", "song_name": "旧曲"}
    parser["FileSettings"] = {"ust_path": "C:/x.ust", "encoding": "UTF-8"}
    with open(ini_path, "w", encoding="utf-8") as f:
        parser.write(f)

    store = SettingsStore()
    data = store.load()

    # 迁移后：旧文件删除、新 JSON 创建、内容正确
    assert not ini_path.exists()
    assert (prog_root / "Settings.json").exists()
    assert data["ProjectSettings"]["project_name"] == "legacy"
    assert data["FileSettings"]["encoding"] == "UTF-8"


# ===================== 损坏文件容错 =====================

def test_load_corrupted_json_returns_empty(prog_root):
    (prog_root / "Settings.json").write_text("{ 不是合法 JSON", encoding="utf-8")
    store = SettingsStore()
    assert store.load() == {}


def test_load_non_dict_json_returns_empty(prog_root):
    # JSON 合法但不是 dict（如列表），按空配置处理
    (prog_root / "Settings.json").write_text("[1, 2, 3]", encoding="utf-8")
    store = SettingsStore()
    assert store.load() == {}


# ===================== 路径解析 =====================

def test_resolve_path_in_writable_root(prog_root):
    store = SettingsStore()
    assert store.settings_path == str(prog_root / "Settings.json")


def test_resolve_path_fallback_when_readonly(prog_root, monkeypatch):
    # 模拟程序根目录不可写（写探针失败）：回退到 %LOCALAPPDATA%\ustPlayer
    import ustplayer.core.settings_store as ss_mod
    monkeypatch.setattr(ss_mod, "ensure_writable_dir", lambda directory: False)
    store = SettingsStore()
    expected = str(prog_root / "ustPlayer" / "Settings.json")
    assert store.settings_path == expected


# ===================== 回归：旧版 ini 的 % 与 BOM =====================

def test_migrate_legacy_ini_with_percent_value(prog_root):
    """含未转义 % 的旧配置不应因 ConfigParser 插值而迁移失败。"""
    (prog_root / "Settings.ini").write_text(
        "[ProjectSettings]\nproject_name = 100% Pure\nsong_name = 曲\n",
        encoding="utf-8",
    )
    store = SettingsStore()
    data = store.load()
    assert data["ProjectSettings"]["project_name"] == "100% Pure"
    assert not (prog_root / "Settings.ini").exists()


def test_load_json_with_utf8_bom(prog_root):
    """Windows 编辑器保存的 UTF-8 BOM 不能把整份设置判为损坏。"""
    (prog_root / "Settings.json").write_bytes(
        b'\xef\xbb\xbf{"ProjectSettings": {"project_name": "bom"}}'
    )
    store = SettingsStore()
    assert store.load()["ProjectSettings"]["project_name"] == "bom"


def test_save_readonly_target_falls_back_and_no_tmp_left(prog_root, monkeypatch):
    """目标 Settings.json 只读时保存应回退 LOCALAPPDATA 且不残留临时文件。"""
    import stat

    import ustplayer.core.settings_store as ss_mod

    preferred = prog_root / "Settings.json"
    preferred.write_text("{}", encoding="utf-8")
    preferred.chmod(stat.S_IREAD)
    monkeypatch.setenv("LOCALAPPDATA", str(prog_root / "local"))
    monkeypatch.setattr(ss_mod, "resolve_program_root", lambda: str(prog_root))

    store = SettingsStore()
    store.save({"A": {"k": "v"}})

    assert store.settings_path == str(prog_root / "local" / "ustPlayer" / "Settings.json")
    assert not (str(store.settings_path) + ".tmp") in os.listdir(
        os.path.dirname(store.settings_path)
    )
    assert store.load() == {"A": {"k": "v"}}
    preferred.chmod(stat.S_IWRITE)
