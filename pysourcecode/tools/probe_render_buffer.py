# probe_render_buffer.py — Spike 0a：渲染器实时出帧性能探针
"""测量 uPlRender 的 up_render_to_buffer 单帧耗时，判定「播放器能不能用渲染器实时出帧」。

背景与目的（见迁移计划 Phase 2 / Spike 0）：
    1.1.x 的播放器用 QPainter 自绘，导出视频用 Rust 渲染器逐帧渲染——两套实现。
    2.0 计划让播放器直接显示渲染器出帧（Image + WriteableBitmap），从而：
      · 跨平台不必再解决 Avalonia 的字体/文本排版问题（排版在 Rust 侧）；
      · 「预览所见 = 导出所得」由同一实现保证。
    但这条路成立的前提是：软件渲染单帧要足够快，能撑住 60fps。

判定线：1080p 每帧 P95 ≤ 8ms（即 60fps 有 16.6ms 预算的一半余量）。
    若明显超标，回退到 Avalonia 自绘方案（额外 2–4 周），并记入 ADR 0001。

用法：
    uv run python tools/probe_render_buffer.py [DLL 路径] [选项]

    --width / --height    分辨率（默认依次测 1920x1080 与 3840x2160）
    --frames              每档采样帧数（默认 600）
    --notes               合成工程的音符数（默认 500，模拟真实工程）
    --warmup              预热帧数（默认 30，不计入统计）
    --json                以 JSON 输出结果（便于存档比对）

注意：本脚本只依赖标准库，可在 pysourcecode 的 venv 里直接运行。
"""

from __future__ import annotations

import argparse
import ctypes
import json
import math
import os
import statistics
import sys
import time
from ctypes import (
    POINTER,
    byref,
    c_char_p,
    c_double,
    c_int32,
    c_uint8,
    c_uint64,
)

# ===================== 默认参数 =====================

DEFAULT_DLL_CANDIDATES = [
    r"E:\code\uPlRender\target\release\ustplayer_renderer.dll",
    r"D:\Code\ustPlayer\renderer\ustplayer_renderer.dll",
    r"renderer\ustplayer_renderer.dll",
    r"ustplayer_renderer.dll",
]

UP_OK = 0


# ===================== 合成测试工程 =====================


def build_ust(notes: int, lyrics: tuple[str, ...]) -> dict:
    """合成一个 UST JSON（字段与 API_Docs.md 的 UstInfo 一致）。

    每个音符都带 PitchBend，覆盖渲染器最重的绘制路径（音高曲线）。
    """
    note_list = []
    for i in range(notes):
        # 每 8 个音符留一个休止符（触发 silent 文本分支）
        if i % 8 == 7:
            lyric = "R"
            pitch = []
        else:
            lyric = lyrics[i % len(lyrics)]
            # 5 个点的音高曲线，幅度 ±120 音分
            pitch = [0, 60, 120, 60, -60]
        note_list.append(
            {
                "index": f"{i:04d}",
                "length": 480,
                "lyric": lyric,
                "note_num": 60 + (i % 13),
                "phoneme": "",
                "pitch_bend": pitch,
            }
        )
    return {
        "version": "UST Version1.2",
        "tempo": 120.0,
        "tracks": 1,
        "notes": note_list,
    }


def build_config(ust: dict, width: int, height: int, fps: int) -> dict:
    """组装 up_set_config 需要的 RenderConfig（字段见 API_Docs.md）。"""
    return {
        "ust": ust,
        "show": {
            "bpm": True,
            "play_time": True,
            "song_name": True,
            "song_author": True,
            "ust_author": True,
            "lyric": True,
            "curve_show": True,
            "note_name": True,
            "ust_lyric": True,
            "copyright": True,
            "font_note": "",
            "font_ust_lyric": "",
            "font_lrc": "",
            "font_other": "",
            "custom_font_paths": [],
        },
        "project": {
            "project_name": "probe",
            "song_name": "探针测试曲",
            "song_author": "作者",
            "ust_author": "调音师",
        },
        "style": {
            "bg_color": "#000000",
            "note_color": "#6c6c6c",
            "lyric_color": "#FFFFFF",
            "lyric_text_color": "#FFFFFF",
            "other_text_color": "#FFFFFF",
            "lyric_pos": "top",
            "lrc_path": "",
            "music_path": "",
            "silent_display": "r",
            "silent_custom_text": "",
            "end_display": "end",
            "end_custom_text": "",
            "pitch_placeholder": "none",
            "pitch_custom_text": "",
            "pitch_curve_color": "#FFFFFF",
            "app_version": "2.0.0",
        },
        "width": width,
        "height": height,
        "fps": fps,
        "output_path": "",
    }


# ===================== DLL 绑定 =====================


