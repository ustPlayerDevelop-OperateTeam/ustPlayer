# AGENTS.md

中文优先、仅支持 Windows 的 PySide6 + PySide6-Fluent-Widgets 桌面应用，用于可视化 UTAU/UST 工程文件。注释、文档字符串、日志消息与 UI 字符串一律使用中文（完整贡献规则见 CONTRIBUTING.md）。开源协议 GPL-3.0（见 LICENSE）。

## 命令

- 环境搭建：`uv sync`（uv；`.python-version` 固定 Python 3.13.12，要求 >=3.11）。`pyproject.toml` 是依赖的唯一事实源——不要另建 `requirements.txt`。
- 运行：`uv run main.py` —— 唯一真实入口（薄壳 → `ustplayer.app.main`）。`uv run ustplayer` 等价（`[project.scripts] ustplayer` → `ustplayer.app:main`）；两条入口路径共用 `AppContext`。
- 测试：`uv run pytest`（171 个用例，覆盖 `contracts` / `ustreader` / `settings` 七个子域 / `settings_store` / `settings_manager` / `i18n` / `player` / `uplr_io`）。跑单个用例：`uv run pytest tests/test_uplr_io.py::test_export_import_round_trip`。`QT_QPA_PLATFORM=offscreen` 在 `tests/conftest.py` 顶层设置，无显示器 / CI 也能跑 Qt 测试；新增测试放 `tests/`，约定见 `tests/conftest.py`。没有已提交的 linter 配置。类型检查目标是 **Pylance / pyright Standard 模式 0 error**（CONTRIBUTING.md 有说明，例如 `npx --yes pyright main.py src`；未提交 `pyrightconfig.json`——需要时在本地自行创建）。
- 仅 Windows：`ustplayer/ui/main_window.py` 使用了 `winreg`（读取系统强调色）。在 WSL/Linux 上无法运行。

## 注意事项（Gotchas）

- **不支持** USTX（`.ustx`）——解析器只处理 `.ust` 文本。不要声称支持 USTX，也不要将 `.ustx` 交给 `UstFileReader`。
- 构建/发版只通过 GitHub Actions（`.github/workflows/build.yml`，windows-latest 上的 Nuitka standalone，另有 `uplr-converter` 任务与发版打包）。提交信息以 `pass` 开头会跳过 CI；以 `v` 开头（任意分支的 push，或推送任何 `v*` 标签）会触发自动发版。除非确实要发版，否则绝不要使用 `v` 前缀。
- CI 从 `ChangeLog.md` 中标题为 `# v{版本}` 的小节提取 Release 说明——新增条目时必须保持该标题格式。顶层的 `## Unreleased` 小节会被提取器忽略。
- 提交信息包含 `close #N` / `fixes #N` 等关键字时，会在 `main` / `dev` 分支自动关闭对应 Issue（见 `.github/workflows/auto-close-issue.yml`）。
- 版本号：现在采用语义化版本（见 `pyproject.toml`，当前为 1.1.0b1，与 `contracts.APP_VERSION` 的 "1.1.0 Beta 1" 对应）；旧的日期式版本号（`v26f19`）已成历史——不要重新引入。
- 依赖说明：`pyside6-fluent-widgets` 刻意**不带** `[full]` extra——那会引入 scipy/numpy/pillow/colorthief（约 120MB 死依赖）。不要"修复"这一点。
- `.vscode/tasks.json` 提供了一个构建任务（`Ctrl+Shift+B`），运行 `tools/uplr_converter/build.bat`——这是唯一的本地构建；转换器其余时候由 CI 构建。

## 架构

