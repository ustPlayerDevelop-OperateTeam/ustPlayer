# 贡献指南（CONTRIBUTING）

首先，感谢你愿意为 **ustPlayer** 做出贡献！🎉

无论是报告 Bug、提出建议、完善文档，还是贡献代码，你的每一份帮助都让这个工具变得更好。在开始之前，请花几分钟读完这份指南——它能让协作更顺畅，也避免我们重复劳动。

## 目录

- [贡献方式](#贡献方式)
- [提问与反馈（Issue）](#提问与反馈issue)
- [本地开发环境搭建](#本地开发环境搭建)
- [项目结构](#项目结构)
- [架构与依赖方向](#架构与依赖方向)
- [.uplr 工程文件格式](#uplr-工程文件格式)
- [编码规范](#编码规范)
- [提交信息规范](#提交信息规范)
- [分支与拉取请求（Pull Request）](#分支与拉取请求pull-request)
- [版本与更新日志](#版本与更新日志)
- [测试与验证](#测试与验证)
- [许可与协议](#许可与协议)

## 贡献方式

你可以通过以下方式参与：

1. **报告 Bug / 提出建议**：在 [Issues](https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer/issues) 中反馈。
2. **贡献代码**：Fork 本仓库 → 修改代码 → 提交 [Pull Request](https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer/pulls)。
3. **完善文档**：修正 README、本指南、更新日志等文档中的错漏。

> [!TIP]
> 如果你只是想在社区里聊聊想法、看看别的用户怎么用，也可以先在[配布视频](https://www.bilibili.com/video/BV1YjcwzVEcX)的评论区逛逛，很多使用问题在那里可能已经讨论过了！

## 提问与反馈（Issue）

提交 Issue 前，请先确认：

- 你使用的是**最新的稳定版本**（查看[更新日志](https://github.com/SYEternalR/ustPlayer/releases/latest)），并阅读了 [README](README.md) 与 [开源协议](LICENSE)。
- 在 Issues 中搜索过，确认没有人提过相同的问题。

请使用已有的 Issue 模板，它们能帮我们更快定位问题：

- **🐛 Bug 报告**（[bug_report.yml](.github/ISSUE_TEMPLATE/bug_report.yml)）：请尽量填写
  - **版本号**：可在"其他"页面查看，或参考更新日志。
  - **操作系统**：如 Windows 11 23H2。
  - **错误码（ERcode）**：弹窗中显示的 `ERcode001` / `ERcode004` / `ERcode999` 等，对照 [ERcode.txt](ERcode.txt) 可初步判断问题。
  - **复现步骤**：一步步描述如何触发问题，越详细越好。
  - **日志 / 截图**：日志文件为程序目录下的 `ustPlayer.log`（不可写时回退到 `%LOCALAPPDATA%\ustPlayer`），拖拽日志文件或截图到 Issue 中即可。
- **✨ 功能建议**（[feature_request.yml](.github/ISSUE_TEMPLATE/feature_request.yml)）：请说明这个功能解决什么问题、期望的方案、以及你考虑过的替代方案。

> [!NOTE]
> 如果遇到崩溃或异常，**日志是定位问题的关键**，请务必附上软件运行目录下的相关日志。

## 本地开发环境搭建

### 环境要求

- **操作系统**：仅 **Windows**（代码中依赖 `winreg` 读取系统强调色，构建产物也是 Windows 平台）。
- **Python**：固定 **3.13.12**（见 [.python-version](.python-version)），最低要求 ≥ 3.11（[pyproject.toml](pyproject.toml) 声明）。
- **包管理器**：推荐使用 [uv](https://docs.astral.sh/uv/)（依赖锁定与虚拟环境都由它管理）。

### 安装 uv

任选一种方式：

```bash
# 方式一：winget
winget install astral-sh.uv

# 方式二：官方安装脚本（安装到 %USERPROFILE%\.local\bin）
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"

# 方式三：pip（需要已有 Python 环境）
python -m pip install uv
```

安装后可用 `uv --version` 验证。

### 第一步：克隆并安装依赖

```bash
git clone https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer.git
cd ustPlayer

# 使用 uv 同步依赖（自动创建 .venv 并按 .python-version 安装 Python）
uv sync
```

> `uv sync` 会按 [pyproject.toml](pyproject.toml) 安装 `PySide6`、`PySide6-Fluent-Widgets`、`loguru` 等依赖，并把项目本身以可编辑方式装入虚拟环境。项目依赖清单以 `pyproject.toml` 为**唯一事实源**，不要另建 `requirements.txt`。

### 第二步：运行

```bash
# 方式一：直接运行入口脚本（推荐，方便打断点调试）
uv run main.py

# 方式二：使用项目入口点（等价于安装后的命令）
uv run ustplayer
```

程序启动后，在"文件"页面选择一个 `.ust` 文件（支持 UTF-8 / GBK / Shift-JIS 编码，可用「编码检查」校验），点击"基础"页的「播放 Play」即可全屏预览。

> [!WARNING]
> 当前**只支持 .ust 文本格式**，`.ustx` 尚未支持——请勿声称支持 USTX，也不要引导 `.ustx` 进入解析器。

### 常见问题

- **启动报 `ImportError: No module named 'ustplayer'`**：确认执行过 `uv sync`，确保 `src/` 下的包已装入虚拟环境。
- **`.venv` 损坏 / Python 解释器失效**：删除 `.venv` 后重新 `uv sync`，或 `uv sync --reinstall` 强制重装。
- **界面没有应用主题 / 强调色**：首次运行时应用会自动从系统读取；在"其他"页面可以切换亮暗主题与强调色。
- **打开 .ust 中文乱码**：在"文件"页面切换编码（Shift-JIS / GBK / UTF-8），并用「编码检查」验证当前编码能否正常读取。
- **希望临时用 Pylance/pyright 检查类型**：见[测试与验证](#测试与验证)中的类型检查小节。

## 项目结构

```
ustPlayer/
├── main.py                     # 启动薄壳（唯一入口：uv run main.py → ustplayer.app.main）
├── pyproject.toml              # 项目元信息与依赖声明（唯一事实源）
├── uv.lock                     # uv 锁定文件（请勿手动编辑）
├── .python-version             # 固定 Python 3.13.12
├── ERcode.txt                  # 错误码字典（001–010、999 已占用）
├── UPDATELOG.md                # 更新日志（发布工作流从此提取 Release 说明）
├── LICENSE                     # GPL-3.0 开源协议
├── icon.ico / icon.png / icon-128.ico
├── .github/
│   ├── workflows/              # CI / 自动发布 / 自动关闭 Issue 工作流
│   ├── ISSUE_TEMPLATE/         # Issue 模板
│   └── pull_request_template.md
├── tools/
│   └── uplr_converter/         # C++17 旧版 .uplr → 新版 ZIP 转换器（零第三方依赖，随 Release 发布）
├── "ustPlayer uplr sample/"    # 本地示例工程（含大体积音频，不提交）
└── src/ustplayer/
    ├── app.py                  # 应用入口：QApplication + AppContext + MainWindow
    ├── context.py              # AppContext — 服务组装门面（UI 的唯一入口）
    ├── core/
    │   ├── contracts.py        # 数据契约 + 服务接口（UstParser / PlayerLauncher / ProjectIO）
    │   ├── log.py              # 日志系统（loguru，文件 + 控制台双输出）
    │   ├── settings_manager.py # SettingsManager — 设置门面（组装各设置子域）
    │   ├── settings/           # 设置子域（按配置分组拆分，各自持有属性 + 信号 + 分组读写）
    │   │   ├── project.py      #   [ProjectSettings]  项目名/曲名/作者/调音师/伴奏
    │   │   ├── file.py         #   [FileSettings]     ust 路径/编码/音高线
    │   │   ├── display.py      #   [DisplaySettings] 显示开关/全屏/歌词显示
    │   │   ├── color.py        #   [ColorSettings]   6 个颜色
    │   │   ├── player.py       #   [PlayerSettings]+[LyricSettings] 播放器样式/LRC
    │   │   └── theme.py        #   [ThemeSettings]   主题/强调色（不入 uplr）
    │   ├── settings_store.py   # Settings.json 存取（含旧版 Settings.ini 自动迁移）
    │   ├── uplr_io.py          # .uplr 导入/导出（实现 ProjectIO 接口）
    │   ├── ustreader.py        # UST 解析器（.ust 文本，不支持 .ustx）
    │   └── player.py           # 全屏播放器（QPainter 渲染 + QtMultimedia 伴奏）
    └── ui/                     # 界面（每个文件对应一个侧边导航页面）
        ├── main_window.py      # 主窗口（FluentWindow，侧边导航 + 播放编排）
        ├── basic_page.py       # 基础 — 项目信息、显示选项、播放
        ├── file_page.py        # 文件 — UST 选择、编码检查、预览
        ├── player_style_page.py# 播放器 — 颜色、歌词位置、静默/结束显示
        ├── lyric_page.py       # 歌词 — LRC 导入、显示开关
        └── other_page.py       # 其他 — 主题、协议、工具
```

> 新代码请放到对应模块中，避免单文件重新膨胀。

## 架构与依赖方向

项目采用**门面 + 接口**的模块化结构：

```
UI 页面 ── 构造注入 ──> AppContext（唯一组装点） ──> core 具体实现
                          │
                          ├─ ctx.settings    SettingsManager（设置门面）
                          ├─ ctx.parser      UstParser   （契约）
                          ├─ ctx.player      PlayerLauncher（契约）
                          └─ ctx.project_io  ProjectIO   （契约，.uplr 导入/导出）
```

- **UI 层只依赖 `AppContext` 与 `contracts` 里的接口**，不直接 import core 具体实现。
- **设置访问**统一走命名空间：`ctx.settings.<子域>.<属性>`，例如 `ctx.settings.display.show_bpm`、`ctx.settings.color.bg_color`；对应信号为 `<子域>.<属性>_changed`。
- **设置子域**（`core/settings/`）每个类负责：属性定义 + Qt Signal + 对应配置分组（原 ini 段，见 `Settings.json`）的 `read_from`/`write_to` + `validate` 校验；`SettingsManager` 只做组装、编排配置读写、校验与播放参数组装。设置持久化为 `Settings.json`（旧版 `Settings.ini` 首次启动自动迁移）。
- **数据流约定**：UI 修改设置 → 子域 setter 发信号 → 其他页面/主窗口实时同步；`sync_all_from_settings()` 只是导航切换时的兜底。
- **.uplr 导入**通过 setter 写入设置，因此导入后各页面会自动同步，无需手动刷新。

## .uplr 工程文件格式

`.uplr` 是 ustPlayer 的工程文件，存在两种格式（导入时按文件头自动识别）：

- **新版（ZIP 容器）**：内含 `Info.json` + 资源文件（`.ust` / `.lrc` / 伴奏音频）。`Info.json` 分四组：
  - `encoding`：UST 编码；
  - `basic`：项目名、ust/music 包内文件名、曲名、MIDI 作者、调音师；
  - `display`：显示开关（BPM / 播放时间 / 曲目信息 / MIDI 作者 / 调音师 / 全屏 / 歌词 / 音高线）；
  - `color`：6 个颜色；
  - `else`：歌词位置、LRC 包内文件名、静默/结束显示、音高占位符等。
  导入时资源解压到 `%LOCALAPPDATA%\ustPlayer\projects\<工程名>-<路径hash8>\`。
- **旧版（纯文本 key=value）**：仅导入兼容，不导出。

> [!IMPORTANT]
> **新增/修改设置项时，必须同时改四处**，否则会出现「设置不生效 / 重启丢失 / 工程文件不完整」：
> 1. 对应子域类（`core/settings/*.py`）：属性 + 信号 + `read_from`/`write_to`；
> 2. `SettingsManager`：如需参与播放参数，更新 `build_ust_info`；
> 3. `uplr_io.py`：`_settings_to_info_json`（导出）与 `_apply_info_json`（导入）；
> 4. 需要的话在 UI 页面接线（信号同步 + `sync_all_from_settings`）。

## 编码规范

请尽量贴合现有代码风格，这样 diff 更清晰、也更容易被 review：

- **语言**：注释、文档字符串、日志消息、UI 字符串使用**中文**（与现有代码一致）。
- **命名**：变量/函数/方法使用 `snake_case`，类使用 `PascalCase`，模块内部常量用 `UPPER_SNAKE_CASE`。
- **类型标注**：公开方法尽量写类型标注（如 `-> str`、`-> UstInfo`、`Optional[QWidget]`）。
- **日志**：使用 `from ustplayer.core.log import logger`，不要直接 `print`；异常场景用 `logger.exception(...)` 记录堆栈。
- **段落分隔**：大文件内用 `# ===================== 段落名 =====================` 分隔不同功能块。
- **错误提示**：新增用户可见的错误时，遵循 `InfoBar.error("ERcodeXXX", "提示文案", ...)` 模式；如果错误类型是新的，请在 [ERcode.txt](ERcode.txt) 登记新错误码（001–010、999 已占用）。
- **类型检查**：项目以 **Pylance / pyright Standard 模式** 为准，新代码应保持 0 error（详见[测试与验证](#测试与验证)）。确属 PySide6 存根缺口的场景（如枚举类级别名）优先改用限定枚举名（`Qt.AlignmentFlag.AlignCenter` 等），必要时才加 `# type: ignore[规则名]` 并附注释说明原因。
- **降级导入**：QtMultimedia 等可选能力的降级导入用 `try/except` + `TYPE_CHECKING` 别名（参考 `core/player.py` 顶部），保持类型可检查。
- **不要破坏现有行为**：`.uplr` 导入/导出必须覆盖全部配置项，新增配置项时按上文「四处」规则同步。

## 提交信息规范

约定简洁的提交信息能让历史更易读，也能让自动化的 CI / 发布流程正常工作：

- **功能**：`feat: 简要描述`
- **修复**：`fix: 简要描述`
- 其他：`docs:`、`refactor:`、`style:`、`chore:` 等常规前缀均可。

### 关联并自动关闭 Issue

提交信息中包含以下关键字之一时，CI 会在合并后自动关闭对应 Issue（见 [auto-close-issue.yml](.github/workflows/auto-close-issue.yml)）：

```
close #1
fixes #2
```

### ⚠️ 两个需要避开的提交前缀

- 提交信息以 `pass` 开头会**跳过 CI 构建**——这是留给"纯文档/不需要构建"的提交用的，普通提交不要用。
- 提交信息以 `v` 开头（且非 `pass`）会**触发自动发版**。版本发布由维护者负责，普通开发提交请避免以 `v` 开头，防止误触发 Release。

## 分支与拉取请求（Pull Request）

### 分支

- 建议基于 `main` 分支新建分支，分支名用简短的主题词，例如 `fix-lyric-cjk-clip`、`feat-encoding-preview`。
- 尽量**一个 PR 只做一件事**：修 Bug 的 PR 不要顺带夹带新功能，反之亦然。

### 流程

1. Fork 本仓库并克隆到本地（见[本地开发环境搭建](#本地开发环境搭建)）。
2. 新建分支并提交你的修改。
3. **本地测试通过后**再推送并创建 PR（见[测试与验证](#测试与验证)）。
4. 提交 PR 时请填写 [pull_request_template.md](.github/pull_request_template.md)：
   - 描述 PR 做了什么。
   - 如有关联的 Issue，用 `close #N` / `fixes #N` 格式列出。
   - 如有破坏性变更，请**详细说明**改了什么、影响什么，笼统的"改了 XXX 模块"无法定位变更，将不被合并。
   - 完成检查清单（本地运行/编译无报错、功能可用）。
5. 建议勾选 **"Allow edits and access to secrets by maintainers"**，方便维护者帮你调整。

### Review 注意事项

- 保持 PR 范围聚焦，过大 / 过散的 PR 会拖慢 review。
- 如果 review 中提出了修改意见，请在同一分支继续提交，保持历史连贯（维护者也可以代为修改）。
- 维护者会在确认后合并并安排发版。

## 版本与更新日志

### 版本号规则

ustPlayer 在 v26f19 及之前的版本使用**日期式版本号**：`v{年份后两位}{月份字母}{日}`，其中月份用字母表示：`a`=1月，`b`=2月，…，`f`=6月，依此类推。

例如 `v26f19` = 2026 年 6 月 19 日。

在 v26f19 之后，ustPlayer 采用**语义化版本号**。下方为新旧版本对照表：

| 原版本号 | 现版本号 | 备注 |
|---|---|---|
| v26f19 | 1.0.0 | 基于 PySide6 重构了整个 uPl，故递增 X |
| v26b10 | 0.2.2 | |
| v26b06 | 0.2.1 | |
| v26a31 | 0.2.0 | 修复了若干 bug 并增添了新功能，故递增 Y |
| v26a24 | 0.1.1 | uPl 的第一个正式版本 |
| **alpha** | 0.1.0 | uPl 的 demo |

### 更新日志（UPDATELOG.md）

发布工作流会从 [UPDATELOG.md](UPDATELOG.md) 中提取对应版本的说明作为 Release 内容，因此**格式必须严格一致**：

- 每个已发布版本一个 `# v{版本号}` 一级标题（如 `# v1.0.0`），其下内容到下一个 `# v` 标题之前，会作为该版本的 Release 说明。
- 顶部 `## Unreleased` 小节**不会被发布提取器读取**，仅供开发期记录待发布改动。
- 如果 PR 改了用户可见的行为，请把对应条目补充到 `## Unreleased` 的「更新内容」列表中（发布前由维护者整理进对应版本段）。

```markdown
## Unreleased

### 更新内容

* 修复：……；
* 新增：……。

---

# v1.0.0

> 该版本的正式说明……
```

> 我们不再提供 .msi 安装包，也不提供 .app 格式程序；正式版以 Windows 可执行文件（Release 页）与源码两种形式分发。

## 测试与验证

提交 PR 前请至少完成：

- [ ] **类型检查**：`pyright` Standard 模式 0 error（见下方说明）。
- [ ] 本地运行 `uv run main.py` 能正常启动，无报错。
- [ ] 你修改的功能 / 修复的 Bug 经实际使用验证过（例如：选择 UST 文件 → 点击播放 → 正常显示；或验证相关页面设置生效）。
- [ ] 如修改了 `.uplr` 工程文件的读写，验证导入/导出的完整性（导出 → 清空 → 导入，确认全部配置项无损）。

> 目前项目还没有自动化测试框架，**手动验证 + 类型检查**是当前最主要的保障手段。

### 类型检查（Pylance / pyright Standard 模式）

项目代码以 **Standard 模式**为目标（`reportMissingImports` 为 error，常规运行时问题都会暴露）。两种方式任选：

**方式一：VS Code + Pylance（推荐）**

1. 打开项目，命令面板选择 Python 解释器为 `.venv`；
2. 在 `.vscode/settings.json`（或用户设置）中加入：
   ```json
   {
       "python.analysis.typeCheckingMode": "standard"
   }
   ```
3. 若 Pylance 报 `ustplayer` 包无法解析（src 布局），给 `python.analysis.extraPaths` 加 `"src"`。

**方式二：命令行 pyright（需要 Node.js）**

在仓库根创建 `pyrightconfig.json`（本项目未提交该文件，可按需自行添加）：

```json
{
    "typeCheckingMode": "standard",
    "pythonVersion": "3.13",
    "pythonPlatform": "Windows",
    "extraPaths": ["src", ".venv/Lib/site-packages"],
    "reportMissingImports": "error"
}
```

然后运行：

```bash
npx --yes pyright main.py src
```

预期输出：`0 errors, 0 warnings, 0 informations`。

> [!NOTE]
> PySide6 的部分存根缺口（如 `QFont.Bold` 类级别枚举名）是已知误报，优先改用限定枚举名（`QFont.Weight.Bold`）；见[编码规范](#编码规范)。

## 许可与协议

- 本项目以 **GPL-3.0** 协议开源（见 [LICENSE](LICENSE)）。**提交代码即表示你同意你的贡献以 GPL-3.0 协议授权本项目使用**。
- 请尊重作者（SYEternalR）的署名与使用约定，不要将本项目冒充为自己的成果，也不要删除 / 篡改随程序附带的版权信息、ERcode.txt 等文件。
- 本项目开发过程中使用了 AI 工具辅助开发，相关信息会在 README 中说明。

---

再次感谢你阅读到这里！🎉

如果这份指南有什么不清楚的地方，欢迎直接在 Issues 里提问。期待你的贡献，玩得开心！
