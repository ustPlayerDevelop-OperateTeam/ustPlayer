# test_settings_file.py — 文件设置子域（Layer 2，需 qapp）
"""覆盖 core/settings/file.py：FileSettings 的属性/信号/读写 + 编码校验。"""

from ustplayer.core.settings import FileSettings

SECTION = "FileSettings"


def test_defaults(qapp):
    s = FileSettings()
    assert s.ust_path == ""
    assert s.encoding == "Shift-JIS"
    assert s.curve_show is False


def test_setters(qapp):
    s = FileSettings()
    s.ust_path = "C:/x.ust"
    s.encoding = "UTF-8"
    s.curve_show = True
    assert s.ust_path == "C:/x.ust"
    assert s.encoding == "UTF-8"
    assert s.curve_show is True


def test_signal_fires_on_change(qapp, slot):
    s = FileSettings()
    s.encoding_changed.connect(slot)
    s.encoding = "GBK"
    assert len(slot.calls) == 1
    assert slot.calls[0] == ("GBK",)


def test_signal_no_duplicate_on_same_value(qapp, slot):
    s = FileSettings()
    s.curve_show = True
    s.curve_show_changed.connect(slot)
    s.curve_show = True  # 同值不发信号
    assert len(slot.calls) == 0


def test_read_from_as_bool_compat(qapp):
    # curve_show 经 as_bool 宽松解析
    s = FileSettings()
    s.read_from({SECTION: {"ust_path": "C:/x.ust", "encoding": "GBK", "curve_show": "1"}})
    assert s.ust_path == "C:/x.ust"
    assert s.encoding == "GBK"
    assert s.curve_show is True

    s2 = FileSettings()
    s2.read_from({SECTION: {"curve_show": "yes"}})
    assert s2.curve_show is True

    s3 = FileSettings()
    s3.read_from({SECTION: {"curve_show": "0"}})
    assert s3.curve_show is False


def test_write_to_stores_bool_as_str(qapp):
    s = FileSettings()
    s.ust_path = "C:/x.ust"
    s.encoding = "GBK"
    s.curve_show = True
    config = {}
    s.write_to(config)
    assert config[SECTION]["curve_show"] == "1"
    assert config[SECTION]["encoding"] == "GBK"


def test_write_to_read_from_round_trip(qapp):
    s = FileSettings()
    s.ust_path = "C:/x.ust"
    s.encoding = "UTF-8"
    s.curve_show = True
    config = {}
    s.write_to(config)

    s2 = FileSettings()
    s2.read_from(config)
    assert s2.ust_path == "C:/x.ust"
    assert s2.encoding == "UTF-8"
    assert s2.curve_show is True


def test_validate_resets_invalid_encoding(qapp):
    s = FileSettings()
    s.encoding = "Latin-1"  # 非法编码
    s.validate()
    assert s.encoding == "Shift-JIS"


def test_validate_keeps_valid_encoding(qapp):
    for enc in ("UTF-8", "GBK", "Shift-JIS"):
        s = FileSettings()
        s.encoding = enc
        s.validate()
        assert s.encoding == enc
