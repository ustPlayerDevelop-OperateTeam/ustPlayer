# uplr_io.py — .uplr 工程文件导入/导出
"""新版 ZIP 容器（Info.json + 资源）与旧版纯文本格式的读写。"""

import hashlib
import json
import os
import re
import shutil
import zipfile
from typing import TYPE_CHECKING

from ustplayer.core.contracts import as_bool, ensure_writable_dir
from ustplayer.core.log import logger

if TYPE_CHECKING:
    from ustplayer.core.settings_manager import SettingsManager


# .uprd 的 else 枚举用的是中文旧值；.uplr 存英文稳定 key。
# 与 PlayerSettings.migrate_value 保持一致（见 settings/player.py）。
_UPRD_LEGACY_ENUM = {
    "lyric_pos": {"上": "top", "下": "bottom"},
    "silent_display": {"R": "r", "-": "dash", "自定义文字": "custom", "什么都不显示": "none"},
    "end_display": {"END": "end", "-": "dash", "自定义文字": "custom", "什么都不显示": "none"},
    "pitch_placeholder": {"无": "none", "-": "dash", "自定义文字": "custom"},
}


def normalize_uprd_info(info: dict) -> dict:
    """把 .uprd 的 Info.json 归一化为 .uplr 兼容结构。

    .uprd ≈ .uplr + video 段 + music(music_path)，与 .uplr 的出入：
      - display.show_phoneme / show_midinote / show_waveform：.uprd 独有，渲染器无对应字段 → 移除；
      - curve_show：.uprd 放在 else → 移除（display 与 else 都不保留）；
      - color.pitch_curve_color：.uprd 缺失 → 补默认 #FFFFFF；
      - else 的枚举值：.uprd 用中文旧值（上 / R / END / 无 等）；
        .uplr 存英文稳定 key（top / r / end / none）→ 迁移成英文 key；
      - video{fps,height,width}：.uprd 独有段 → 原样保留（供渲染器取 width/height/fps）；
      - basic.music_path（music）→ 随 basic 原样保留。
    """
    display = dict(info.get("display") or {})
    else_ = dict(info.get("else") or {})
    for key in ("show_phoneme", "show_midinote", "show_waveform"):
        display.pop(key, None)
    display.pop("curve_show", None)
    else_.pop("curve_show", None)

    # 枚举值：中文旧值 → 英文稳定 key（已是英文 key 的保持不变）
    for field, legacy in _UPRD_LEGACY_ENUM.items():
        v = else_.get(field)
        if isinstance(v, str) and v in legacy:
            else_[field] = legacy[v]

    color = dict(info.get("color") or {})
    color.setdefault("pitch_curve_color", "#FFFFFF")
    return {
        "encoding": info.get("encoding") or "Shift-JIS",
        "basic": dict(info.get("basic") or {}),
        "display": display,
        "color": color,
        "else": else_,
        "video": dict(info.get("video") or {}),
    }


