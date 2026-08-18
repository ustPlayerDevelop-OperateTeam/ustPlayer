# uplr_io.py — .uplr 工程文件导入/导出
"""新版 ZIP 容器（Info.json + 资源）与旧版纯文本格式的读写。"""

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
    def __init__(self, settings: "SettingsManager"):
        self._settings = settings

    def export_uplr(self, output_file: str):
        s = self._settings
        members = {}
        used_names = set()
        for attr, holder in (("ust_path", s.file), ("lrc_path", s.player), ("music_path", s.project)):
            local = getattr(holder, attr).strip()
            if not local or not os.path.exists(local):
                continue
            base = os.path.basename(local)
            name = base
            stem, ext = os.path.splitext(base)
            i = 2
            while name.lower() in used_names:
                name = f"{stem}_{i}{ext}"
                i += 1
            members[attr] = name
            used_names.add(name.lower())

        with zipfile.ZipFile(output_file, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr("Info.json", json.dumps(self._settings_to_info_json(members), ensure_ascii=False, indent=4))
            for attr, name in members.items():
                holder = {"ust_path": s.file, "lrc_path": s.player, "music_path": s.project}[attr]
                zf.write(getattr(holder, attr).strip(), arcname=name)

    def _settings_to_info_json(self, members: dict) -> dict:
        s = self._settings
        def name_or_none(attr):
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
                "show_bpm": int(s.display.show_bpm),
                "show_play_time": int(s.display.show_play_time),
                "show_song_name": int(s.display.show_song_name),
                "show_song_author": int(s.display.show_song_author),
                "show_ust_author": int(s.display.show_ust_author),
                "fullscreen": int(s.display.fullscreen),
                "show_lyric": int(s.display.show_lyric),
                "curve_show": int(s.file.curve_show),
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

    def import_uplr(self, input_file: str):
        with open(input_file, "rb") as f:
            head = f.read(4)
        if head.startswith(b"PK\x03\x04"):
            self._import_uplr_zip(input_file)
        else:
            self._import_uplr_text(input_file)

    def _import_uplr_text(self, input_file: str):
        s = self._settings
        str_keys = {
            "project_name": ("project", "project_name"), "ust_path": ("file", "ust_path"),
            "music_path": ("project", "music_path"), "song_name": ("project", "song_name"),
            "song_author": ("project", "song_author"), "ust_author": ("project", "ust_author"),
            "encoding": ("file", "encoding"),
            "bg_color": ("color", "bg_color"), "note_color": ("color", "note_color"),
            "lyric_color": ("color", "lyric_color"), "lyric_text_color": ("color", "lyric_text_color"),
            "other_text_color": ("color", "other_text_color"), "pitch_curve_color": ("color", "pitch_curve_color"),
            "lyric_pos": ("player", "lyric_pos"), "lrc_path": ("player", "lrc_path"),
            "silent_display": ("player", "silent_display"), "silent_custom_text": ("player", "silent_custom_text"),
            "end_display": ("player", "end_display"), "end_custom_text": ("player", "end_custom_text"),
            "pitch_placeholder": ("player", "pitch_placeholder"), "pitch_custom_text": ("player", "pitch_custom_text"),
        }
        bool_keys = {
            "show_bpm": ("display", "show_bpm"), "show_play_time": ("display", "show_play_time"),
            "show_song_name": ("display", "show_song_name"), "show_song_author": ("display", "show_song_author"),
            "show_ust_author": ("display", "show_ust_author"), "fullscreen": ("display", "fullscreen"),
            "show_lyric": ("display", "show_lyric"), "curve_show": ("file", "curve_show"),
        }
        truthy = ("1", "true", "yes", "on")

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

        migrate = s.player.migrate_value
        for line in content.splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split("=", 1)
            if len(parts) != 2:
                continue
            key, value = parts[0].strip(), parts[1].strip()
            if key in str_keys:
                holder, attr = str_keys[key]
                if attr in ("lyric_pos", "silent_display", "end_display", "pitch_placeholder"):
                    value = migrate(attr, value)
                setattr(getattr(s, holder), attr, value)
            elif key in bool_keys:
                holder, attr = bool_keys[key]
                setattr(getattr(s, holder), attr, value.lower() in truthy)

        s.sanitize()
        for attr, holder in (("ust_path", s.file), ("lrc_path", s.player), ("music_path", s.project)):
            p = getattr(holder, attr, "").strip()
            if p and not os.path.exists(p):
                logger.warning(f"旧版 .uplr 路径在本机不存在: {p}")

    def _import_uplr_zip(self, input_file: str):
        cache_dir = self._uplr_cache_dir(input_file)
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
        s = self._settings
        def resolve(name):
            return os.path.join(base_dir, name) if name else ""

        migrate = s.player.migrate_value
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
        s.file.curve_show = as_bool(display.get("curve_show", else_.get("curve_show")), False)

        s.color.bg_color = color.get("bg_color") or "#000000"
        s.color.note_color = color.get("note_color") or "#6c6c6c"
        s.color.lyric_color = color.get("lyric_color") or "#FFFFFF"
        s.color.lyric_text_color = color.get("lyric_text_color") or "#FFFFFF"
        s.color.other_text_color = color.get("other_text_color") or "#FFFFFF"
        s.color.pitch_curve_color = color.get("pitch_curve_color") or "#FFFFFF"

        s.player.lyric_pos = migrate("lyric_pos", else_.get("lyric_pos") or "top")
        s.player.lrc_path = resolve(else_.get("lrc_path") or "")
        s.player.silent_display = migrate("silent_display", else_.get("silent_display") or "r")
        s.player.silent_custom_text = else_.get("silent_custom_text") or ""
        s.player.end_display = migrate("end_display", else_.get("end_display") or "end")
        s.player.end_custom_text = else_.get("end_custom_text") or ""
        s.player.pitch_placeholder = migrate("pitch_placeholder", else_.get("pitch_placeholder") or "none")
        s.player.pitch_custom_text = else_.get("pitch_custom_text") or ""

    @staticmethod
    def _uplr_cache_dir(uplr_path: str) -> str:
        stem = os.path.splitext(os.path.basename(uplr_path))[0]
        digest = hashlib.sha1(os.path.abspath(uplr_path).encode("utf-8")).hexdigest()[:8]
        base = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ustPlayer", "projects")
        return os.path.join(base, f"{stem}-{digest}")

    @staticmethod
    def _extract_member_safe(zf, name: str, dest_dir: str):
        normalized = name.replace("\\", "/")
        if normalized.startswith("/") or ".." in normalized.split("/"):
            raise ValueError(f"工程文件包含不安全路径: {name}")
        target = os.path.join(dest_dir, normalized)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with zf.open(name) as src, open(target, "wb") as dst:
            dst.write(src.read())