class Renderer:
    """up_* 函数的 ctypes 薄封装（只包含探针需要的部分）。"""

    def __init__(self, dll_path: str):
        self.lib = ctypes.CDLL(dll_path)
        lib = self.lib

        lib.up_create_context.restype = c_uint64
        lib.up_create_context.argtypes = []

        lib.up_destroy_context.restype = None
        lib.up_destroy_context.argtypes = [c_uint64]

        lib.up_set_config.restype = c_int32
        lib.up_set_config.argtypes = [c_uint64, c_char_p]

        lib.up_set_ust_text.restype = c_int32
        lib.up_set_ust_text.argtypes = [c_uint64, c_char_p]

        lib.up_set_lrc_text.restype = c_int32
        lib.up_set_lrc_text.argtypes = [c_uint64, c_char_p]

        lib.up_render_to_buffer.restype = c_int32
        lib.up_render_to_buffer.argtypes = [
            c_uint64,
            c_double,
            POINTER(c_uint8),
            c_int32,
            POINTER(c_int32),
            POINTER(c_int32),
        ]

        lib.up_last_error.restype = c_char_p
        lib.up_last_error.argtypes = [c_uint64]

    def last_error(self, ctx: int) -> str:
        ptr = self.lib.up_last_error(ctx)
        return ptr.decode("utf-8", errors="replace") if ptr else ""

    def check(self, rc: int, ctx: int, stage: str) -> None:
        if rc != UP_OK:
            raise RuntimeError(f"{stage} 失败：rc={rc} {self.last_error(ctx)}")


# ===================== 测量 =====================


def measure(
    renderer: Renderer,
    ctx: int,
    width: int,
    height: int,
    frames: int,
    warmup: int,
    fps: int,
    call_overhead_ns: float,
) -> dict:
    """渲染 frames 帧，返回耗时统计（毫秒）。"""
    buf_len = width * height * 4
    buf = (c_uint8 * buf_len)()
    out_w = c_int32(0)
    out_h = c_int32(0)

    render_to_buffer = renderer.lib.up_render_to_buffer
    width_out = byref(out_w)
    height_out = byref(out_h)

    # 预热：首帧含字体加载与缓存构建，单列统计不混入稳态数据
    cold_start = None
    for i in range(warmup):
        t0 = time.perf_counter_ns()
        rc = render_to_buffer(ctx, i / fps, buf, buf_len, width_out, height_out)
        elapsed = time.perf_counter_ns() - t0
        renderer.check(rc, ctx, "render_to_buffer(warmup)")
        if i == 0:
            cold_start = elapsed

    if (out_w.value, out_h.value) != (width, height):
        raise RuntimeError(f"输出尺寸不符：期望 {width}x{height}，实得 {out_w.value}x{out_h.value}")

    samples = []
    for i in range(frames):
        t0 = time.perf_counter_ns()
        rc = render_to_buffer(ctx, (warmup + i) / fps, buf, buf_len, width_out, height_out)
        elapsed = time.perf_counter_ns() - t0
        renderer.check(rc, ctx, "render_to_buffer")
        samples.append(elapsed)

    total_ns = sum(samples)
    ms = [s / 1e6 for s in samples]
    ms_sorted = sorted(ms)

    def pct(p: float) -> float:
        if not ms_sorted:
            return 0.0
        idx = min(len(ms_sorted) - 1, max(0, math.ceil(p / 100.0 * len(ms_sorted)) - 1))
        return ms_sorted[idx]

    mean_ms = total_ns / len(samples) / 1e6
    overhead_ms = call_overhead_ns / 1e6

    return {
        "width": width,
        "height": height,
        "frames": frames,
        "cold_start_ms": (cold_start or 0) / 1e6,
        "mean_ms": mean_ms,
        "median_ms": statistics.median(ms),
        "p95_ms": pct(95),
        "p99_ms": pct(99),
        "max_ms": max(ms),
        "min_ms": min(ms),
        "sustained_fps": 1000.0 / mean_ms if mean_ms > 0 else 0.0,
        "host_overhead_ms": overhead_ms,
        "render_only_mean_ms": max(0.0, mean_ms - overhead_ms),
        "budget_60fps_ms": 1000.0 / 60.0,
    }


def measure_call_overhead(iterations: int = 20000) -> float:
    """测量一次 Python + ctypes 调用的固有开销（纳秒）。

    用于把宿主循环开销从单帧耗时里分离出去：本探针跑在 CPython 上，
    而真实宿主是 .NET + Avalonia，其调用开销与 CPython 不同，
    因此两个数字都要看。

    取一个极廉价的 C 函数作为基准（Windows 用 msvcrt 的 abs，
    其他平台用进程自身的 libc）。测量失败时返回 0，不影响主结论。
    """
    try:
        libc = ctypes.CDLL("msvcrt" if os.name == "nt" else None)
        func = libc.abs
        func.restype = c_int32
        func.argtypes = [c_int32]
    except (OSError, TypeError, AttributeError):
        return 0.0

    # 校准到至少几十毫秒，避免计时器分辨率影响
    t0 = time.perf_counter_ns()
    for i in range(iterations):
        func(i)
    elapsed = time.perf_counter_ns() - t0
    return elapsed / iterations


