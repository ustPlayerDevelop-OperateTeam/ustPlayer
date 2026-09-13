"""抓取渲染器（uPlRender）在给定配置下的一帧，保存为 PNG。

用途：把「渲染器画了什么」与「宿主显示链路」「播放时序」彻底分开，
用于对比 2.0 播放器画面与 1.1.x QPainter 版式的差异。

用法（在仓库根目录运行）：
    uv run --project pysourcecode python pysourcecode/tools/dump_renderer_frame.py <输出.png> [选项]

选项：
    --seconds 0.19     渲染位置（秒）
    --width/--height   分辨率
    --ust <路径>       用真实 UST 文件（默认用合成工程）
    --curve-off        关闭音高曲线（用于确认 curve_show 的作用）
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import struct
import sys
import zlib
from ctypes import POINTER, byref, c_char_p, c_double, c_int32, c_uint8, c_uint64

DEFAULT_DLLS = [
    r"ustPlayer.Desktop\bin\Release\net10.0\renderer\ustplayer_renderer.dll",
    r"ustPlayer.Desktop\bin\Debug\net10.0\renderer\ustplayer_renderer.dll",
    r"E:\code\uPlRender\target\release\ustplayer_renderer.dll",
]


def resolve_dll(explicit: str | None) -> str:
    if explicit:
        return explicit
    for candidate in DEFAULT_DLLS:
        if os.path.isfile(candidate):
            return candidate
    raise SystemExit("未找到 ustplayer_renderer.dll，请用 --dll 指定")


def write_png(path: str, width: int, height: int, rgb: bytes) -> None:
    """把 RGB 字节写成 PNG（只用标准库，避免引入 Pillow 依赖）。"""

    def chunk(tag: bytes, data: bytes) -> bytes:
        payload = tag + data
        return struct.pack(">I", len(data)) + payload + struct.pack(">I", zlib.crc32(payload))

    raw = bytearray()
    stride = width * 3
    for y in range(height):
        raw.append(0)  # filter type 0
        raw += rgb[y * stride : (y + 1) * stride]

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 6))
    png += chunk(b"IEND", b"")

    with open(path, "wb") as handle:
        handle.write(png)


def build_ust_from_json(notes: int) -> dict:
    """合成工程：每个音符都带 5 点音高曲线（覆盖最重的绘制路径）。"""
    note_list = []
    for i in range(notes):
        rest = i % 8 == 7
        note_list.append(
            {
                "index": f"{i:04d}",
                "length": 480,
                "lyric": "R" if rest else "a",
                "note_num": 60 + (i % 13),
                "phoneme": "",
                "pitch_bend": [] if rest else [-60, 0, 60, 120, 60],
            }
        )
    return {"version": "UST Version1.2", "tempo": 120.0, "tracks": 1, "notes": note_list}


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    parser.add_argument("--dll")
    parser.add_argument("--seconds", type=float, default=0.19)
    parser.add_argument("--width", type=int, default=1920)
    parser.add_argument("--height", type=int, default=1080)
    parser.add_argument("--ust")
    parser.add_argument("--curve-off", action="store_true")
    parser.add_argument("--notes", type=int, default=20)
    args = parser.parse_args(argv)

    dll = resolve_dll(args.dll)
    lib = ctypes.CDLL(dll)
    lib.up_create_context.restype = c_uint64
    lib.up_set_config.argtypes = [c_uint64, c_char_p]
    lib.up_set_ust_text.argtypes = [c_uint64, c_char_p]
    lib.up_render_to_buffer.argtypes = [
        c_uint64, c_double, POINTER(c_uint8), c_int32, POINTER(c_int32), POINTER(c_int32),
    ]
    lib.up_destroy_context.argtypes = [c_uint64]

    if args.ust:
        from ustplayer.core.ustreader import UstFileReader

        ust = UstFileReader().parse(args.ust, encoding="Shift-JIS")
        ust_json = {
            "version": ust.version,
            "tempo": ust.tempo,
            "tracks": ust.tracks,
            "notes": [
                {
                    "index": n.index,
                    "length": n.length,
                    "lyric": n.lyric,
                    "note_num": n.note_number,
                    "phoneme": n.phoneme,
                    "pitch_bend": list(n.pitch_bend),
                }
                for n in ust.notes
            ],
        }
        print(f"UST: {args.ust}（音符 {len(ust.notes)} 个，"
              f"带 PitchBend 的 {sum(1 for n in ust.notes if n.pitch_bend)} 个）")
    else:
        ust_json = build_ust_from_json(args.notes)
        print(f"合成工程：{args.notes} 个音符，全部带 5 点音高曲线")

    config = {
        "ust": ust_json,
        "show": {
            "bpm": True, "play_time": True, "song_name": True, "song_author": True,
            "ust_author": True, "lyric": True,
            "curve_show": not args.curve_off,
            "note_name": True, "ust_lyric": True, "copyright": True,
            "font_note": "", "font_ust_lyric": "", "font_lrc": "", "font_other": "",
            "custom_font_paths": [],
        },
        "project": {
            "project_name": "探针", "song_name": "测试曲名",
            "song_author": "曲师", "ust_author": "调音师",
        },
        "style": {
            "bg_color": "#000000", "note_color": "#6c6c6c",
            "lyric_color": "#FFFFFF", "lyric_text_color": "#FFFFFF",
            "other_text_color": "#FFFFFF", "lyric_pos": "top",
            "lrc_path": "", "music_path": "",
            "silent_display": "r", "silent_custom_text": "",
            "end_display": "end", "end_custom_text": "",
            "pitch_placeholder": "none", "pitch_custom_text": "",
            "pitch_curve_color": "#FFFFFF", "app_version": "2.0.0",
        },
        "width": args.width, "height": args.height, "fps": 60, "output_path": "",
    }

    ctx = lib.up_create_context()
    if not ctx:
        raise SystemExit("up_create_context 失败")

    try:
        rc = lib.up_set_config(ctx, json.dumps(config).encode("utf-8"))
        if rc != 0:
            raise SystemExit(f"up_set_config 失败 rc={rc}")

        buf_len = args.width * args.height * 4
        buf = (c_uint8 * buf_len)()
        out_w, out_h = c_int32(0), c_int32(0)

        rc = lib.up_render_to_buffer(
            ctx, args.seconds, buf, buf_len, byref(out_w), byref(out_h)
        )
        if rc != 0:
            raise SystemExit(f"up_render_to_buffer 失败 rc={rc}")

        if (out_w.value, out_h.value) != (args.width, args.height):
            raise SystemExit(f"尺寸不符：{out_w.value}x{out_h.value}")

        # 渲染器输出 BGRA → PNG 需要 RGB
        rgb = bytearray(args.width * args.height * 3)
        for i in range(0, buf_len, 4):
            o = (i // 4) * 3
            rgb[o] = buf[i + 2]
            rgb[o + 1] = buf[i + 1]
            rgb[o + 2] = buf[i]

        non_black = sum(1 for i in range(0, len(rgb), 3)
                        if rgb[i] or rgb[i + 1] or rgb[i + 2])
        write_png(args.output, args.width, args.height, bytes(rgb))
        print(f"已保存：{args.output}（非黑像素 {non_black}）")
        return 0
    finally:
        lib.up_destroy_context(ctx)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
