# test_settings_project.py — 项目信息设置子域（Layer 2，需 qapp）
"""覆盖 core/settings/project.py：ProjectSettings 的属性/信号/分组读写。"""

from ustplayer.core.settings import ProjectSettings

SECTION = "ProjectSettings"
FIELDS = ["project_name", "song_name", "song_author", "ust_author", "music_path"]
SAMPLE = {
    "project_name": "demo",
    "song_name": "曲名",
    "song_author": "作曲",
    "ust_author": "调音",
    "music_path": "C:/m.wav",
}


def test_defaults(qapp):
    s = ProjectSettings()
    assert s.project_name == ""
    assert s.song_name == ""
    assert s.song_author == ""
    assert s.ust_author == ""
    assert s.music_path == ""


def test_setters(qapp):
    s = ProjectSettings()
    s.project_name = "demo"
    s.song_name = "曲名"
    s.song_author = "作曲"
    s.ust_author = "调音"
    s.music_path = "C:/m.wav"
    assert s.project_name == "demo"
    assert s.song_name == "曲名"
    assert s.song_author == "作曲"
    assert s.ust_author == "调音"
    assert s.music_path == "C:/m.wav"


def test_signal_fires_on_change(qapp, slot):
    s = ProjectSettings()
    s.project_name_changed.connect(slot)
    s.project_name = "demo"
    assert len(slot.calls) == 1
    assert slot.calls[0] == ("demo",)


def test_signal_no_duplicate_on_same_value(qapp, slot):
    s = ProjectSettings()
    s.project_name = "demo"
    s.project_name_changed.connect(slot)
    s.project_name = "demo"  # 同值，不应发信号
    assert len(slot.calls) == 0


def test_read_from(qapp):
    s = ProjectSettings()
    s.read_from({SECTION: SAMPLE})
    assert s.project_name == "demo"
    assert s.song_name == "曲名"
    assert s.song_author == "作曲"
    assert s.ust_author == "调音"
    assert s.music_path == "C:/m.wav"


def test_read_from_missing_section_noop(qapp):
    s = ProjectSettings()
    s.project_name = "keep"
    s.read_from({})  # 无该分组，保持原值
    assert s.project_name == "keep"


def test_write_to(qapp):
    s = ProjectSettings()
    for f in FIELDS:
        setattr(s, f, SAMPLE[f])
    config = {}
    s.write_to(config)
    assert config[SECTION] == SAMPLE


def test_write_to_read_from_round_trip(qapp):
    s = ProjectSettings()
    for f in FIELDS:
        setattr(s, f, SAMPLE[f])
    config = {}
    s.write_to(config)

    s2 = ProjectSettings()
    s2.read_from(config)
    for f in FIELDS:
        assert getattr(s2, f) == SAMPLE[f]
