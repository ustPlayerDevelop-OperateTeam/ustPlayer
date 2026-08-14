# uplr_converter — 旧版 .uplr 转新版 .uplr 转换器（C++17）

将旧版纯文本 `.uplr`（`key=value`）转换为新版 ZIP 容器 `.uplr`
（内含 `Info.json` + 工程/歌词/伴奏资源文件）。

## 构建

- 本机：`build.bat`（自动选择 MSVC `cl` 或 MinGW `g++`），或
  `cmake -S . -B build && cmake --build build --config Release`
- CI：GitHub Actions `windows-latest`（自带 MSVC）自动构建并随 Release 发布

## 用法

### TUI 模式（默认，鼠标交互）

直接双击运行或命令行不带参数启动 `uplr_converter.exe`：

```
uplr_converter.exe
```

进入可鼠标点击的交互菜单：

```
 ═══════════════════════════════════════════════════
   ustPlayer .uplr 转换器（旧版文本 → 新版 ZIP）
 ═══════════════════════════════════════════════════
 输入文件: （未选择）
 输出文件: （自动生成）
 ----------------------------------------------
 [1] 选择输入 .uplr 文件
 [2] 设置输出路径
 [3] 开始转换
 [4] 退出
 ----------------------------------------------
 提示: 鼠标单击菜单项 / 数字键 / 方向键+回车
```

- 鼠标单击菜单项即可选择（也支持数字键 `1-4`、`↑`/`↓` + 回车）；
- 选择输入文件后自动生成默认输出路径（同目录 `<原名>_new.uplr`），也可手动修改；
- 路径输入支持中文（宽字符读取，UTF-8 处理）；输入框内直接回车表示取消/保留默认。

### 命令行模式（脚本/批处理）

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
