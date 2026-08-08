# settings_manager.py — 配置管理器
"""Settings.ini 配置读写 + .uplr 工程文件导入/导出。

通过 Qt Signal 通知 UI 所有配置变更，替代 tkinter 的 StringVar/BooleanVar 机制。
"""

import os
import sys
import configparser
from typing import Optional

from PySide6.QtCore import QObject, Signal


class SettingsManager(QObject):
    """应用配置管理器，集中管理所有设置项。

    每个配置项对应一个属性，修改时发出对应的 Signal。
    UI 层通过 connect/setValue 模式绑定。
    """

    # ===================== 信号定义 =====================
    # 字符串信号
    ustx_path_changed = Signal(str)
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
    show_phoneme_changed = Signal(bool)
    show_midinote_changed = Signal(bool)
    show_waveform_changed = Signal(bool)
    fullscreen_changed = Signal(bool)
    show_lyric_changed = Signal(bool)
    curve_show_changed = Signal(bool)
    theme_mode_changed = Signal(str)
    accent_color_mode_changed = Signal(str)
    custom_accent_color_changed = Signal(str)

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)

        # 程序根目录
        self.program_root = os.path.dirname(os.path.abspath(sys.argv[0]))
        self.settings_path = os.path.join(self.program_root, "Settings.ini")

        # 文本文件路径
        self.terms_file_path = os.path.join(self.program_root, "LICENSE")
        self.ercode_file_path = os.path.join(self.program_root, "ERcode.txt")

        # 默认路径
        default_desktop = os.path.join(os.path.expanduser("~"), "Desktop")
        self.last_open_dir = default_desktop
        self.last_export_dir = default_desktop

        # 额外字段
        self.OPU_site = ""
        self.default_singer = ""
        self.default_phenomizer = ""

        # ===== 字符串属性 =====
        self._ustx_path = ""
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
        self._show_phoneme = False
        self._show_midinote = False
        self._show_waveform = False
        self._fullscreen = True
        self._show_lyric = False
        self._curve_show = False

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
    def ustx_path(self) -> str:
        return self._ustx_path

    @ustx_path.setter
    def ustx_path(self, v: str):
        if self._ustx_path != v:
            self._ustx_path = v
            self.ustx_path_changed.emit(v)

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
        """读取配置文件，恢复上次的导入/导出路径。"""
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
                    self._custom_accent_color = self._config["ThemeSettings"].get(
                        "custom_accent_color", "#009faa"
                    )
            else:
                self.last_open_dir = default_desktop
                self.last_export_dir = default_desktop
        except Exception as e:
            self.last_open_dir = default_desktop
            self.last_export_dir = default_desktop
            print(f"读取配置文件失败：{e}")

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
            print(f"写入配置文件失败：{e}")

    # ===================== .uplr 工程文件导入/导出 =====================

    def export_uplr(self, output_file: str):
        """导出所有配置到 .uplr 工程文件。"""
        with open(output_file, "w", encoding="utf-8") as f:
            # 编码配置
            f.write("#Encoding\n")
            f.write(f"encoding={self.encoding}\n\n")

            # 基础设置
            f.write("#BasicSettings\n")
            f.write(f"project_name={self.project_name}\n")
            f.write(f"ust_path={self.ustx_path}\n")
            f.write(f"song_name={self.song_name}\n")
            f.write(f"song_author={self.song_author}\n")
            f.write(f"ust_author={self.ust_author}\n\n")

            # 显示设置
            f.write("#DisplaySettings\n")
            f.write(f"show_bpm={1 if self.show_bpm else 0}\n")
            f.write(f"show_play_time={1 if self.show_play_time else 0}\n")
            f.write(f"show_song_name={1 if self.show_song_name else 0}\n")
            f.write(f"show_song_author={1 if self.show_song_author else 0}\n")
            f.write(f"show_ust_author={1 if self.show_ust_author else 0}\n")
            f.write(f"show_phoneme={1 if self.show_phoneme else 0}\n")
            f.write(f"show_midinote={1 if self.show_midinote else 0}\n")
            f.write(f"show_waveform={1 if self.show_waveform else 0}\n")
            f.write(f"fullscreen={1 if self.fullscreen else 0}\n")
            f.write(f"show_lyric={1 if self.show_lyric else 0}\n\n")

            # 颜色设置
            f.write("#ColorSettings\n")
            f.write(f"bg_color={self.bg_color}\n")
            f.write(f"note_color={self.note_color}\n")
            f.write(f"lyric_color={self.lyric_color}\n")
            f.write(f"lyric_text_color={self.lyric_text_color}\n")
            f.write(f"other_text_color={self.other_text_color}\n")
            f.write(f"pitch_curve_color={self.pitch_curve_color}\n\n")

            # 歌词与额外配置
            f.write("#LyricAndExtra\n")
            f.write(f"lyric_pos={self.lyric_pos}\n")
            f.write(f"lrc_path={self.lrc_path}\n")
            f.write(f"silent_display={self.silent_display}\n")
            f.write(f"silent_custom_text={self.silent_custom_text}\n")
            f.write(f"end_display={self.end_display}\n")
            f.write(f"end_custom_text={self.end_custom_text}\n")
            f.write(f"curve_show={1 if self.curve_show else 0}\n")
            f.write(f"pitch_placeholder={self.pitch_placeholder}\n")
            f.write(f"pitch_custom_text={self.pitch_custom_text}\n")
            f.write(f"")


    def import_uplr(self, input_file: str):
        """从 .uplr 工程文件导入全部配置。"""
        # 字段映射：key → (setter, type)
        str_keys = {
            "project_name": "project_name",
            "ust_path": "ustx_path",
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
            "show_phoneme": "show_phoneme",
            "show_midinote": "show_midinote",
            "show_waveform": "show_waveform",
            "fullscreen": "fullscreen",
            "show_lyric": "show_lyric",
            "curve_show": "curve_show",
        }

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
                    setattr(self, bool_keys[key], value == "1")

    # ===================== 构建播放器需要的 ust_info 字典 =====================

    def build_ust_info(self, core_ust_info: dict) -> dict:
        """组装传递给播放器的完整参数 dict（兼容原 ustplayer.py 的接口）。"""
        silent_disp = self.silent_display if self.silent_display != "什么都不显示" else ""
        end_disp = self.end_display if self.end_display != "什么都不显示" else ""

        return {
            "version": core_ust_info.get("version", "未知版本"),
            "tempo": core_ust_info.get("tempo", 120.0),
            "tracks": core_ust_info.get("tracks", 1),
            "notes": core_ust_info.get("notes", []),
            "show_config": {
                "bpm": self.show_bpm,
                "play_time": self.show_play_time,
                "song_name": self.show_song_name,
                "song_author": self.show_song_author,
                "ust_author": self.show_ust_author,
                "lyric": self.show_lyric,
                "curve_show": self.curve_show,
            },
            "project_info": {
                "project_name": self.project_name,
                "song_name": self.song_name,
                "song_author": self.song_author,
                "ust_author": self.ust_author,
            },
            "encoding": self.encoding,
            "player_style": {
                "bg_color": self.bg_color,
                "note_color": self.note_color,
                "lyric_color": self.lyric_color,
                "lyric_text_color": self.lyric_text_color,
                "other_text_color": self.other_text_color,
                "lyric_pos": self.lyric_pos,
                "show_phoneme": self.show_phoneme,
                "show_midinote": self.show_midinote,
                "show_waveform": self.show_waveform,
                "fullscreen": self.fullscreen,
                "lrc_path": self.lrc_path,
                "lrc_gray_level": 180,
                "lrc_font_scale": 0.03,
                "silent_display": silent_disp,
                "silent_custom_text": self.silent_custom_text,
                "end_display": end_disp,
                "end_custom_text": self.end_custom_text,
                "pitch_placeholder": self.pitch_placeholder,
                "pitch_custom_text": self.pitch_custom_text,
                "pitch_curve_color": self.pitch_curve_color,
            },
        }
