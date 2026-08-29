# ustreader.py — UST 文件解析器
"""UTAU Sequence Text (UST) 文件解析模块。

通过 UstFileReader 类实现 contracts.UstParser 接口，
供播放器和主窗口经 AppContext 统一调用。
"""

import math
from typing import Optional

from ustplayer.core.contracts import NoteInfo, UstInfo
from ustplayer.core.log import logger


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


def _parse_pitch_values(value: str) -> list:
    """把 PBS/PBY 这类可含小数的音分值拆成整数列表（四舍五入）。"""
    result: list = []
    if not value.strip():
        return result
    for num_str in value.split(","):
        try:
            result.append(round(float(num_str.strip())))
        except (ValueError, TypeError):
            pass
    return result


def _finalize_pitch_bend(note: NoteInfo) -> NoteInfo:
    """兼容 OpenUtau 风格音高曲线。

    传统 UST 直接给 PitchBend=数值列表；OpenUtau 等工具输出 PBS/PBW/PBY/PBM。
    此处把 PBS（起点音分）+ PBY（后续点音分）合并成播放器/渲染器使用的
    pitch_bend 点序列；PBW/PBM 只影响插值曲线形状，当前按均匀点近似，
    因此仅解析不参与合成。
    """
    if note.pitch_bend:
        _discard_pitch_parts(note)
        return note
    pby = _parse_pitch_values(getattr(note, "_pby", ""))
    if not pby:
        _discard_pitch_parts(note)
        return note
    start = _parse_pitch_values(getattr(note, "_pbs", ""))
    note.pitch_bend = ([start[0]] if start else [0]) + pby
    _discard_pitch_parts(note)
    return note


def _discard_pitch_parts(note: NoteInfo) -> None:
    for attr in ("_pbs", "_pbw", "_pby", "_pbm"):
        if hasattr(note, attr):
            delattr(note, attr)


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
        # UTF-8 家族统一用 utf-8-sig 打开：自动吞掉 BOM，避免 \ufeff 残留在首行
        # 导致 [#VERSION]/[#SETTING]/首个音符段匹配失败、首段数据被静默丢弃。
        if enc.lower().replace("-", "").replace("_", "") in ("utf8", "utf8sig"):
            enc = "utf-8-sig"

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
                        note_list.append(_finalize_pitch_bend(current_note))
                        current_note = None
                    continue

                if line == "[#SETTING]":
                    in_setting = True
                    expect_version = False
                    if current_note is not None:
                        note_list.append(_finalize_pitch_bend(current_note))
                        current_note = None
                    continue

                # 音符段，形如 [#0000]
                if line.startswith("[#") and line.endswith("]") and line[2:-1].isdigit():
                    in_setting = False
                    expect_version = False
                    if current_note is not None:
                        note_list.append(_finalize_pitch_bend(current_note))
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
                    elif key in ("PBS", "PBW", "PBY", "PBM"):
                        # OpenUtau 风格音高曲线原始字段：先暂存，音符结束时合成
                        setattr(current_note, f"_{key.lower()}", value)

        if current_note is not None:
            note_list.append(_finalize_pitch_bend(current_note))

        # 速度值边界校验：0 / 负数 / NaN / Inf 都会让下游时间轴失效
        # （0 → 时间轴永远停在第 0 tick；NaN/Inf → 比较行为怪异），统一回退默认。
        if not (math.isfinite(ust_tempo) and ust_tempo > 0):
            logger.warning(f"UST 速度值非法（{ust_tempo}），回退默认 120 BPM")
            ust_tempo = 120.0

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
    logger.info("=== UST 提取结果 ===")
    logger.info(f"版本：{info.version}")
    logger.info(f"速度：{info.tempo} BPM")
    logger.info(f"轨道数：{info.tracks}")
    logger.info(f"音符列表（共 {len(info.notes)} 个）：")
    for i, note in enumerate(info.notes):
        logger.info(
            f"  音符{i + 1}：歌词={note.lyric}，"
            f"音高={note.note_num}，"
            f"时长={note.length}，"
            f"PitchBend={len(note.pitch_bend)}点"
        )
