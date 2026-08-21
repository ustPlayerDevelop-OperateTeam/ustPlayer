# test_ustreader.py — UST 解析器（Layer 1，无 Qt 依赖）
"""覆盖 core/ustreader.py：_parse_pitch_bend 纯函数 + UstFileReader.parse 全路径。"""

import pytest

from ustplayer.core.contracts import UstInfo
from ustplayer.core.ustreader import UstFileReader, _parse_pitch_bend


# ===================== _parse_pitch_bend =====================

class TestParsePitchBend:
    def test_empty_string(self):
        assert _parse_pitch_bend("") == []
        assert _parse_pitch_bend("   ") == []

    def test_single_value(self):
        assert _parse_pitch_bend("1") == [1]

    def test_multiple_values(self):
        assert _parse_pitch_bend("1,2,3") == [1, 2, 3]

    def test_negative_and_zero(self):
        assert _parse_pitch_bend("0,64,128,0,-64") == [0, 64, 128, 0, -64]

    def test_whitespace_around_items(self):
        assert _parse_pitch_bend(" 1 , 2 , 3 ") == [1, 2, 3]

    def test_invalid_items_skipped(self):
        assert _parse_pitch_bend("1,abc,3") == [1, 3]
        assert _parse_pitch_bend("x,y,z") == []


# ===================== UstFileReader.parse =====================

class TestParse:
    def test_basic_two_notes(self, ust_file):
        info = UstFileReader().parse(ust_file, "UTF-8")
        assert isinstance(info, UstInfo)
        assert info.version == "UST Version1.2"
        assert info.tempo == 120.0
        assert info.tracks == 2
        assert len(info.notes) == 2

        n0 = info.notes[0]
        assert n0.index == "0000"
        assert n0.length == 480
        assert n0.lyric == "do"
        assert n0.note_num == 60

        n1 = info.notes[1]
        assert n1.index == "0001"
        assert n1.lyric == "re"
        assert n1.note_num == 62

    def test_pitch_bend(self, tmp_path):
        path = tmp_path / "p.ust"
        path.write_text(
            "[#SETTING]\nTempo=90\n[#0000]\nLength=480\nLyric=do\nNoteNum=60\n"
            "PitchBend=0,64,128,0,-64\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.tempo == 90.0
        assert info.notes[0].pitch_bend == [0, 64, 128, 0, -64]

    def test_rest_note_R(self, tmp_path):
        path = tmp_path / "r.ust"
        path.write_text(
            "[#SETTING]\nTempo=120\n[#0000]\nLength=480\nLyric=R\nNoteNum=60\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.notes[0].lyric == "R"

    def test_extension_dash(self, tmp_path):
        path = tmp_path / "dash.ust"
        path.write_text(
            "[#SETTING]\n[#0000]\nLength=480\nLyric=-\nNoteNum=62\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.notes[0].lyric == "-"

    def test_empty_file(self, tmp_path):
        path = tmp_path / "empty.ust"
        path.write_text("", encoding="utf-8")
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.version == ""
        assert info.tempo == 120.0  # 默认
        assert info.tracks == 1    # 默认
        assert info.notes == []

    def test_no_setting_section_uses_defaults(self, tmp_path):
        path = tmp_path / "noset.ust"
        path.write_text("[#0000]\nLength=480\nLyric=do\nNoteNum=60\n", encoding="utf-8")
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.tempo == 120.0
        assert info.tracks == 1
        assert len(info.notes) == 1

    def test_phoneme_field(self, tmp_path):
        path = tmp_path / "phon.ust"
        path.write_text(
            "[#SETTING]\n[#0000]\nLength=480\nLyric=do\nNoteNum=60\nPhoneme=d o\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.notes[0].phoneme == "d o"

    def test_malformed_values_tolerated(self, tmp_path):
        path = tmp_path / "bad.ust"
        path.write_text(
            "[#SETTING]\nTempo=abc\nTracks=xyz\n[#0000]\n"
            "Length=notanum\nLyric=do\nNoteNum=nope\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        # 非法值被跳过，保持默认
        assert info.tempo == 120.0
        assert info.tracks == 1
        n = info.notes[0]
        assert n.length == 0
        assert n.note_num == 0
        assert n.lyric == "do"

    def test_empty_pitch_bend_value(self, tmp_path):
        path = tmp_path / "emptypb.ust"
        path.write_text(
            "[#SETTING]\n[#0000]\nLength=480\nLyric=do\nNoteNum=60\nPitchBend=\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.notes[0].pitch_bend == []

    def test_pitch_bend_with_invalid_items(self, tmp_path):
        path = tmp_path / "badpb.ust"
        path.write_text(
            "[#SETTING]\n[#0000]\nLength=480\nLyric=do\nNoteNum=60\nPitchBend=1,abc,2\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        assert info.notes[0].pitch_bend == [1, 2]

    def test_multiple_notes_order_preserved(self, tmp_path):
        path = tmp_path / "multi.ust"
        path.write_text(
            "[#SETTING]\n[#0000]\nLyric=do\n[#0001]\nLyric=re\n[#0002]\nLyric=mi\n",
            encoding="utf-8",
        )
        info = UstFileReader().parse(str(path), "UTF-8")
        assert [n.lyric for n in info.notes] == ["do", "re", "mi"]
        assert [n.index for n in info.notes] == ["0000", "0001", "0002"]

    def test_file_not_found_raises(self, tmp_path):
        with pytest.raises(FileNotFoundError):
            UstFileReader().parse(str(tmp_path / "nope.ust"), "UTF-8")

    def test_invalid_encoding_raises_unicode_decode_error(self, tmp_path):
        # 写入 UTF-8 无法解码的字节，用 UTF-8 读取应抛 UnicodeDecodeError
        path = tmp_path / "badenc.ust"
        path.write_bytes(b"\xff\xfe[#SETTING]\n")
        with pytest.raises(UnicodeDecodeError):
            UstFileReader().parse(str(path), "UTF-8")

    def test_shift_jis_file(self, tmp_path):
        # Shift-JIS 编码的日文 UST 也能正确解析
        path = tmp_path / "sjis.ust"
        content = "[#SETTING]\nTempo=120\n[#0000]\nLyric=あ\nNoteNum=60\n"
        path.write_text(content, encoding="shift-jis")
        info = UstFileReader().parse(str(path), "Shift-JIS")
        assert info.notes[0].lyric == "あ"

    def test_default_encoding_is_shift_jis(self, tmp_path):
        # 不传 encoding 时使用默认 Shift-JIS
        path = tmp_path / "def.ust"
        content = "[#SETTING]\n[#0000]\nLyric=あ\nNoteNum=60\n"
        path.write_text(content, encoding="shift-jis")
        info = UstFileReader().parse(str(path))
        assert info.notes[0].lyric == "あ"
