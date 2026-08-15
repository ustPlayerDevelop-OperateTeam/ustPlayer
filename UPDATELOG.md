## Unreleased

### 更新内容

* 重构：所有模块解耦，统一通过 `AppContext` 服务门面与数据契约（`core/contracts.py`）互相调用，页面不再直接依赖核心实现；
* 新增新版 .uplr 工程格式：ZIP 容器（`Info.json` + 工程/歌词/伴奏资源打包），旧版文本 .uplr 仍可导入；
* 新增「伴奏音乐」选项：播放器可播放伴奏并与画面同步，无音频或解码失败时自动降级为纯可视化；
* 新增 C++ 转换器 `tools/uplr_converter`（旧版 .uplr → 新版，零第三方依赖），随 Release 发布；
* 修复：「编码检查」现在会真实检测当前编码能否读取文件，失败时提示切换编码；
* 修复：ERcode008/009 已登记进 ERcode.txt；
* 修复：配置读写失败改用日志记录，打包版不再因缺少控制台而崩溃；
* 修复：日志文件位置调整，优先写入程序根目录，不可写时回退用户数据目录；
* 清理：移除冗余文件 Terms.txt；
* 入口迁移至 `ustplayer.app`，`main.py` 仅作启动薄壳。
* 修复：「歌词色」选项现在真正作用于播放器 LRC 歌词渲染（此前配置了但未生效）；
* 移除「显示波形 / 显示音素 / 显示MIDI号」选项：波形依赖的 QtMultimedia QAudioProbe 在 Qt6.8/PySide6 6.11 中已移除，三个开关的渲染与配置代码一并清理；
* 重构：`SettingsManager` 拆分为设置域（`settings_manager.py`，信号驱动属性 + ini 映射）、ini 文件存取（`settings_store.py`）与工程文件导入导出（`uplr_io.py`）；`AppContext` 新增 `project_io` 统一接口（`contracts.ProjectIO`），UI 页面统一经接口导入导出工程；
* 重构：设置域进一步按 ini 段拆分为 `core/settings/` 六个子域类（project / file / display / color / player / theme，各自持有属性 + 信号 + 段读写 + 校验），`SettingsManager` 仅做组装与编排；UI 访问改为 `ctx.settings.<子域>.<属性>`；
* 修复：导出 .uplr 时未手打扩展名会自动补全 `.uplr`；
* 修复：播放时间超过 1 小时时不再显示错乱的分钟数（HH:MM:SS:CC）；
* 修复：伴奏音频播完时优先取媒体总时长作为时间锚点，避免时间轴回跳；
* 修复：新版 .uplr 重复导入前会清空旧缓存目录，不再混入过期资源；
* 修复：旧版文本 .uplr 支持 GBK/Shift-JIS 等多编码读取，且对跨机器失效的资源路径记录日志提示；
* 修复：导出 .uplr 时不同目录下的同名资源自动重命名，避免 ZIP 内条目互相覆盖；
* 修复：Settings.ini 写入在程序目录只读时回退到用户数据目录；读取时校验修正的默认值会写回文件；
* 重构：设置持久化由 `Settings.ini` 迁移至 **`Settings.json`**（结构为「分组 → 键值」字典，键与旧 ini 段保持一致）；旧版 `Settings.ini` 首次启动自动迁移，迁移成功后删除旧文件；
* 修复：编码预览非严格模式的死代码清理；
* 修复：Pylance/pyright Standard 模式类型检查问题——接口契约返回类型、音符对象类型标注、PySide6 枚举改用限定名（如 `QFont.Weight.Bold`）、`Optional[Callable]` 等；
* 其他：选择 LRC 文件时起始目录与文件页保持一致。
* 美化：各页面的 `/ XXX` 分区改为卡片式分区（卡片头部为「主题色竖线 + 分区名」），基础/播放器/歌词/其他四个页面风格统一；
* 新增「窗口效果」设置（其他页 → 主题卡片）：可切换 **关闭 / 亚克力 / Mica** 三档窗口背景效果，随设置持久化、重启记忆；Mica 仅 Win11 生效（其余系统自动回退纯色），亚克力 Win10/11 均可用。
* 新增：支持把 `.uplr` 工程文件直接拖入主窗口导入（仅**基础页**响应）；**文件页**支持拖入 `.ust` 文件自动填入路径，**歌词页**支持拖入 `.lrc` 自动填入歌词路径，其余页面不接受拖拽。

---

# v1.0.0

>[!CAUTION]
>
>ustPlayer 1.0.0 (v26f19)（当前新版本）仅支持 64位Windows10及更晚的版本 ，若您的设备不符合条件，请您使用uPl-v26b10的旧版本 —— 它同样被提交在该Release下。

新版本的ustPlayerform产出画面与旧版本无异，您可以选择对您的既有uPl进行升级迭代，也可以选择维持原有版本持续使用。

旧版本的.uplr工程文件可以被此Release版本读取，但此版本产生的工程文件将会包含旧版本不支持的选项。

## 更新内容

* 对GUI框架进行重写，由 Tkinter 全面迁移至 PySide6 + Fluent Design；
>- 用图标的侧边导航替代了原来的手绘标签页。
>- 采用 PySide6-Fluent-Widgets 组件库。
>- 原来基础页的复选框全部换成 Switch 按钮。
>- 操作成功/失败的提示改为顶部滑入的 InfoBar。
* 新增「音高线颜色」选项；
* 新增 新增 loguru 日志模块 ，您可以在其产生的日志及控制台反馈文件中找到问题所在；
* 颜色选择从系统对话框升级为 Fluent 风格的内置取色面板；
* .ust文件的编码检查逻辑由原本“用户目视文件内容并确认其可读取性”变更为“触发式uPl自动检查功能”；
* 大范围调整了代码的结构，优化其他贡献者的体验；
* 优化了产出画面的清晰度；
* 增强了部分代码的鲁棒性。

---

## 项目结构
```Bash
ustPlayer/
├── main.py                     # 启动薄壳（唯一入口：uv run main.py）
├── pyproject.toml              # 依赖与打包声明（唯一事实源）
├── src/ustplayer/
│   ├── app.py                  # 应用入口：QApplication + 主窗口装配
│   ├── context.py              # AppContext — 模块间统一调用门面
│   ├── core/
│   │   ├── contracts.py        # 数据契约与接口（UstInfo/PlayerLaunchParams/Protocol）
│   │   ├── settings_manager.py # 配置管理器（信号驱动）
│   │   ├── ustreader.py        # UST 文件解析器（UstFileReader）
│   │   ├── player.py           # 全屏播放器（QPainter 渲染）
│   │   └── log.py              # 日志系统（loguru）
│   └── ui/
│       ├── main_window.py      # 主窗口（侧边导航）
│       ├── basic_page.py       # 基础 — 项目信息、显示选项、播放
│       ├── file_page.py        # 文件 — UST选择、编码检查、预览
│       ├── player_style_page.py# 播放器 — 6色选择、显示设置
│       ├── lyric_page.py       # 歌词 — LRC导入、显示开关
│       └── other_page.py       # 其他 — 版权、工具、协议
├── tools/
│   └── uplr_converter/         # C++ 旧版 .uplr → 新版 转换器
└── ...
```
