# uplr_io.py — .uplr 工程文件导入/导出
"""新版 ZIP 容器（Info.json + ust/lrc/music 资源）与旧版纯文本格式的读写。

实现 contracts.ProjectIO 接口，通过 SettingsManager 的子域访问设置属性
（导入时经 setter 触发信号同步 UI）。
"""

import hashlib
import json
import os
import shutil
import zipfile
from typing import TYPE_CHECKING

from ustplayer.core.contracts import as_bool
from ustplayer.core.log import logger

if TYPE_CHECKING:
    from ustplayer.core.settings_manager import SettingsManager


class UplrProjectIO:
    """.uplr 工程文件服务 — 实现 contracts.ProjectIO 接口。"""

    def __init__(self, settings: "SettingsManager"):
        self._settings = settings

    # ===================== 导出 =====================

    def export_uplr(self, output_file: str):
        """导出所有配置与资源到新版 .uplr（ZIP 容器）工程文件。

        资源文件（ust/lrc/music）存在时一并打包，Info.json 内路径记录包内文件名；
        缺失的资源对应 null。使用 ZIP_STORED（不压缩），flac 等已压缩格式体积不变。
        """
        s = self._settings
        members = {}  # 属性名 → 包内文件名
        used_names = set()
        for attr, holder in (("ust_path", s.file), ("lrc_path", s.player), ("music_path", s.project)):
            local = getattr(holder, attr).strip()
            if not local or not os.path.exists(local):
                continue
            base = os.path.basename(local)
            # 不同目录下同名资源去重，避免 ZIP 内条目互相覆盖
            name = base
            stem, ext = os.path.splitext(base)
            i = 2
            while name.lower() in used_names:
                name = f"{stem}_{i}{ext}"
                i += 1
            members[attr] = name
            used_names.add(name.lower())

        info = self._settings_to_info_json(members)

        with zipfile.ZipFile(output_file, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr("Info.json", json.dumps(info, ensure_ascii=False, indent=4))
            for attr, name in members.items():
                holder = {"ust_path": s.file, "lrc_path": s.player, "music_path": s.project}[attr]
                zf.write(getattr(holder, attr).strip(), arcname=name)

    def _settings_to_info_json(self, members: dict) -> dict:
        """当前设置 → Info.json 结构（路径字段写包内文件名，缺失为 None）。"""
        s = self._settings

        def name_or_none(attr: str):
            return members.get(attr) or None

        return {
            "encoding": s.file.encoding,
            "basic": {
                "project_name": s.project.project_name or None,
                "ust_path": name_or_none("ust_path"),
                "music_path": name_or_none("music_path"),
                "song_name": s.project.song_name or None,
                "song_author": s.project.song_author or None,
                "ust_author": s.project.ust_author or None,
            },
            "display": {
                "show_bpm": 1 if s.display.show_bpm else 0,
                "show_play_time": 1 if s.display.show_play_time else 0,
                "show_song_name": 1 if s.display.show_song_name else 0,
                "show_song_author": 1 if s.display.show_song_author else 0,
                "show_ust_author": 1 if s.display.show_ust_author else 0,
                "fullscreen": 1 if s.display.fullscreen else 0,
                "show_lyric": 1 if s.display.show_lyric else 0,
                "curve_show": 1 if s.file.curve_show else 0,
            },
            "color": {
                "bg_color": s.color.bg_color,
                "note_color": s.color.note_color,
                "lyric_color": s.color.lyric_color,
                "lyric_text_color": s.color.lyric_text_color,
                "other_text_color": s.color.other_text_color,
                "pitch_curve_color": s.color.pitch_curve_color,
            },
            "else": {
                "lyric_pos": s.player.lyric_pos,
                "lrc_path": name_or_none("lrc_path"),
                "silent_display": s.player.silent_display,
                "silent_custom_text": s.player.silent_custom_text or None,
                "end_display": s.player.end_display,
                "end_custom_text": s.player.end_custom_text or None,
                "pitch_placeholder": s.player.pitch_placeholder,
                "pitch_custom_text": s.player.pitch_custom_text or None,
            },
        }

    # ===================== 导入 =====================

    def import_uplr(self, input_file: str):
        """从 .uplr 工程文件导入全部配置（自动识别 ZIP / 旧文本格式）。"""
        with open(input_file, "rb") as f:
            head = f.read(4)
        if head.startswith(b"PK\x03\x04"):
            self._import_uplr_zip(input_file)
        else:
            self._import_uplr_text(input_file)

    # ===================== 旧版文本格式（仅导入兼容） =====================

    def _import_uplr_text(self, input_file: str):
        """解析旧版纯文本 .uplr（key=value）。"""
        s = self._settings
        # 字段映射：key → (子域名, 属性名)
        str_keys = {
            "project_name": ("project", "project_name"),
            "ust_path": ("file", "ust_path"),
            "music_path": ("project", "music_path"),
            "song_name": ("project", "song_name"),
            "song_author": ("project", "song_author"),
            "ust_author": ("project", "ust_author"),
            "encoding": ("file", "encoding"),
            "bg_color": ("color", "bg_color"),
            "note_color": ("color", "note_color"),
            "lyric_color": ("color", "lyric_color"),
            "lyric_text_color": ("color", "lyric_text_color"),
            "other_text_color": ("color", "other_text_color"),
            "pitch_curve_color": ("color", "pitch_curve_color"),
            "lyric_pos": ("player", "lyric_pos"),
            "lrc_path": ("player", "lrc_path"),
            "silent_display": ("player", "silent_display"),
            "silent_custom_text": ("player", "silent_custom_text"),
            "end_display": ("player", "end_display"),
            "end_custom_text": ("player", "end_custom_text"),
            "pitch_placeholder": ("player", "pitch_placeholder"),
            "pitch_custom_text": ("player", "pitch_custom_text"),
        }
        bool_keys = {
            "show_bpm": ("display", "show_bpm"),
            "show_play_time": ("display", "show_play_time"),
            "show_song_name": ("display", "show_song_name"),
            "show_song_author": ("display", "show_song_author"),
            "show_ust_author": ("display", "show_ust_author"),
            "fullscreen": ("display", "fullscreen"),
            "show_lyric": ("display", "show_lyric"),
            "curve_show": ("file", "curve_show"),
        }
        truthy = ("1", "true", "yes", "on")

        # 旧版文件可能是 GBK/Shift-JIS 编码，逐个尝试，全部失败才用 replace 兜底
        content = ""
        for enc in ("utf-8-sig", "utf-8", "gbk", "gb2312", "shift-jis"):
            try:
                with open(input_file, "r", encoding=enc) as f:
                    content = f.read()
                break
            except UnicodeDecodeError:
                continue
        if not content:
            with open(input_file, "r", encoding="utf-8", errors="replace") as f:
                content = f.read()

        for line in content.splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split("=", 1)
            if len(parts) != 2:
                continue
            key = parts[0].strip()
            value = parts[1].strip()

            if key in str_keys:
                holder, attr = str_keys[key]
                setattr(getattr(s, holder), attr, value)
            elif key in bool_keys:
                holder, attr = bool_keys[key]
                setattr(getattr(s, holder), attr, value.lower() in truthy)

        s.sanitize()
        # 旧版文本格式记录的是导出机器上的绝对路径，导入后通常在本机失效，仅提示
        for attr, holder in (("ust_path", s.file), ("lrc_path", s.player), ("music_path", s.project)):
            p = getattr(holder, attr, "").strip()
            if p and not os.path.exists(p):
                logger.warning(f"旧版 .uplr 中的 {attr} 路径在本机不存在（跨机器路径常见）: {p}")

    # ===================== 新版 ZIP 格式 =====================

    def _import_uplr_zip(self, input_file: str):
        """解析新版 ZIP .uplr：读取 Info.json 并把资源解压到缓存目录。"""
        cache_dir = self._uplr_cache_dir(input_file)
        # 先清空旧缓存，避免上次导入的同名资源残留混入
        shutil.rmtree(cache_dir, ignore_errors=True)
        with zipfile.ZipFile(input_file, "r") as zf:
            if "Info.json" not in zf.namelist():
                raise ValueError("ZIP 工程文件缺少 Info.json")
            info = json.loads(zf.read("Info.json").decode("utf-8"))
            for name in zf.namelist():
                if name == "Info.json":
                    continue
                self._extract_member_safe(zf, name, cache_dir)
        self._apply_info_json(info, cache_dir)
        self._settings.sanitize()

    def _apply_info_json(self, info: dict, base_dir: str):
        """Info.json → 设置。路径字段解析为缓存目录中的完整路径。"""
        s = self._settings

        def resolve(name):
            return os.path.join(base_dir, name) if name else ""

        basic = info.get("basic", {}) or {}
        display = info.get("display", {}) or {}
        color = info.get("color", {}) or {}
        else_ = info.get("else", {}) or {}

        s.file.encoding = info.get("encoding") or "Shift-JIS"
        s.project.project_name = basic.get("project_name") or ""
        s.file.ust_path = resolve(basic.get("ust_path") or "")
        s.project.music_path = resolve(basic.get("music_path") or "")
        s.project.song_name = basic.get("song_name") or ""
        s.project.song_author = basic.get("song_author") or ""
        s.project.ust_author = basic.get("ust_author") or ""

        s.display.show_bpm = as_bool(display.get("show_bpm"), True)
        s.display.show_play_time = as_bool(display.get("show_play_time"), True)
        s.display.show_song_name = as_bool(display.get("show_song_name"), True)
        s.display.show_song_author = as_bool(display.get("show_song_author"), True)
        s.display.show_ust_author = as_bool(display.get("show_ust_author"), True)
        s.display.fullscreen = as_bool(display.get("fullscreen"), True)
        s.display.show_lyric = as_bool(display.get("show_lyric"), False)
        # 样例将 curve_show 放在 else 分组，导出并入 display；导入时 display 优先、else 兜底
        s.file.curve_show = as_bool(
            display.get("curve_show", else_.get("curve_show")), False
        )

        s.color.bg_color = color.get("bg_color") or "#000000"
        s.color.note_color = color.get("note_color") or "#6c6c6c"
        s.color.lyric_color = color.get("lyric_color") or "#FFFFFF"
        s.color.lyric_text_color = color.get("lyric_text_color") or "#FFFFFF"
        s.color.other_text_color = color.get("other_text_color") or "#FFFFFF"
        s.color.pitch_curve_color = color.get("pitch_curve_color") or "#FFFFFF"

        s.player.lyric_pos = else_.get("lyric_pos") or "上"
        s.player.lrc_path = resolve(else_.get("lrc_path") or "")
        s.player.silent_display = else_.get("silent_display") or "R"
        s.player.silent_custom_text = else_.get("silent_custom_text") or ""
        s.player.end_display = else_.get("end_display") or "END"
        s.player.end_custom_text = else_.get("end_custom_text") or ""
        s.player.pitch_placeholder = else_.get("pitch_placeholder") or "无"
        s.player.pitch_custom_text = else_.get("pitch_custom_text") or ""

    # ===================== 缓存目录 =====================

    @staticmethod
    def _uplr_cache_dir(uplr_path: str) -> str:
        """计算 uplr 解压缓存目录：%LOCALAPPDATA%\\ustPlayer\\projects\\<stem>-<hash8>。"""
        stem = os.path.splitext(os.path.basename(uplr_path))[0]
        digest = hashlib.sha1(os.path.abspath(uplr_path).encode("utf-8")).hexdigest()[:8]
        base = os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
            "ustPlayer", "projects",
        )
        return os.path.join(base, f"{stem}-{digest}")

    @staticmethod
    def _extract_member_safe(zf: zipfile.ZipFile, name: str, dest_dir: str):
        """解压单个成员，阻止 zip slip（绝对路径 / .. 穿越）。"""
        normalized = name.replace("\\", "/")
        if normalized.startswith("/") or ".." in normalized.split("/"):
            raise ValueError(f"工程文件包含不安全路径: {name}")
        target = os.path.join(dest_dir, normalized)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with zf.open(name) as src, open(target, "wb") as dst:
            dst.write(src.read())
