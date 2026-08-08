# ustreader.py — UST 文件解析器（优化版）
"""UTAU Sequence Text (UST) 文件解析模块。

提取 UST 文件中的版本、速度、轨道数和音符信息，
供播放器和主窗口使用。
"""

from typing import List, Dict, Union


def _parse_pitch_bend(value: str) -> List[int]:
    """将 PitchBend 字符串解析为整数列表。

    Args:
        value: 逗号分隔的整数字符串，如 "-5,0,5,10"

    Returns:
        整数列表，解析失败的值被静默跳过
    """
    result: List[int] = []
    if not value.strip():
        return result
    for num_str in value.split(","):
        try:
            result.append(int(num_str.strip()))
        except (ValueError, TypeError):
            pass
    return result


def get_ust_info(ust_path: str, encoding: str = "UTF-8") -> Dict[str, Union[str, float, int, List[Dict]]]:
    """解析 UST 文件，提取版本、速度、轨道数和音符列表。

    Args:
        ust_path: UST 文件路径（.ust）
        encoding: 文件编码，默认 UTF-8。日文 UST 通常用 Shift-JIS

    Returns:
        dict::
            {
                "version": str,      # UST 版本号，如 "UST Version 1.20"
                "tempo": float,      # 速度 (BPM)，默认 120.0
                "tracks": int,       # 轨道数，默认 1
                "notes": list[dict]  # 音符列表，每项含:
                                     #   index, length, lyric,
                                     #   note_num, pitch_bend
            }

    Raises:
        FileNotFoundError: UST 文件不存在
        UnicodeDecodeError: 使用了错误的编码

    Example:
        >>> info = get_ust_info("song.ust", "Shift-JIS")
        >>> print(info["tempo"])
        120.0
        >>> print(len(info["notes"]))
        42
    """
    # ===== 初始化返回值 =====
    ust_version = ""
    ust_tempo = 120.0
    ust_tracks = 1
    note_list: List[Dict] = []

    # ===== 状态标记 =====
    in_setting = False
    current_note: Dict = {}

    # ===== 从 [#VERSION] 段提取版本的标记 =====
    # 原逻辑：遇到 [#VERSION] 时 set continue，下一行非空非注释时取版本号
    expect_version = False

    # ===== 打开文件（with 语句确保自动关闭） =====
    with open(ust_path, "r", encoding=encoding) as f:
        for raw_line in f:
            line = raw_line.strip()
            if not line:
                continue

            # ---- 分段标记识别 ----
            if line == "[#VERSION]":
                in_setting = False
                expect_version = True
                # 保存上一个音符
                if current_note:
                    note_list.append(current_note)
                    current_note = {}
                continue

            if line == "[#SETTING]":
                in_setting = True
                expect_version = False
                if current_note:
                    note_list.append(current_note)
                    current_note = {}
                continue

            # 音符段: [#0000], [#0001], ...
            if line.startswith("[#") and line.endswith("]") and line[2:-1].isdigit():
                in_setting = False
                expect_version = False
                if current_note:
                    note_list.append(current_note)
                current_note = {
                    "index": line[2:-1],
                    "length": 0,
                    "lyric": "",
                    "note_num": 0,
                    "pitch_bend": [],
                }
                continue

            # ---- 版本号（[#VERSION] 后的第一行有效内容） ----
            if expect_version and line.startswith("UST Version"):
                ust_version = line
                expect_version = False
                continue

            # ---- 键值对解析 ----
            if "=" not in line:
                continue

            key, value = line.split("=", 1)
            key = key.strip()
            value = value.strip()

            if in_setting:
                # [#SETTING] 段
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

            elif current_note:
                # 音符段（6个核心字段）
                if key == "Length":
                    try:
                        current_note["length"] = int(value)
                    except ValueError:
                        pass
                elif key == "Lyric":
                    current_note["lyric"] = value
                elif key == "NoteNum":
                    try:
                        current_note["note_num"] = int(value)
                    except ValueError:
                        pass
                elif key == "PitchBend":
                    current_note["pitch_bend"] = _parse_pitch_bend(value)

    # 最后一个音符
    if current_note:
        note_list.append(current_note)

    return {
        "version": ust_version,
        "tempo": ust_tempo,
        "tracks": ust_tracks,
        "notes": note_list,
    }


# ===================== 独立测试 =====================
if __name__ == "__main__":
    import sys

    if len(sys.argv) > 1:
        path = sys.argv[1]
    else:
        path = "sample.ust"

    info = get_ust_info(path, "UTF-8")
    print("=== UST 提取结果 ===")
    print(f"版本：{info['version']}")
    print(f"速度：{info['tempo']} BPM")
    print(f"轨道数：{info['tracks']}")
    print(f"\n音符列表（共 {len(info['notes'])} 个）：")
    for i, note in enumerate(info["notes"]):
        print(
            f"  音符{i + 1}：歌词={note['lyric']}，"
            f"音高={note['note_num']}，"
            f"时长={note['length']}，"
            f"PitchBend={len(note['pitch_bend'])}点"
        )
