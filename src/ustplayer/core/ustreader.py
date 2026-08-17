# ustreader.py — UST 文件解析器
"""UTAU Sequence Text (UST) 文件解析模块。

通过 UstFileReader 类实现 contracts.UstParser 接口，
供播放器和主窗口经 AppContext 统一调用。
"""

from typing import Optional

from ustplayer.core.contracts import NoteInfo, UstInfo, UstParser


def _parse_pitch_bend(value: str) -> list:
    """把 "1,2,3" 这种字符串拆成整数列表，非法项直接跳过。"""
    result: list = []
    if not value.strip():
        return result
    for num_str in value.split(","):
        try:
            result.append(int(num_str.strip()))
        except (ValueError, TypeError):
            pass
    return result


class UstFileReader:
    """UST 文件解析器 — 实现 UstParser 接口。"""

    # 默认编码与 SettingsManager 保持一致（日文 UST 通常用 Shift-JIS）
    DEFAULT_ENCODING = "Shift-JIS"

    def parse(self, ust_path: str, encoding: Optional[str] = None) -> UstInfo:
        """解析 UST 文件，返回版本、速度、轨道数和音符列表。

        Args:
            ust_path: .ust 文件路径
            encoding: 文件编码，默认 Shift-JIS（日文 UST 常用，中文/英文多用 UTF-8 或 GBK）

        Raises:
            FileNotFoundError: 文件不存在
            UnicodeDecodeError: 编码错误
        """
        enc = encoding or self.DEFAULT_ENCODING

        ust_version = ""
        ust_tempo = 120.0
        ust_tracks = 1
        note_list: list = []

        in_setting = False
        current_note: Optional[NoteInfo] = None
        expect_version = False

        with open(ust_path, "r", encoding=enc) as f:
            for raw_line in f:
                line = raw_line.strip()
                if not line:
                    continue

                if line == "[#VERSION]":
                    in_setting = False
                    expect_version = True
                    if current_note is not None:
                        note_list.append(current_note)
                        current_note = None
                    continue

                if line == "[#SETTING]":
                    in_setting = True
                    expect_version = False
                    if current_note is not None:
                        note_list.append(current_note)
                        current_note = None
                    continue

                # 音符段，形如 [#0000]
                if line.startswith("[#") and line.endswith("]") and line[2:-1].isdigit():
                    in_setting = False
                    expect_version = False
                    if current_note is not None:
                        note_list.append(current_note)
                    current_note = NoteInfo(index=line[2:-1])
                    continue

                # [#VERSION] 段的第一行有效内容即为版本号
                if expect_version and line.startswith("UST Version"):
                    ust_version = line
                    expect_version = False
                    continue

                if "=" not in line:
                    continue

                key, value = line.split("=", 1)
                key = key.strip()
                value = value.strip()

                if in_setting:
                    if key == "Tempo":
                        try:
                            ust_tempo = float(value)
                        except ValueError:
                            pass
                    elif key == "Tracks":
                        try:
                            ust_tracks = int(value)
                        except ValueError:
                            pass

                elif current_note is not None:
                    if key == "Length":
                        try:
                            current_note.length = int(value)
                        except ValueError:
                            pass
                    elif key == "Lyric":
                        current_note.lyric = value
                    elif key == "NoteNum":
                        try:
                            current_note.note_num = int(value)
                        except ValueError:
                            pass
                    elif key == "Phoneme":
                        current_note.phoneme = value
                    elif key == "PitchBend":
                        current_note.pitch_bend = _parse_pitch_bend(value)

        if current_note is not None:
            note_list.append(current_note)

        return UstInfo(
            version=ust_version,
            tempo=ust_tempo,
            tracks=ust_tracks,
            notes=note_list,
        )


# ===================== 独立测试 =====================
if __name__ == "__main__":
    import sys

    if len(sys.argv) > 1:
        path = sys.argv[1]
    else:
        path = "sample.ust"

    info = UstFileReader().parse(path, "UTF-8")
    print("=== UST 提取结果 ===")
    print(f"版本：{info.version}")
    print(f"速度：{info.tempo} BPM")
    print(f"轨道数：{info.tracks}")
    print(f"\n音符列表（共 {len(info.notes)} 个）：")
    for i, note in enumerate(info.notes):
        print(
            f"  音符{i + 1}：歌词={note.lyric}，"
            f"音高={note.note_num}，"
            f"时长={note.length}，"
            f"PitchBend={len(note.pitch_bend)}点"
        )
