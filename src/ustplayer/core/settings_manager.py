# settings_manager.py — 配置管理器
"""Settings.ini 配置读写 + .uplr 工程文件导入/导出。

通过 Qt Signal 通知 UI 所有配置变更，替代 tkinter 的 StringVar/BooleanVar 机制。
UI 页面经 AppContext 获取本管理器实例，不直接构造。
"""

import configparser
import hashlib
import json
import os
import zipfile
from typing import Optional

from PySide6.QtCore import QObject, Signal

from ustplayer.core.contracts import (
    PlayerLaunchParams,
    PlayerStyle,
    ProjectInfo,
    ShowConfig,
    UstInfo,
    is_valid_hex_color,
    resolve_program_root,
)
from ustplayer.core.log import logger


class SettingsManager(QObject):
    """应用配置管理器，集中管理所有设置项。

    每个配置项对应一个属性，修改时发出对应的 Signal。
    UI 层通过 connect/setValue 模式绑定。
    """

    # ===================== 信号定义 =====================
    # 字符串信号
    ust_path_changed = Signal(str)
    project_name_changed = Signal(str)
    song_name_changed = Signal(str)
    song_author_changed = Signal(str)
    ust_author_changed = Signal(str)
    encoding_changed = Signal(str)
    bg_color_changed = Signal(str)
    note_color_changed = Signal(str)
    lyric_color_changed = Signal(str)
    lyric_text_color_changed = Signal(str)
    other_text_color_changed = Signal(str)
    pitch_curve_color_changed = Signal(str)
    lyric_pos_changed = Signal(str)
    lrc_path_changed = Signal(str)
    music_path_changed = Signal(str)
    silent_display_changed = Signal(str)
    silent_custom_text_changed = Signal(str)
    end_display_changed = Signal(str)
    end_custom_text_changed = Signal(str)
    pitch_placeholder_changed = Signal(str)
    pitch_custom_text_changed = Signal(str)

    # 布尔信号
    show_bpm_changed = Signal(bool)
    show_play_time_changed = Signal(bool)
    show_song_name_changed = Signal(bool)
    show_song_author_changed = Signal(bool)
    show_ust_author_changed = Signal(bool)
    fullscreen_changed = Signal(bool)
    show_lyric_changed = Signal(bool)
    curve_show_changed = Signal(bool)
    show_phoneme_changed = Signal(bool)
    show_midinote_changed = Signal(bool)
    show_waveform_changed = Signal(bool)
    theme_mode_changed = Signal(str)
    accent_color_mode_changed = Signal(str)
    custom_accent_color_changed = Signal(str)

    # ===================== 枚举/颜色合法值 =====================
    _ENCODINGS = ("UTF-8", "GBK", "Shift-JIS")
    _LYRIC_POSITIONS = ("上", "下")
    _SILENT_DISPLAYS = ("R", "-", "自定义文字", "什么都不显示")
    _END_DISPLAYS = ("END", "-", "自定义文字", "什么都不显示")
    _PITCH_PLACEHOLDERS = ("无", "-", "自定义文字")

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)

        # 程序根目录
        self.program_root = resolve_program_root()
        self.settings_path = os.path.join(self.program_root, "Settings.ini")

        # 文本文件路径
        self.terms_file_path = os.path.join(self.program_root, "LICENSE")
        self.ercode_file_path = os.path.join(self.program_root, "ERcode.txt")

        # 默认路径
        default_desktop = os.path.join(os.path.expanduser("~"), "Desktop")
        self.last_open_dir = default_desktop
        self.last_export_dir = default_desktop

        # ===== 字符串属性 =====
        self._ust_path = ""
        self._project_name = ""
        self._song_name = ""
        self._song_author = ""
        self._ust_author = ""
        self._encoding = "Shift-JIS"
        self._bg_color = "#000000"
        self._note_color = "#6c6c6c"
        self._lyric_color = "#FFFFFF"
        self._lyric_text_color = "#FFFFFF"
        self._other_text_color = "#FFFFFF"
        self._pitch_curve_color = "#FFFFFF"
        self._lyric_pos = "上"
        self._lrc_path = ""
        self._music_path = ""
        self._silent_display = "R"
        self._silent_custom_text = ""
        self._end_display = "END"
        self._end_custom_text = ""
        self._pitch_placeholder = "无"
        self._pitch_custom_text = ""

        # ===== 布尔属性 =====
        self._show_bpm = True
        self._show_play_time = True
        self._show_song_name = True
        self._show_song_author = True
        self._show_ust_author = True
        self._fullscreen = True
        self._show_lyric = False
        self._curve_show = False
        self._show_phoneme = False
        self._show_midinote = False
        self._show_waveform = False

        # 主题模式（用户级 UI 偏好，不参与 uplr 导入导出）
        self._theme_mode = "auto"  # auto=跟随系统, light=亮色, dark=暗色

        # 强调色设置（用户级 UI 偏好）
        self._accent_color_mode = "auto"  # auto=跟随系统强调色, custom=自定义
        self._custom_accent_color = "#009faa"  # qfluentwidgets 默认主题色

        # 初始化配置
        self._config = configparser.ConfigParser()
        self.read_settings()

    # ===================== 字符串属性（getter/setter + signal） =====================

    @property
    def ust_path(self) -> str:
        return self._ust_path

    @ust_path.setter
    def ust_path(self, v: str):
        if self._ust_path != v:
            self._ust_path = v
            self.ust_path_changed.emit(v)

    @property
    def project_name(self) -> str:
        return self._project_name

    @project_name.setter
    def project_name(self, v: str):
        if self._project_name != v:
            self._project_name = v
            self.project_name_changed.emit(v)

    @property
    def song_name(self) -> str:
        return self._song_name

    @song_name.setter
    def song_name(self, v: str):
        if self._song_name != v:
            self._song_name = v
            self.song_name_changed.emit(v)

    @property
    def song_author(self) -> str:
        return self._song_author

    @song_author.setter
    def song_author(self, v: str):
        if self._song_author != v:
            self._song_author = v
            self.song_author_changed.emit(v)

    @property
    def ust_author(self) -> str:
        return self._ust_author

    @ust_author.setter
    def ust_author(self, v: str):
        if self._ust_author != v:
            self._ust_author = v
            self.ust_author_changed.emit(v)

    @property
    def encoding(self) -> str:
        return self._encoding

    @encoding.setter
    def encoding(self, v: str):
        if self._encoding != v:
            self._encoding = v
            self.encoding_changed.emit(v)

    @property
    def bg_color(self) -> str:
        return self._bg_color

    @bg_color.setter
    def bg_color(self, v: str):
        if self._bg_color != v:
            self._bg_color = v
            self.bg_color_changed.emit(v)

    @property
    def note_color(self) -> str:
        return self._note_color

    @note_color.setter
    def note_color(self, v: str):
        if self._note_color != v:
            self._note_color = v
            self.note_color_changed.emit(v)

    @property
    def lyric_color(self) -> str:
        return self._lyric_color

    @lyric_color.setter
    def lyric_color(self, v: str):
        if self._lyric_color != v:
            self._lyric_color = v
            self.lyric_color_changed.emit(v)

    @property
    def lyric_text_color(self) -> str:
        return self._lyric_text_color

    @lyric_text_color.setter
    def lyric_text_color(self, v: str):
        if self._lyric_text_color != v:
            self._lyric_text_color = v
            self.lyric_text_color_changed.emit(v)

    @property
    def other_text_color(self) -> str:
        return self._other_text_color

    @other_text_color.setter
    def other_text_color(self, v: str):
        if self._other_text_color != v:
            self._other_text_color = v
            self.other_text_color_changed.emit(v)

    @property
    def pitch_curve_color(self) -> str:
        return self._pitch_curve_color

    @pitch_curve_color.setter
    def pitch_curve_color(self, v: str):
        if self._pitch_curve_color != v:
            self._pitch_curve_color = v
            self.pitch_curve_color_changed.emit(v)

    @property
    def lyric_pos(self) -> str:
        return self._lyric_pos

    @lyric_pos.setter
    def lyric_pos(self, v: str):
        if self._lyric_pos != v:
            self._lyric_pos = v
            self.lyric_pos_changed.emit(v)

    @property
    def lrc_path(self) -> str:
        return self._lrc_path

    @lrc_path.setter
    def lrc_path(self, v: str):
        if self._lrc_path != v:
            self._lrc_path = v
            self.lrc_path_changed.emit(v)

    @property
    def music_path(self) -> str:
        return self._music_path

    @music_path.setter
    def music_path(self, v: str):
        if self._music_path != v:
            self._music_path = v
            self.music_path_changed.emit(v)

    @property
    def silent_display(self) -> str:
        return self._silent_display

    @silent_display.setter
    def silent_display(self, v: str):
        if self._silent_display != v:
            self._silent_display = v
            self.silent_display_changed.emit(v)

    @property
    def silent_custom_text(self) -> str:
        return self._silent_custom_text

    @silent_custom_text.setter
    def silent_custom_text(self, v: str):
        if self._silent_custom_text != v:
            self._silent_custom_text = v
            self.silent_custom_text_changed.emit(v)

    @property
    def end_display(self) -> str:
        return self._end_display

    @end_display.setter
    def end_display(self, v: str):
        if self._end_display != v:
            self._end_display = v
            self.end_display_changed.emit(v)

    @property
    def end_custom_text(self) -> str:
        return self._end_custom_text

    @end_custom_text.setter
    def end_custom_text(self, v: str):
        if self._end_custom_text != v:
            self._end_custom_text = v
            self.end_custom_text_changed.emit(v)

    @property
    def pitch_placeholder(self) -> str:
        return self._pitch_placeholder

    @pitch_placeholder.setter
    def pitch_placeholder(self, v: str):
        if self._pitch_placeholder != v:
            self._pitch_placeholder = v
            self.pitch_placeholder_changed.emit(v)

    @property
    def pitch_custom_text(self) -> str:
        return self._pitch_custom_text

    @pitch_custom_text.setter
    def pitch_custom_text(self, v: str):
        if self._pitch_custom_text != v:
            self._pitch_custom_text = v
            self.pitch_custom_text_changed.emit(v)

    # ===================== 布尔属性（getter/setter + signal） =====================

    @property
    def show_bpm(self) -> bool:
        return self._show_bpm

    @show_bpm.setter
    def show_bpm(self, v: bool):
        if self._show_bpm != v:
            self._show_bpm = v
            self.show_bpm_changed.emit(v)

    @property
    def show_play_time(self) -> bool:
        return self._show_play_time

    @show_play_time.setter
    def show_play_time(self, v: bool):
        if self._show_play_time != v:
            self._show_play_time = v
            self.show_play_time_changed.emit(v)

    @property
    def show_song_name(self) -> bool:
        return self._show_song_name

    @show_song_name.setter
    def show_song_name(self, v: bool):
        if self._show_song_name != v:
            self._show_song_name = v
            self.show_song_name_changed.emit(v)

    @property
    def show_song_author(self) -> bool:
        return self._show_song_author

    @show_song_author.setter
    def show_song_author(self, v: bool):
        if self._show_song_author != v:
            self._show_song_author = v
            self.show_song_author_changed.emit(v)

    @property
    def show_ust_author(self) -> bool:
        return self._show_ust_author

    @show_ust_author.setter
    def show_ust_author(self, v: bool):
        if self._show_ust_author != v:
            self._show_ust_author = v
            self.show_ust_author_changed.emit(v)

    @property
    def fullscreen(self) -> bool:
        return self._fullscreen

    @fullscreen.setter
    def fullscreen(self, v: bool):
        if self._fullscreen != v:
            self._fullscreen = v
            self.fullscreen_changed.emit(v)

    @property
    def show_lyric(self) -> bool:
        return self._show_lyric

    @show_lyric.setter
    def show_lyric(self, v: bool):
        if self._show_lyric != v:
            self._show_lyric = v
            self.show_lyric_changed.emit(v)

    @property
    def curve_show(self) -> bool:
        return self._curve_show

    @curve_show.setter
    def curve_show(self, v: bool):
        if self._curve_show != v:
            self._curve_show = v
            self.curve_show_changed.emit(v)

    @property
    def show_phoneme(self) -> bool:
        return self._show_phoneme

    @show_phoneme.setter
    def show_phoneme(self, v: bool):
        if self._show_phoneme != v:
            self._show_phoneme = v
            self.show_phoneme_changed.emit(v)

    @property
    def show_midinote(self) -> bool:
        return self._show_midinote

    @show_midinote.setter
    def show_midinote(self, v: bool):
        if self._show_midinote != v:
            self._show_midinote = v
            self.show_midinote_changed.emit(v)

    @property
    def show_waveform(self) -> bool:
        return self._show_waveform

    @show_waveform.setter
    def show_waveform(self, v: bool):
        if self._show_waveform != v:
            self._show_waveform = v
            self.show_waveform_changed.emit(v)

    # ===================== 主题模式属性 =====================

    @property
    def theme_mode(self) -> str:
        return self._theme_mode

    @theme_mode.setter
    def theme_mode(self, v: str):
        if v not in ("auto", "light", "dark"):
            v = "auto"
        if self._theme_mode != v:
            self._theme_mode = v
            self.theme_mode_changed.emit(v)

    # ===================== 强调色属性 =====================

    @property
    def accent_color_mode(self) -> str:
        return self._accent_color_mode

    @accent_color_mode.setter
    def accent_color_mode(self, v: str):
        if v not in ("auto", "custom"):
            v = "auto"
        if self._accent_color_mode != v:
            self._accent_color_mode = v
            self.accent_color_mode_changed.emit(v)

    @property
    def custom_accent_color(self) -> str:
        return self._custom_accent_color

    @custom_accent_color.setter
    def custom_accent_color(self, v: str):
        if self._custom_accent_color != v:
            self._custom_accent_color = v
            self.custom_accent_color_changed.emit(v)

    # ===================== Settings.ini 读写 =====================

    def read_settings(self):
        """读取配置文件，恢复上次的导入/导出路径与主题偏好。"""
        default_desktop = os.path.join(os.path.expanduser("~"), "Desktop")
        try:
            if os.path.exists(self.settings_path):
                self._config.read(self.settings_path, encoding="utf-8")
                if "PathSettings" in self._config:
                    self.last_open_dir = self._config["PathSettings"].get(
                        "last_open_dir", default_desktop
                    )
                    self.last_export_dir = self._config["PathSettings"].get(
                        "last_export_dir", default_desktop
                    )
                    if not os.path.isdir(self.last_open_dir):
                        self.last_open_dir = default_desktop
                    if not os.path.isdir(self.last_export_dir):
                        self.last_export_dir = default_desktop
                # 读取主题设置
                if "ThemeSettings" in self._config:
                    mode = self._config["ThemeSettings"].get("theme_mode", "auto")
                    self._theme_mode = mode if mode in ("auto", "light", "dark") else "auto"
                    amode = self._config["ThemeSettings"].get("accent_color_mode", "auto")
                    self._accent_color_mode = amode if amode in ("auto", "custom") else "auto"
                    raw = self._config["ThemeSettings"].get("custom_accent_color", "#009faa")
                    self._custom_accent_color = (
                        raw if is_valid_hex_color(raw) else "#009faa"
                    )
            else:
                self.last_open_dir = default_desktop
                self.last_export_dir = default_desktop
        except Exception as e:
            self.last_open_dir = default_desktop
            self.last_export_dir = default_desktop
            logger.exception(f"读取配置文件失败：{e}")

    def write_settings(self):
        """将当前路径和主题偏好写入配置文件。"""
        try:
            if "PathSettings" not in self._config:
                self._config["PathSettings"] = {}
            self._config["PathSettings"]["last_open_dir"] = self.last_open_dir
            self._config["PathSettings"]["last_export_dir"] = self.last_export_dir

            if "ThemeSettings" not in self._config:
                self._config["ThemeSettings"] = {}
            self._config["ThemeSettings"]["theme_mode"] = self._theme_mode
            self._config["ThemeSettings"]["accent_color_mode"] = self._accent_color_mode
            self._config["ThemeSettings"]["custom_accent_color"] = self._custom_accent_color

            with open(self.settings_path, "w", encoding="utf-8") as f:
                self._config.write(f)
        except Exception as e:
            logger.exception(f"写入配置文件失败：{e}")

    # ===================== .uplr 工程文件导入/导出 =====================

    def export_uplr(self, output_file: str):
        """导出所有配置与资源到新版 .uplr（ZIP 容器）工程文件。

        资源文件（ust/lrc/music）存在时一并打包，Info.json 内路径记录包内文件名；
        缺失的资源对应 null。使用 ZIP_STORED（不压缩），flac 等已压缩格式体积不变。
        """
        members = {}  # 属性名 → 包内文件名
        for attr in ("ust_path", "lrc_path", "music_path"):
            local = getattr(self, attr).strip()
            if local and os.path.exists(local):
                members[attr] = os.path.basename(local)

        info = self._settings_to_info_json(members)

        with zipfile.ZipFile(output_file, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr("Info.json", json.dumps(info, ensure_ascii=False, indent=4))
            for attr, name in members.items():
                zf.write(getattr(self, attr).strip(), arcname=name)

    @staticmethod
    def _as_bool(value, default: bool = False) -> bool:
        """宽松布尔转换：整数 0/1、bool、字符串 true/yes/on/1。"""
        if value is None:
            return default
        if isinstance(value, bool):
            return value
        if isinstance(value, (int, float)):
            return value != 0
        return str(value).strip().lower() in ("1", "true", "yes", "on")

    def _settings_to_info_json(self, members: dict) -> dict:
        """当前设置 → Info.json 结构（路径字段写包内文件名，缺失为 None）。"""
        def name_or_none(attr: str):
            return members.get(attr) or None

        return {
            "encoding": self.encoding,
            "basic": {
                "project_name": self.project_name or None,
                "ust_path": name_or_none("ust_path"),
                "music_path": name_or_none("music_path"),
                "song_name": self.song_name or None,
                "song_author": self.song_author or None,
                "ust_author": self.ust_author or None,
            },
            "display": {
                "show_bpm": 1 if self.show_bpm else 0,
                "show_play_time": 1 if self.show_play_time else 0,
                "show_song_name": 1 if self.show_song_name else 0,
                "show_song_author": 1 if self.show_song_author else 0,
                "show_ust_author": 1 if self.show_ust_author else 0,
                "show_phoneme": 1 if self.show_phoneme else 0,
                "show_midinote": 1 if self.show_midinote else 0,
                "show_waveform": 1 if self.show_waveform else 0,
                "fullscreen": 1 if self.fullscreen else 0,
                "show_lyric": 1 if self.show_lyric else 0,
                "curve_show": 1 if self.curve_show else 0,
            },
            "color": {
                "bg_color": self.bg_color,
                "note_color": self.note_color,
                "lyric_color": self.lyric_color,
                "lyric_text_color": self.lyric_text_color,
                "other_text_color": self.other_text_color,
                "pitch_curve_color": self.pitch_curve_color,
            },
            "else": {
                "lyric_pos": self.lyric_pos,
                "lrc_path": name_or_none("lrc_path"),
                "silent_display": self.silent_display,
                "silent_custom_text": self.silent_custom_text or None,
                "end_display": self.end_display,
                "end_custom_text": self.end_custom_text or None,
                "pitch_placeholder": self.pitch_placeholder,
                "pitch_custom_text": self.pitch_custom_text or None,
            },
        }

    def _apply_info_json(self, info: dict, base_dir: str):
        """Info.json → 设置。路径字段解析为缓存目录中的完整路径。"""
        def resolve(name):
            return os.path.join(base_dir, name) if name else ""

        basic = info.get("basic", {}) or {}
        display = info.get("display", {}) or {}
        color = info.get("color", {}) or {}
        else_ = info.get("else", {}) or {}

        self.encoding = info.get("encoding") or "Shift-JIS"
        self.project_name = basic.get("project_name") or ""
        self.ust_path = resolve(basic.get("ust_path") or "")
        self.music_path = resolve(basic.get("music_path") or "")
        self.song_name = basic.get("song_name") or ""
        self.song_author = basic.get("song_author") or ""
        self.ust_author = basic.get("ust_author") or ""

        self.show_bpm = self._as_bool(display.get("show_bpm"), True)
        self.show_play_time = self._as_bool(display.get("show_play_time"), True)
        self.show_song_name = self._as_bool(display.get("show_song_name"), True)
        self.show_song_author = self._as_bool(display.get("show_song_author"), True)
        self.show_ust_author = self._as_bool(display.get("show_ust_author"), True)
        self.show_phoneme = self._as_bool(display.get("show_phoneme"), False)
        self.show_midinote = self._as_bool(display.get("show_midinote"), False)
        self.show_waveform = self._as_bool(display.get("show_waveform"), False)
        self.fullscreen = self._as_bool(display.get("fullscreen"), True)
        self.show_lyric = self._as_bool(display.get("show_lyric"), False)
        # 样例将 curve_show 放在 else 分组，导出并入 display；导入时 display 优先、else 兜底
        self.curve_show = self._as_bool(
            display.get("curve_show", else_.get("curve_show")), False
        )

        self.bg_color = color.get("bg_color") or "#000000"
        self.note_color = color.get("note_color") or "#6c6c6c"
        self.lyric_color = color.get("lyric_color") or "#FFFFFF"
        self.lyric_text_color = color.get("lyric_text_color") or "#FFFFFF"
        self.other_text_color = color.get("other_text_color") or "#FFFFFF"
        self.pitch_curve_color = color.get("pitch_curve_color") or "#FFFFFF"

        self.lyric_pos = else_.get("lyric_pos") or "上"
        self.lrc_path = resolve(else_.get("lrc_path") or "")
        self.silent_display = else_.get("silent_display") or "R"
        self.silent_custom_text = else_.get("silent_custom_text") or ""
        self.end_display = else_.get("end_display") or "END"
        self.end_custom_text = else_.get("end_custom_text") or ""
        self.pitch_placeholder = else_.get("pitch_placeholder") or "无"
        self.pitch_custom_text = else_.get("pitch_custom_text") or ""

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
        # 字段映射：key → (setter, type)
        str_keys = {
            "project_name": "project_name",
            "ust_path": "ust_path",
            "music_path": "music_path",
            "song_name": "song_name",
            "song_author": "song_author",
            "ust_author": "ust_author",
            "encoding": "encoding",
            "bg_color": "bg_color",
            "note_color": "note_color",
            "lyric_color": "lyric_color",
            "lyric_text_color": "lyric_text_color",
            "other_text_color": "other_text_color",
            "pitch_curve_color": "pitch_curve_color",
            "lyric_pos": "lyric_pos",
            "lrc_path": "lrc_path",
            "silent_display": "silent_display",
            "silent_custom_text": "silent_custom_text",
            "end_display": "end_display",
            "end_custom_text": "end_custom_text",
            "pitch_placeholder": "pitch_placeholder",
            "pitch_custom_text": "pitch_custom_text",
        }
        bool_keys = {
            "show_bpm": "show_bpm",
            "show_play_time": "show_play_time",
            "show_song_name": "show_song_name",
            "show_song_author": "show_song_author",
            "show_ust_author": "show_ust_author",
            "fullscreen": "fullscreen",
            "show_lyric": "show_lyric",
            "curve_show": "curve_show",
            "show_phoneme": "show_phoneme",
            "show_midinote": "show_midinote",
            "show_waveform": "show_waveform",
        }
        truthy = ("1", "true", "yes", "on")

        with open(input_file, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                parts = line.split("=", 1)
                if len(parts) != 2:
                    continue
                key = parts[0].strip()
                value = parts[1].strip()

                if key in str_keys:
                    setattr(self, str_keys[key], value)
                elif key in bool_keys:
                    setattr(self, bool_keys[key], value.lower() in truthy)

        self._sanitize_imported()

    # ===================== 新版 ZIP 格式 =====================

    def _import_uplr_zip(self, input_file: str):
        """解析新版 ZIP .uplr：读取 Info.json 并把资源解压到缓存目录。"""
        cache_dir = self._uplr_cache_dir(input_file)
        with zipfile.ZipFile(input_file, "r") as zf:
            if "Info.json" not in zf.namelist():
                raise ValueError("ZIP 工程文件缺少 Info.json")
            info = json.loads(zf.read("Info.json").decode("utf-8"))
            for name in zf.namelist():
                if name == "Info.json":
                    continue
                self._extract_member_safe(zf, name, cache_dir)
        self._apply_info_json(info, cache_dir)
        self._sanitize_imported()

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

    def _sanitize_imported(self):
        """校验导入的枚举/颜色值，非法时回退默认（通过 setter 保证信号同步）。"""
        if self.encoding not in self._ENCODINGS:
            self.encoding = "Shift-JIS"
        if self.lyric_pos not in self._LYRIC_POSITIONS:
            self.lyric_pos = "上"
        if self.silent_display not in self._SILENT_DISPLAYS:
            self.silent_display = "R"
        if self.end_display not in self._END_DISPLAYS:
            self.end_display = "END"
        if self.pitch_placeholder not in self._PITCH_PLACEHOLDERS:
            self.pitch_placeholder = "无"
        for attr in ("bg_color", "note_color", "lyric_color",
                     "lyric_text_color", "other_text_color", "pitch_curve_color"):
            value = getattr(self, attr)
            if not is_valid_hex_color(value):
                setattr(self, attr, {
                    "bg_color": "#000000",
                    "note_color": "#6c6c6c",
                    "lyric_color": "#FFFFFF",
                    "lyric_text_color": "#FFFFFF",
                    "other_text_color": "#FFFFFF",
                    "pitch_curve_color": "#FFFFFF",
                }[attr])

    # ===================== 构建播放器启动参数 =====================

    def build_ust_info(self, core_ust_info: UstInfo) -> PlayerLaunchParams:
        """将解析结果与当前设置组装为播放器启动参数（统一接口）。"""
        return PlayerLaunchParams(
            ust=core_ust_info,
            show=ShowConfig(
                bpm=self.show_bpm,
                play_time=self.show_play_time,
                song_name=self.show_song_name,
                song_author=self.show_song_author,
                ust_author=self.show_ust_author,
                lyric=self.show_lyric,
                curve_show=self.curve_show,
                phoneme=self.show_phoneme,
                midinote=self.show_midinote,
                waveform=self.show_waveform,
            ),
            project=ProjectInfo(
                project_name=self.project_name,
                song_name=self.song_name,
                song_author=self.song_author,
                ust_author=self.ust_author,
            ),
            style=PlayerStyle(
                bg_color=self.bg_color,
                note_color=self.note_color,
                lyric_color=self.lyric_color,
                lyric_text_color=self.lyric_text_color,
                other_text_color=self.other_text_color,
                lyric_pos=self.lyric_pos,
                fullscreen=self.fullscreen,
                lrc_path=self.lrc_path,
                music_path=self.music_path,
                silent_display=self.silent_display,
                silent_custom_text=self.silent_custom_text,
                end_display=self.end_display,
                end_custom_text=self.end_custom_text,
                pitch_placeholder=self.pitch_placeholder,
                pitch_custom_text=self.pitch_custom_text,
                pitch_curve_color=self.pitch_curve_color,
            ),
        )
