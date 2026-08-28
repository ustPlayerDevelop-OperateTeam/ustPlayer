# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **`AGENTS.md` 是更详细的权威指南**（完整架构、Gotchas、约定）。本文件只提炼每次会话必须立刻知道的核心；深度内容与边界情况请读 `AGENTS.md` 与 `CONTRIBUTING.md`。两者冲突时以 `AGENTS.md` 为准。

## 项目概览

中文优先、仅 Windows 的 PySide6 + PySide6-Fluent-Widgets 桌面应用，用于可视化 UTAU/UST 工程文件。注释 / 文档字符串 / 日志消息 / UI 字符串一律使用中文；用户可见的 UI 字符串必须用 `tr()` 包裹。GPL-3.0。

## 常用命令

- 环境搭建：`uv sync`（uv 管理依赖与虚拟环境；`.python-version` 固定 Python 3.13.12，最低 >=3.11）。`pyproject.toml` 是依赖的**唯一事实源**——不要另建 `requirements.txt`。
- 运行：`uv run main.py`（薄壳 → `ustplayer.app.main`）。`uv run ustplayer` 等价（`[project.scripts]`）；两条入口共用 `AppContext`。
- 类型检查：`npx --yes pyright main.py src`（Standard 模式，目标 **0 error**；仓库未提交 `pyrightconfig.json`，需要时本地创建）。PySide6 存根缺口优先改用限定枚举名（如 `Qt.AlignmentFlag.AlignCenter`）而非 `# type: ignore`。
- 测试：`uv run pytest`（222 个用例，覆盖 `contracts` / `ustreader` / `settings` 七个子域 / `settings_store` / `settings_manager` / `i18n` / `player` / `uplr_io` / `video_exporter` / `audio_backend`）。跑单个用例：`uv run pytest tests/test_uplr_io.py::test_export_import_round_trip`。`QT_QPA_PLATFORM=offscreen` 在 `tests/conftest.py` 顶层设置，无显示器 / CI 也能跑 Qt 测试；新增测试放 `tests/`，约定见 `tests/conftest.py`。
- 翻译：改 UI 字符串后必须 `pyside6-lupdate -extensions py src/ustplayer -ts i18n/ustplayer_zh_CN.ts i18n/ustplayer_en_US.ts i18n/ustplayer_zh_classic.ts`，再 `pyside6-lrelease i18n/*.ts`，并提交 `.qm`（运行时只读 `.qm`）。

## 架构（依赖方向）

```
UI 页面 ──构造注入──> AppContext（唯一组合根 / 门面）──> core 具体实现
                        ├─ ctx.settings     SettingsManager（设置门面，组装各子域）
                        ├─ ctx.parser       UstParser（契约）
                        ├─ ctx.player       PlayerLauncher（契约）
                        ├─ ctx.project_io   ProjectIO（契约，.uplr 导入 / 导出 / .uprd）
                        └─ ctx.video_exporter  VideoExporter（契约，MP4 视频导出）
```