# ===================== 主流程 =====================


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="uPlRender 实时出帧性能探针（Spike 0a）")
    parser.add_argument("dll", nargs="?", help="ustplayer_renderer.dll 路径（缺省自动查找）")
    parser.add_argument("--frames", type=int, default=600, help="每档采样帧数")
    parser.add_argument("--warmup", type=int, default=30, help="预热帧数（不计入统计）")
    parser.add_argument("--notes", type=int, default=500, help="合成工程音符数")
    parser.add_argument(
        "--sizes",
        nargs="+",
        default=["1920x1080", "3840x2160"],
        help="待测分辨率列表，形如 1920x1080",
    )
    parser.add_argument("--json", action="store_true", help="以 JSON 输出")
    parser.add_argument("--budget-ms", type=float, default=8.0, help="1080p P95 判定线（毫秒）")
    return parser.parse_args(argv)


def resolve_dll(explicit: str | None) -> str:
    if explicit:
        if not os.path.isfile(explicit):
            raise SystemExit(f"指定的 DLL 不存在：{explicit}")
        return explicit
    for candidate in DEFAULT_DLL_CANDIDATES:
        if os.path.isfile(candidate):
            return candidate
    raise SystemExit(
        "未找到 ustplayer_renderer.dll，请显式传入路径。\n候选：\n  "
        + "\n  ".join(DEFAULT_DLL_CANDIDATES)
    )


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    dll_path = resolve_dll(args.dll)

    lyrics = ("あ", "い", "う", "え", "お", "か", "き", "く", "中文", "测", "试", "la", "li")

    if not args.json:
        print("=" * 72)
        print("Spike 0a：uPlRender 实时出帧探针")
        print("=" * 72)
        print(f"DLL            : {dll_path}")
        print(f"合成音符数     : {args.notes}")
        print(f"采样 / 预热    : {args.frames} / {args.warmup} 帧")
        print()

    renderer = Renderer(dll_path)
    overhead_ns = measure_call_overhead()

    results = []
    for size in args.sizes:
        try:
            width, height = (int(v) for v in size.lower().split("x"))
        except ValueError:
            raise SystemExit(f"无法解析分辨率：{size}（应形如 1920x1080）")

        ust = build_ust(args.notes, lyrics)
        config = build_config(ust, width, height, fps=60)

        ctx = renderer.lib.up_create_context()
        if not ctx:
            raise SystemExit("up_create_context 返回 0，创建渲染上下文失败")

        try:
            renderer.check(
                renderer.lib.up_set_config(ctx, json.dumps(config).encode("utf-8")),
                ctx,
                "set_config",
            )
            # LRC 覆盖一行多时间戳与无毫秒写法（1.1.x 已支持的边界情况）
            lrc = "[00:01.00][00:02.50]第一行\n[00:05]第二行\n[00:10.123]第三行"
            renderer.check(
                renderer.lib.up_set_lrc_text(ctx, lrc.encode("utf-8")), ctx, "set_lrc_text"
            )

            stats = measure(
                renderer,
                ctx,
                width,
                height,
                args.frames,
                args.warmup,
                fps=60,
                call_overhead_ns=overhead_ns,
            )
        finally:
            renderer.lib.up_destroy_context(ctx)

        results.append(stats)

        if not args.json:
            print(f"--- {width}x{height} ---")
            print(f"  冷启动首帧      : {stats['cold_start_ms']:.2f} ms")
            print(
                f"  单帧 mean/median: {stats['mean_ms']:.2f} / {stats['median_ms']:.2f} ms"
            )
            print(
                f"  单帧 p95/p99/max: {stats['p95_ms']:.2f} / {stats['p99_ms']:.2f} / "
                f"{stats['max_ms']:.2f} ms"
            )
            print(f"  可维持帧率      : {stats['sustained_fps']:.1f} fps")
            print(
                f"  其中宿主开销    : {stats['host_overhead_ms']:.3f} ms"
                f"（纯渲染约 {stats['render_only_mean_ms']:.2f} ms）"
            )
            verdict = (
                "通过"
                if stats["p95_ms"] <= args.budget_ms
                else "超标"
            )
            print(
                f"  判定（P95 ≤ {args.budget_ms:.1f} ms，60fps 预算 "
                f"{stats['budget_60fps_ms']:.2f} ms）: {verdict}"
            )
            print()

    if args.json:
        print(json.dumps({"dll": dll_path, "overhead_ms": overhead_ns / 1e6, "results": results},
                         ensure_ascii=False, indent=2))
        return 0

    # 汇总判定：以 1080p 为准
    primary = next((r for r in results if r["width"] == 1920 and r["height"] == 1080), results[0])
    passed = primary["p95_ms"] <= args.budget_ms
    print("=" * 72)
    print(
        f"结论（{primary['width']}x{primary['height']}）：P95 = {primary['p95_ms']:.2f} ms，"
        f"判定线 {args.budget_ms:.1f} ms → {'可实时出帧，Spike 0 通过' if passed else '超标，需回退自绘方案'}"
    )
    print("=" * 72)
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