class UplrProjectIO:
    # 导入防护上限：Info.json 应极小；单成员 / 工程总量过大视为异常（防 zip bomb）
    _MAX_INFO_SIZE = 1024 * 1024          # 1MB
    _MAX_MEMBER_SIZE = 512 * 1024 * 1024  # 512MB
    _MAX_TOTAL_SIZE = 1024 * 1024 * 1024  # 1GB

    # .uplr 导入会触碰的全部设置属性（快照/回滚用），与 _apply_info_json 保持同步
    _IMPORT_TOUCHED = (
        ("file", ("encoding", "ust_path", "curve_show")),
        ("project", ("project_name", "music_path", "song_name", "song_author", "ust_author")),
        ("display", ("show_bpm", "show_play_time", "show_song_name", "show_song_author",
                     "show_ust_author", "fullscreen", "show_lyric")),
        ("color", ("bg_color", "note_color", "lyric_color", "lyric_text_color",
                   "other_text_color", "pitch_curve_color")),
        ("player", ("lyric_pos", "lrc_path", "silent_display", "silent_custom_text",
                    "end_display", "end_custom_text", "pitch_placeholder", "pitch_custom_text")),
    )

    def _snapshot_import_settings(self) -> dict:
        """快照 .uplr 导入会触碰的全部设置属性，供失败回滚。"""
        snap = {}
        for holder, attrs in self._IMPORT_TOUCHED:
            obj = getattr(self._settings, holder)
            for attr in attrs:
                snap[(holder, attr)] = getattr(obj, attr)
        return snap

    def _restore_import_settings(self, snapshot: dict) -> None:
        """按快照恢复设置（setter 触发信号，UI 自动同步回旧值）。"""
        for (holder, attr), value in snapshot.items():
            setattr(getattr(self._settings, holder), attr, value)

    def __init__(self, settings: "SettingsManager"):
        self._settings = settings

    def _collect_members(self) -> dict:
        """收集存在且非空的三个资源，返回 {uv_attr: 包内文件名}，重名自动 _2/_3 去重。"""
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
        return members

    def _write_members(self, zf, members: dict):
        """把已收集的资源写入 zip（arcname 为包内文件名）。"""
        s = self._settings
        for attr, name in members.items():
            holder = {"ust_path": s.file, "lrc_path": s.player, "music_path": s.project}[attr]
            zf.write(getattr(holder, attr).strip(), arcname=name)

    def export_uplr(self, output_file: str):
        members = self._collect_members()
        with zipfile.ZipFile(output_file, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr("Info.json", json.dumps(self._settings_to_info_json(members), ensure_ascii=False, indent=4))
            self._write_members(zf, members)

    def export_uprd(self, output_file: str, video: dict):
        """把当前工程导出为 .uprd（ZIP 容器：Info.json + 资源）。

        .uprd ≈ .uplr + video 段 + 额外的 display 波形/音名开关 + curve_show 置于 else。
        枚举值沿用存储层稳定的英文 key（与 .uplr 一致），导入时由 _apply_info_json 兼容。
        """
        members = self._collect_members()
        with zipfile.ZipFile(output_file, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr(
                "Info.json",
                json.dumps(self._settings_to_uprd_info(members, video), ensure_ascii=False, indent=4),
            )
            self._write_members(zf, members)

    def _settings_to_uprd_info(self, members: dict, video: dict) -> dict:
        """构造 .uprd 的 Info.json：在 .uplr 结构之上叠加 video 段与 uprd 独有字段。"""
        s = self._settings

        def name_or_none(attr):
            return members.get(attr) or None

        def enum(field, default):
            # .uprd 的枚举同样存英文稳定 key；值非法时回退默认
            value = getattr(s.player, field)
            valid = {
                "lyric_pos": ("top", "bottom"),
                "silent_display": ("r", "dash", "custom", "none"),
                "end_display": ("end", "dash", "custom", "none"),
                "pitch_placeholder": ("none", "dash", "custom"),
            }[field]
            return value if value in valid else default

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
                # uprd 独有：渲染器暂无对应字段，导出时默认关闭
                "show_phoneme": 0,
                "show_midinote": 0,
                "show_waveform": 0,
                "fullscreen": int(s.display.fullscreen),
                "show_lyric": int(s.display.show_lyric),
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
                "lyric_pos": enum("lyric_pos", "top"),
                "lrc_path": name_or_none("lrc_path"),
                "silent_display": enum("silent_display", "r"),
                "silent_custom_text": s.player.silent_custom_text or None,
                "end_display": enum("end_display", "end"),
                "end_custom_text": s.player.end_custom_text or None,
                "curve_show": int(s.file.curve_show),
                "pitch_placeholder": enum("pitch_placeholder", "none"),
                "pitch_custom_text": s.player.pitch_custom_text or None,
            },
            "video": {
                "width": int(video.get("width", 1920)),
                "height": int(video.get("height", 1080)),
                "fps": int(video.get("fps", 60)),
            },
        }

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

        snapshot = self._snapshot_import_settings()
        try:
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
        except BaseException:
            # 事务化：任一环节失败即回滚设置，避免部分篡改残留
            self._restore_import_settings(snapshot)
            raise

    def _import_uplr_zip(self, input_file: str):
        cache_dir = self._uplr_cache_dir(input_file)
        shutil.rmtree(cache_dir, ignore_errors=True)
        snapshot = self._snapshot_import_settings()
        try:
            with zipfile.ZipFile(input_file, "r") as zf:
                if "Info.json" not in zf.namelist():
                    raise ValueError("ZIP 工程文件缺少 Info.json")
                info_entry = zf.getinfo("Info.json")
                if info_entry.file_size > self._MAX_INFO_SIZE:
                    raise ValueError("Info.json 异常过大，已中止导入")
                info = json.loads(zf.read(info_entry).decode("utf-8"))
                total = 0
                for name in zf.namelist():
                    if name == "Info.json":
                        continue
                    total += self._extract_member_safe(zf, name, cache_dir)
                    if total > self._MAX_TOTAL_SIZE:
                        raise ValueError("工程文件解压总量超限，已中止导入")
            self._apply_info_json(info, cache_dir)
            self._settings.sanitize()
        except BaseException:
            # 事务化：回滚已改设置并清理半成品缓存，导入失败 ≠ 状态被污染
            self._restore_import_settings(snapshot)
            shutil.rmtree(cache_dir, ignore_errors=True)
            raise

    def _apply_info_json(self, info: dict, base_dir: str):
        s = self._settings
        def resolve(name):
            # Info.json 里的资源名与 ZIP 成员走同一套防护：只接受缓存目录内的
            # 纯相对路径，含 .. 穿越 / 绝对路径 / 盘符前缀的一律拒绝导入
            if not isinstance(name, str) or not name.strip():
                return ""
            return UplrProjectIO._safe_join(base_dir, name)

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

    # ===================== 工程缓存目录（程序目录下 cache/） =====================

    def cache_base(self) -> str:
        """缓存根目录：默认 <程序目录>/cache，程序目录不可写时回退 %LOCALAPPDATA%/ustPlayer/cache。

        可写性用真实写探针判断（Windows 的 os.access 不检查 ACL，会误报可写）。"""
        root = getattr(self._settings, "program_root", None) or os.getcwd()
        preferred = os.path.join(root, "cache")
        if os.path.isdir(preferred):
            # 目录已存在：确认它本身仍可写（ACL 可能事后收紧）
            return preferred if ensure_writable_dir(preferred) else self._fallback_cache_base()
        # 目录不存在：只要程序根可写就采用默认位置（首次导入时才真正创建）
        return preferred if ensure_writable_dir(root) else self._fallback_cache_base()

    @staticmethod
    def _fallback_cache_base() -> str:
        return os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ustPlayer", "cache"
        )

    def _uplr_cache_dir(self, uplr_path: str) -> str:
        stem = os.path.splitext(os.path.basename(uplr_path))[0]
        digest = hashlib.sha1(os.path.abspath(uplr_path).encode("utf-8")).hexdigest()[:8]
        return os.path.join(self.cache_base(), f"{stem}-{digest}")

    def cache_usage(self) -> int:
        """统计缓存目录占用字节数（不存在返回 0）。"""
        base = self.cache_base()
        total = 0
        if not os.path.isdir(base):
            return 0
        for dirpath, _dirnames, filenames in os.walk(base):
            for fname in filenames:
                try:
                    total += os.path.getsize(os.path.join(dirpath, fname))
                except OSError:
                    continue
        return total

    def clear_cache(self) -> None:
        """清空工程缓存目录（解压出的 .uplr/.uprd 资源）。"""
        base = self.cache_base()
        if os.path.isdir(base):
            shutil.rmtree(base, ignore_errors=True)
        logger.info(f"已清除工程缓存目录: {base}")

    @staticmethod
    def _safe_join(base_dir: str, member_name: str) -> str:
        """校验包内成员名并解析为 base_dir 内的绝对路径（防 zip slip / 路径穿越）。

        拒绝：空名、绝对路径、盘符前缀、NUL 字符、含 .. 组件；
        并以 commonpath 二次确认解析结果仍在 base_dir 内。
        """
        normalized = member_name.replace("\\", "/")
        if (
            not normalized
            or normalized.startswith("/")
            or "\x00" in normalized
            or ".." in normalized.split("/")
            or re.match(r"^[A-Za-z]:", normalized)
        ):
            raise ValueError(f"工程文件包含不安全路径: {member_name}")
        target = os.path.abspath(os.path.join(base_dir, *normalized.split("/")))
        abs_base = os.path.abspath(base_dir)
        try:
            inside = os.path.commonpath([abs_base, target]) == abs_base
        except ValueError:  # 不同盘符等无法比较的情况一律拒绝
            inside = False
        if not inside:
            raise ValueError(f"工程文件包含不安全路径: {member_name}")
        return target

    @staticmethod
    def _extract_member_safe(zf, name: str, dest_dir: str) -> int:
        """解压单个成员，分块流式写入并限制单成员大小；返回实际解压的字节数。"""
        # 先做路径安全校验（目录条目也校验，名为 ../x/ 的穿越条目同样拒绝）
        target = UplrProjectIO._safe_join(dest_dir, name)
        if name.replace("\\", "/").endswith("/"):
            # 目录条目：无需创建，子文件写入时 os.makedirs 会自动补父目录
            return 0
        os.makedirs(os.path.dirname(target), exist_ok=True)
        written = 0
        with zf.open(name) as src, open(target, "wb") as dst:
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                written += len(chunk)
                if written > UplrProjectIO._MAX_MEMBER_SIZE:
                    raise ValueError(f"工程文件成员过大，已中止导入: {name}")
                dst.write(chunk)
        return written