- UI 层**只**依赖 `AppContext` 与 `contracts` 里的接口，**不得**直接 import core 具体实现。组合根在 `src/ustplayer/context.py`。
- `core/contracts.py`：数据契约（`UstInfo` / `NoteInfo` / `PlayerLaunchParams`）+ 服务 Protocol（`UstParser` / `PlayerLauncher` / `ProjectIO` / `VideoExporter`）+ 颜色 / 布尔工具 + `APP_VERSION`。
- `core/settings/`：每个分组一个信号驱动子域（`project` / `file` / `display` / `color` / `player` 含 `LyricSettings` / `language` / `theme`），各持属性 + `Signal` + `read_from` / `write_to` / `validate`；`SettingsManager` 只做组装与编排。
- `core/settings_store.py`：`Settings.json` 文件 I/O（分组→键值字典），首次运行自动迁移旧版 `Settings.ini`。
- `core/uplr_io.py`：`.uplr` 导入 / 导出（另负责 `.uprd` 视频工程导出）。**新版 = ZIP 容器**（`Info.json` + 资源，导入解压到 `%LOCALAPPDATA%\ustPlayer\projects\<工程名>-<hash8>\`）；**旧文本格式仅可导入**（按 ZIP 魔数自动识别）。导入经 setter 写设置 → 触发信号 → UI 自动同步。
- `core/ustreader.py`：只解析 `.ust` 文本（**不支持** `.ustx`），默认 Shift-JIS，编码错误抛 `UnicodeDecodeError`。
- `core/audio_backend.py`：伴奏音频后端封装——QtMultimedia 的降级导入与加载/播放/状态机收敛在此，`create_audio_backend` 返回 `AudioBackend` 或 None；`core/player.py` 只依赖该窄接口（无音频 / 失败时回退墙钟计时，降级瞬间重锚定不跳变）。QtMultimedia 相关改动改这里，播放器内不要直接 import。
- `core/video_exporter.py` + `core/renderer_ffi.py`：视频导出 —— ctypes 封装 uPlRender DLL（`ustplayer_renderer.dll`，从主程序 `renderer/` 子目录加载），渲染 MP4 并写 `.uprd` 工程文件。时序以「音频播完」为结束边界；`ffmpeg`/`ffprobe` 优先用程序目录内置（打包在 `ffmpeg/` 子目录），缺失回退 PATH。

## 关键约定（违反会静默出 bug）

- **新增 / 重命名设置项要同步改四处**：① 子域类（属性 + 信号 + `read_from`/`write_to`）→ ② `SettingsManager`（若参与播放参数）→ ③ `uplr_io.py`（`_settings_to_info_json` 导出 / `_apply_info_json` 导入）→ ④ UI 接线。漏一处会导致设置不生效 / 重启丢失 / `.uplr` 往返不完整。
- **存储层只存稳定英文 key**：枚举值（`lyric_pos` / `silent_display` / `end_display` / `pitch_placeholder`）在 `Settings.json` 与 `.uplr` 中始终是英文 key（`top` / `r` / `custom` / `none` 等），显示文案由 UI `tr()` 翻译；旧中文值由 `core/settings/player.py` 的 `migrate_value()` 兼容迁移。**不要把显示文案写回存储层**。
- **日志**：`from ustplayer.core.log import logger`（loguru），绝不 `print`；异常用 `logger.exception(...)`。日志不翻译。
- **错误码**：用户可见错误用 `InfoBar.error("ERcodeXXX", "提示文案", ...)`，新错误码登记到 `ERcode.txt`（001–012、999 已占用）。
- **i18n**：UI 字符串必须 `tr("中文原文")`（`from ustplayer.core.i18n import tr`，自由函数名必须叫 `tr`——lupdate 只认这个名字）。语言偏好存 `Settings.json` 的 `[LanguageSettings]`（默认 `system` 跟随系统），**不写入 .uplr**。

## 提交与发版陷阱

- 提交信息以 `feat:` / `fix:` / `docs:` / `refactor:` / `style:` / `chore:` 等常规前缀开头。含 `close #N` / `fixes #N` 会自动关闭 Issue。
- ⚠️ 提交信息以 `pass` 开头会**跳过 CI 构建**（留给纯文档提交）。**发版只由标签推送触发**（`git tag <版本> && git push origin <版本>`），提交信息不参与发版判定——以 `v` 开头的提交信息不会触发发版，发版由维护者负责。
- CI 从 `CHANGELOG.md` 的 `# {版本}` 一级标题提取 Release 说明（`v` 前缀可省略、连字符/空格与 tag 名互通、大小写不敏感）；顶部 `## Unreleased` 不被提取。用户可见变更补进 `## Unreleased` 的「更新内容」列表。
- 版本号用语义化（`pyproject.toml` 当前 `1.1.0b2` ↔ `contracts.APP_VERSION` "1.1.0 Beta 2"）；旧的日期式版本号（如 `v26f19`）已废弃，不要重新引入。
- `pyside6-fluent-widgets` 刻意**不带** `[full]` extra（那会引入 ~120MB scipy/numpy/pillow/colorthief 死依赖），不要"修复"这一点。

## 平台

仅 Windows：`ustplayer/ui/main_window.py` 使用 `winreg` 读取系统强调色，WSL / Linux 上无法运行。构建 / 发版只通过 GitHub Actions（windows-latest 上的 Nuitka standalone；另有 C++ `uplr_converter` 任务、CI 内 `cargo build` 编译 uPlRender 产出 `ustplayer_renderer.dll` 打进产物 `renderer/` 子目录，与发版打包）。

## 已 gitignore 的本地目录（不要依赖、不要提交）

`Settings.ini` / `Settings.json`（用户本地配置）；`test/` 与 `ustPlayer uplr sample/`（仅本地示例数据）。
