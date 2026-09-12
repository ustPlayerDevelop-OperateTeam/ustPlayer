# 格式契约参考：Settings.json 与 .uplr

> 与 1.1.x **双向兼容**是硬承诺。本文记录逐字核对过的格式细节，供实现与测试比对。
> 结论来自直接阅读 `pysourcecode/src/ustplayer/core/` 的源码，不是推测。

## 一、`Settings.json`

结构：`{ 分组名: { 键: 值 } }`，缩进 2 空格、`ensure_ascii=False`（中文不转义）、UTF-8 无 BOM。

### 1.1 分组名（`SettingsSections`）

| 分组 | 说明 |
|---|---|
| `ProjectSettings` | 项目信息 |
| `FileSettings` | 文件与编码 |
| `DisplaySettings` | 显示开关与字体 |
| `ColorSettings` | 颜色 |
| `PlayerSettings` | 播放器样式枚举与自定义文字 |
| `LyricSettings` | LRC 路径（**独立分组**，不要并入 PlayerSettings） |
| `LanguageSettings` | 语言偏好（**不写入 .uplr**） |
| `ThemeSettings` | 主题（**不写入 .uplr**） |
| `PathSettings` | `last_open_dir` / `last_export_dir` |

### 1.2 值形态 ⚠️ 易错

**布尔值在设置文件里是字符串 `"1"` / `"0"`，不是 JSON 布尔。**

```python
# settings/display.py 与 settings/file.py 的 write_to
"show_bpm":  "1" if self._show_bpm else "0",
"curve_show":"1" if self._curve_show else "0",
```

只有 `[FileSettings]` 与 `[DisplaySettings]` 两处有布尔；其余键全是字符串或数组。
读取端用宽松解析（`as_bool`）：接受 `1/0`、`true/false`、`yes/no`、`on/off`（字符串或数字皆可）。

> 对比：`.uplr` 的 `Info.json` 里布尔是**整数** `int(x)` → `0/1`。
> 两处**写法不同**，移植时不要互相套用。

### 1.3 键名一览

| 分组 | 键 |
|---|---|
| `ProjectSettings` | `project_name`, `song_name`, `song_author`, `ust_author`, `music_path` |
| `FileSettings` | `ust_path`, `encoding`, `curve_show` |
| `DisplaySettings` | `show_bpm`, `show_play_time`, `show_song_name`, `show_song_author`, `show_ust_author`, `fullscreen`, `show_lyric`, `show_note_name`, `show_ust_lyric`, `show_copyright`, `font_note`, `font_ust_lyric`, `font_lrc`, `font_other`, `custom_font_paths` |
| `ColorSettings` | `bg_color`, `note_color`, `lyric_color`, `lyric_text_color`, `other_text_color`, `pitch_curve_color` |
| `PlayerSettings` | `lyric_pos`, `silent_display`, `silent_custom_text`, `end_display`, `end_custom_text`, `pitch_placeholder`, `pitch_custom_text` |
| `LyricSettings` | `lrc_path` |
| `ThemeSettings` | `theme_mode`, `accent_color_mode`, `custom_accent_color`, `window_effect` |
| `LanguageSettings` | `language`（默认 `system`） |

`curve_show` 同时在 `[FileSettings]`（设置文件）与 `.uplr` 的 `display` 段出现，两处都要写。

### 1.4 默认值（逐字核对自各子域的 `__init__`）

- `show_lyric` **默认 `False`**（其余显示项默认 `True`），`curve_show` 默认 `False`，`fullscreen` 默认 `True`。
- 颜色：`bg_color=#000000`、`note_color=#6c6c6c`、其余 `#FFFFFF`。
- 枚举：`lyric_pos=top`、`silent_display=r`、`end_display=end`、`pitch_placeholder=none`。
- 主题：`theme_mode=auto`、`accent_color_mode=auto`、`custom_accent_color=#009faa`、`window_effect=mica`。
- 语言：`system`；编码：`Shift-JIS`。

### 1.5 旧中文枚举值 → 英文 key 迁移表（`player.py` 的 `_LEGACY_*`）

| 字段 | 旧值 → 新值 |
|---|---|
| `lyric_pos` | `上`→`top`、`下`→`bottom` |
| `silent_display` | `R`→`r`、`-`→`dash`、`自定义文字`→`custom`、`什么都不显示`→`none` |
| `end_display` | `END`→`end`、`-`→`dash`、`自定义文字`→`custom`、`什么都不显示`→`none` |
| `pitch_placeholder` | `无`→`none`、`-`→`dash`、`自定义文字`→`custom` |

规则：先看是否已是合法 key，再查迁移表，最后回退该字段默认值。

### 1.6 旧版 `Settings.ini` 迁移

首次运行把 ini 转成 JSON 并删除旧文件。**解析时不做插值**——值里可能含裸 `%`
（如工程名「100% Pure」），1.1.x 曾因 ConfigParser 默认插值在此崩溃。

## 二、`.uplr` 的 `Info.json`

结构与字段名见迁移计划 §6.1（`_settings_to_info_json`）。要点复述：

- 布尔是**整数** `0/1`（不是 `"1"`、也不是 `true`）。
- 空字符串转 `null`（`or None` 语义）；`custom_font_paths` 空列表转 `null`。
- `.uplr` 的 `display` 段**含 `curve_show`**；`.uprd` **不含**，但含恒为 `0` 的
  `show_phoneme` / `show_midinote` / `show_waveform`。
- `.uprd` 额外有 `video: { width, height, fps }`，且枚举走带默认值回退的 `enum()`。

## 三、渲染器 `RenderConfig`（`up_set_config`）

字段名见根 `API_Docs.md`；宿主把 `PlayerLaunchParams` 序列化后追加
`width` / `height` / `fps` / `output_path`。模型上的 `JsonPropertyName` 是唯一事实源，
由 `UstPlayer.Tests/Models/JsonContractTests` 逐字段钉死。

**渲染器的 serde 对未知字段静默忽略**：字段名写错不会报错，只会表现为画面缺内容。
