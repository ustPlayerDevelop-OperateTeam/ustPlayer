# uplr_converter — 旧版 .uplr 转新版 .uplr 转换器（C++17）

将旧版纯文本 `.uplr`（`key=value`）转换为新版 ZIP 容器 `.uplr`
（内含 `Info.json` + 工程/歌词/伴奏资源文件）。

## 构建

- 本机：`build.bat`（自动选择 MSVC `cl` 或 MinGW `g++`），或
  `cmake -S . -B build && cmake --build build --config Release`
- CI：GitHub Actions `windows-latest`（自带 MSVC）自动构建并随 Release 发布

## 用法

```
uplr_converter.exe <input.uplr> <output.uplr>
```

- `input.uplr`：旧版文本 .uplr（UTF-8）
- `output.uplr`：新版 ZIP .uplr

资源文件（`ust_path`/`lrc_path`/`music_path`）按旧文件中的路径解析：
相对路径以 input 所在目录为基准；文件存在才打包，缺失时 Info.json 对应字段为
`null`。旧格式没有 `music_path`，故转换结果该字段恒为 `null`。

## 与 Python 侧的一致性

字段映射、Info.json 结构与
`src/ustplayer/core/settings_manager.py`（`_import_uplr_text` / `_settings_to_info_json`）
保持一致：`display` 分组含 `curve_show`，`color` 分组含 `pitch_curve_color`。
