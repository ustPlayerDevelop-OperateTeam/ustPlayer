# test_settings_store.py — Settings.json 文件存取（Layer 1，无 Qt 依赖）
"""覆盖 core/settings_store.py：JSON 读写、原子写入、旧版 ini 迁移、损坏文件、路径解析。"""

import configparser

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
    # 模拟程序根目录不可写：回退到 %LOCALAPPDATA%\ustPlayer
    import ustplayer.core.settings_store as ss_mod
    monkeypatch.setattr(ss_mod.os, "access", lambda *a, **k: False)
    store = SettingsStore()
    expected = str(prog_root / "ustPlayer" / "Settings.json")
    assert store.settings_path == expected