- `main.py` —— 薄壳；真实入口 `src/ustplayer/app.py`：QApplication + `AppContext` + `MainWindow`。
- `src/ustplayer/context.py` —— `AppContext`：唯一的组合根 / 门面。UI 页面通过构造注入获得它，并调用 `ctx.settings`、`ctx.parser`、`ctx.player`、`ctx.project_io`；不得直接 import core 的具体实现。
- `src/ustplayer/core/contracts.py` —— 数据契约（`UstInfo`/`NoteInfo`/`PlayerLaunchParams`）、服务 Protocol（`UstParser`/`PlayerLauncher`/`ProjectIO`）、颜色与布尔工具、应用版本常量。
- `src/ustplayer/core/`：
  - `log.py` —— loguru，将 `ustPlayer.log` 写入 exe 旁（不可写时回退到 `%LOCALAPPDATA%\ustPlayer`）+ stdout（对打包 GUI 的 `sys.stdout is None` 做了防护）
  - `settings_manager.py` —— `SettingsManager`：薄门面，负责组装 `core/settings/` 下的设置子域，并编排设置的读写 / 校验 / 播放参数组装。UI 通过 `ctx.settings.<子域>.<属性>` 访问设置（如 `ctx.settings.display.show_bpm`）；每个属性都有对应的 `<属性>_changed` 信号。
  - `settings/` —— 每个设置分组对应一个信号驱动的子域（原 ini 段，键保留在 `Settings.json` 中）：`project.py`（`ProjectSettings`）、`file.py`（`FileSettings`）、`display.py`（`DisplaySettings`）、`color.py`（`ColorSettings`）、`player.py`（`PlayerSettings`+`LyricSettings`）、`language.py`（`LanguageSettings`，不导出到 uplr）、`theme.py`（`ThemeSettings`，不导出到 uplr）。每个类自行持有属性 + `Signal`s + `read_from`/`write_to`/`validate`。
  - `settings_store.py` —— `SettingsStore`：`Settings.json` 的文件 I/O（分组→键值字典；路径解析，不可写时回退 `%LOCALAPPDATA%\ustPlayer`），首次运行自动迁移旧版 `Settings.ini`，无业务逻辑。
  - `uplr_io.py` —— `UplrProjectIO` 实现 `contracts.ProjectIO`：`.uplr` 导入/导出。**新格式 = ZIP 容器**（`Info.json` + ust/lrc/音乐资源，导入时解压到 `%LOCALAPPDATA%\ustPlayer\projects\<工程名>-<hash8>\`）；**旧文本格式仍可导入**（按 ZIP 魔数自动识别）。依赖 `SettingsManager` 读写属性；导入会触发设置信号，UI 因此实时同步。
  - `ustreader.py` —— `UstFileReader` 实现 `UstParser`；只处理 `.ust` 文本（解析 Lyric/Length/NoteNum/Phoneme/PitchBend），接受 `encoding` 参数（默认 "Shift-JIS"）；编码错误时抛出 `UnicodeDecodeError`。
  - `player.py` —— `NotePlayerLauncher` 实现 `PlayerLauncher`；`NoteLyricDisplay` 是全屏 QPainter 播放器。通过 QtMultimedia 播放伴奏（`music_path`）并按媒体位置驱动时间轴；无音频或音频失败时回退到墙钟计时。按 `ShowConfig` 渲染歌词 / 音符名 / 音高曲线 / LRC 歌词。改动 QtMultimedia 相关代码时，请保留文件顶部的 `try/except` + `TYPE_CHECKING` 降级导入模式。
- `src/ustplayer/ui/` —— 每个侧边栏项对应一个页面：`basic_page.py`、`file_page.py`、`player_style_page.py`、`lyric_page.py`、`other_page.py`，另有 `main_window.py`（带侧边导航的 FluentWindow）与共享控件 `section_card.py`。每个页面都实现 `sync_all_from_settings()`。
- `tools/uplr_converter/` —— 独立的 C++17 转换器（旧文本 .uplr → 新版 ZIP .uplr），零第三方依赖；由 CI 在 windows-latest 上构建，随 Release 发布。
- `Settings.ini` / `Settings.json` 已 gitignore（用户本地）。
- `test/` 与 `ustPlayer uplr sample/` 已 gitignore（仅本地示例数据）——不要依赖它们，也不要往里提交。

## 约定（Conventions）

- 使用 `from ustplayer.core.log import logger`（loguru），绝不使用 `print`；需要堆栈时用 `logger.exception(...)`。
- 面向用户的错误遵循 `InfoBar.error("ERcodeXXX", "提示文案", ...)` 模式；新错误码请登记到 `ERcode.txt`（001–010 与 999 已占用）。
- **i18n**：所有用户可见的 UI 字符串必须用 `from ustplayer.core.i18n import tr` 的 `tr("中文原文")` 包裹（自由函数 `tr`，lupdate 只认这个名字）；日志不翻译。改 UI 字符串后必须跑 `pyside6-lupdate -extensions py src/ustplayer -ts i18n/ustplayer_zh_CN.ts i18n/ustplayer_en_US.ts i18n/ustplayer_zh_classic.ts` + `pyside6-lrelease i18n/*.ts` 并提交 `.qm`（详见 CONTRIBUTING.md「翻译贡献」）。
- **存储层只存稳定 key**：`Settings.json` 与 `.uplr` 中的枚举值（`lyric_pos`/`silent_display`/`end_display`/`pitch_placeholder`）一律是英文 key（`top`/`r`/`custom`/`none` 等），显示文案由 UI 层 `tr()` 翻译；旧中文值由 `core/settings/player.py` 的 `migrate_value()` 兼容迁移。不要把显示文案写回存储层。
- 语言偏好存 `Settings.json` 的 `[LanguageSettings]`（默认 `system` 跟随系统），**不写入 .uplr**；切换语言经 `LanguageSettings.language_changed` 信号 → 主窗口 `_on_language_changed` 全窗口重译。
- 新增/重命名设置项意味着要改四处：子域类、`SettingsManager`（若参与播放参数）、`uplr_io.py`（`_settings_to_info_json` / `_apply_info_json`），以及任何 UI 接线——否则设置会静默不生效、无法持久化，或无法随 `.uplr` 完整往返。
- 大文件用 `# ===================== 段落名 =====================` 分隔不同功能块。
- 用户可见的变更要在最新 `ChangeLog.md` 小节中补一条（未发版时写在 `## Unreleased` 下）。
