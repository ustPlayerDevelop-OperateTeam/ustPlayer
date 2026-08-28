## Unreleased

## 🐛 问题修复

- **修复所有 InfoBar 提示 1 秒即消失且布局错乱**：`InfoBar.error/success/warning` 的毫秒数此前被当作第 3 个位置参数传给了 `orient`（`duration` 保持默认 1000ms，`orient=5000` 非法导致提示渲染成竖排）。全应用 23 处调用改为关键字传参（`duration=3000/5000/7000`），错误提示恢复 5~7 秒展示时长与横排布局。
- **修复带 UTF-8 BOM 的 UST 首段被静默丢弃**：`\ufeff` 残留首行导致 `[#VERSION]`/`[#SETTING]`/首个音符段匹配失败（版本号丢失；若首行即音符段，该音符整段丢失且无提示）。解析器对 UTF-8 家族改用 `utf-8-sig` 打开，自动吞掉 BOM。
- **修复 UST 速度值（Tempo）无边界校验**：`Tempo=0`/负数/`NaN`/`Inf` 此前原样接受，`Tempo=0` 会让播放器时间轴永远停在 0:00（无提示卡死）。解析器与播放器双端校验，非法值统一回退 120 BPM（与视频导出侧防护一致）。
- **修复 .uplr 导入失败时设置被部分篡改并落盘**：`_apply_info_json` 边校验边赋值，遇到 Info.json 中不安全资源路径抛异常时已赋值属性不回滚，退出时会被 `write_settings` 持久化。导入改为事务化——失败即回滚全部已触碰设置并清理半成品缓存，导入失败 ≠ 状态被污染。
- **修复音频失效降级时播放时间轴跳变**：音频初始化失败 / 看门狗判定异常降级为纯可视化后，时间轴此前从 0:00 重新走墙钟（若音频一直未就绪，画面会从 0:00 直接跳到约 9 秒处）。降级瞬间改为以当前播放位置重锚定墙钟零点，时间轴连续不跳变。
- **修复播放完误报"音频未进入播放状态"WARNING 并误降级**：Qt 的 FFmpeg 后端在播放结束后会把 `mediaStatus` 回落为 LoadedMedia（而非停留 EndOfMedia），3 秒后触发的一次性看门狗据此把"已播完"误判为"已加载但未播放"而降级（短伴奏尤其必现）。看门狗现增加「已播完」排除：`_media_finished` 已置位或后端处于 EndOfMedia 状态时不再降级；后者（信号未发出的兜底情形）会补记播完锚点。
- 新增回归测试：UTF-8 BOM（版本/首音符）、Tempo 非法值（0/-5/nan/inf）、导入失败后设置回滚与缓存清理、音频状态机（就绪只播一次 / 播完锚点 / 看门狗降级 / 降级时间轴连续）。

## 🏗️ 底层重构（面向开发者的变更）

- **封装伴奏音频后端**：新增 `core/audio_backend.py`——QtMultimedia 的降级导入（`try/except` + None 占位）与加载/播放/状态机从 `player.py` 迁移至此，`create_audio_backend` 工厂按环境返回 `QtAudioBackend` 或 `None`；播放器只依赖 `AudioBackend` 窄接口（`media_ready`/`media_ended`/`media_error` 信号 + 位置/时长/媒体阶段布尔查询），不再直接 import QtMultimedia。音频状态机由此可在无音频设备环境用可编程 fake 后端测试（新增 `tests/test_audio_backend.py`，11 个用例）。
- **修正音频看门狗「EndOfMedia 兜底」分支**：`is_finished()` 判断此前嵌套在 `is_loaded()` 分支内，而 `LoadedMedia/BufferedMedia` 与 `EndOfMedia` 是互斥状态，该分支对真实后端不可能执行（回归测试亦模拟了不会出现的状态组合）；现提前到最前判断，与「信号已发（`_media_finished`）」一起构成完整的播完排除。
- **统一更新日志文件名大小写**：Git 跟踪名统一为 `CHANGELOG.md`，与 CI 提取脚本、README / CONTRIBUTING / AGENTS / CLAUDE 的引用保持一致（此前跟踪名为 `ChangeLog.md`，仅靠 Windows 大小写不敏感文件系统才不失效）。
- **同步三份翻译源文件并清理过期条目**：按当前代码重建 `i18n/*.ts`（`lupdate -no-obsolete`），移除已从代码消失的 17 条旧文案（`i18n/ustplayer_en_US.ts`、`i18n/ustplayer_zh_classic.ts` 此前滞后于 `i18n/ustplayer_zh_CN.ts`），并重建 `.qm`。英文/文言翻译完成度 30/144，欢迎 PR 补全。
- **内置 FFmpeg**：构建流程（`build.yml`）下载 ffmpeg/ffprobe 并打进产物的 `ffmpeg/` 子目录；视频导出的混流与伴奏时长探测（`video_exporter._find_tool`）**优先使用程序目录内置版本**，缺失时才回退 PATH——用户不再需要自行安装 FFmpeg 或配置环境变量。
- **修复 CI 的 FFmpeg 动态库复制正则**：`^(av|sw)\d` 无法匹配 `avcodec-61.dll` / `swresample-5.dll` 等（av/sw 后是字母、版本号在连字符之后），导致打包产物缺失全部 FFmpeg 动态库；改为 `^(av|sw)[a-z]*-\d`。
- **发版版本匹配大小写兜底**：tag 写成 `v1.1.0-beta-2`（小写）也能匹配到 `# 1.1.0 Beta 2` 小节（ChangeLog 提取正则加 `(?i)`），不再因大小写不一致中止发版。
- **文档同步**：`CONTRIBUTING.md` / `CLAUDE.md` 的发版机制描述更正为现行规则——发版**只由标签推送触发**、提交信息不参与发版判定、ChangeLog 小节为 `# {版本}` 一级标题（`v` 前缀可省略、连字符/空格互通、大小写不敏感）、版本校验为「tag ↔ `contracts.APP_VERSION`」；版本号示例同步为 1.1.0b2。Issue 模板同步修正：Bug 报告中的版本号示例更新为语义化版本（`1.1.0 Beta 2`）、日志路径更正为程序目录下的 `ustPlayer.log`（不再指向不存在的 `logs/` 文件夹）、"更新日志"链接改为 `ustPlayerDevelop-OperateTeam/ustPlayer` 组织仓库地址。

# 1.1.0 Beta 2

> [!NOTE]
>
>欢迎加入拾忆的小群^ ^群号[882598164](https://qm.qq.com/q/BspSouY6I0)!

>[!WARNING]
>
>我们并不推荐您于生产环境中使用测试版本的ustPlayer，请使用目前的正式版本[1.0.0](https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer/releases/tag/1.0.0)。

>[!TIP]
>
>若您在使用渲染功能的时候出现问题，请[点击这里](https://github.com/ustPlayerDevelop-OperateTeam/uPlRender/issues)进入uPlRender的项目Issue页来反馈，目前渲染功能还不是很稳定，请多多反馈^ ^
>
>渲染功能依赖系统环境变量的FFmpeg，如果系统环境变量没有请在[FFmpeg官网](https://ffmpeg.org/)下载并添加到系统环境变量中，如果您的系统运行着Windows 10 1709及以上版本也可以使用`winget install --id Gyan.FFmpeg -e`来调用系统自带的WinGet安装。

## 🎉 新功能

- **导出视频**：一键把当前工程渲染为 MP4（"基础"页 → 项目卡片 → 导出视频）。渲染复用 uPlRender（`ustplayer_renderer.dll`）逐帧绘制，输出与播放器画面一致的视频；可混入伴奏音频（默认开启，由外部 `ffmpeg` 完成）。分辨率 / 帧率 / 是否混音在对话框中临时选择。同时会落一个 `.uprd` 工程文件（配置 + 资源 + `video` 段），便于复用与二次导出。**时序与播放器一致**：有伴奏时以“音频播完”为结束边界，音符 tick 结束后、音频仍未播完的区间显示空拍/静默文字，音频播完后显示结束文字并保留 1 秒；无伴奏时按音符 tick 总长。导出视频窗口做成 **Fluent 风格弹窗**（遮罩幕 + 居中圆角卡片 + 随主题的强调色主按钮）。
- **工程缓存管理**：`.uplr` / `.uprd` 解压产物改放到**程序目录下 `cache/`**（不可写时回退用户数据目录）；「其他」页新增**工程缓存卡片**——显示「缓存占用：XXX」并提供**清除缓存**按钮（带确认）。
- **打开日志**：「其他」页新增日志卡片，一键用记事本打开应用日志文件（`ustPlayer.log`）。

## 🐛 问题修复

- **修复导入新版 .uplr / .uprd 后播放音频卡死**：音频长时间处于「加载中」且未进入可播放状态时，看门狗不再无限重试，改为**超限后降级为纯可视化计时**，避免时间轴停在 0:00 表现为卡死。
- 修复播放器在「音符 tick 结束后」立即显示结束文字并提前关闭的问题：改为**音频驱动**收尾——有伴奏时，音符内容结束但音频未播完的区间显示空拍/静默文字，音频播完后才显示结束文字并停留 1 秒关闭（不再提前掐断伴奏）；与导出视频的时序一致。同时：**播完后不再重播**（`play()` 只调用一次 + `_media_finished`/`_end_shown` 守卫，音频播完或被再次触发 `LoadedMedia` 时均不再自动播放），并**显示结束文字时隐藏左下角秒表**（对应视频渲染器 `ustplayer_renderer.dll` 已同步更新，同样渲染结束画面时隐藏秒表）。
- 修复 .uplr 解压的路径穿越防护：拒绝带盘符 / 绝对路径的压缩条目，防止恶意工程文件把内容解压到缓存目录之外。
- 修复 .uplr 解压遇到目录条目直接报错的问题；解压改为分块流式写入并加入单成员 / 总量上限，防 zip bomb 耗尽内存或磁盘。
- 旧版文本 .uplr 经 C++ 转换器转换时，GBK / Shift-JIS 编码的内容会正确转成 UTF-8（中文项目名 / 路径 / 自定义文字不再乱码）。
- 播放器启动失败改用 ERcode005 提示（此前统一归入 ERcode999）。
- 切换 UST 编码后内容预览立即刷新（此前需改动路径或手动点「编码检查」才会重读）。
- `Settings.json` 改为临时文件 + 原子替换写入，避免崩溃/断电留下半截配置。
- 修复语言切换后「关于软件」区链接的提示文字不随语言更新。
- **修复播放器音频彻底失效的严重 bug**：`QMediaPlayerType` 仅在类型检查时定义，运行时引用的 `_on_media_status` / `_check_audio_ready` / 类型注解均抛 `NameError`，被 `try/except` 吞掉后音频永远无法播放、播放器卡在 0:00。改用 `QMediaPlayer` 直接引用并加 `# pyright: ignore` 注释。
- 改进音频看门狗逻辑：不再固定 3 秒超时降级，改为按 `mediaStatus` 判断——仅已加载但未播放 / 媒体无效时降级，仍在加载中则再等 3 秒。
- **安全加固**：导入 .uplr / .uprd 时，Info.json 中登记的资源路径（ust / music / lrc）现与 ZIP 成员执行同一套防护——含 `..` 穿越、绝对路径、盘符前缀的记录会整体拒绝导入。此前仅压缩条目受保护，恶意工程的 Info.json 可把缓存目录之外的任意本机文件登记为工程资源。
- **修复程序目录可写性探测**：Windows 的 `os.access` 不检查 ACL，应用安装在 Program Files 等受限目录时会误报「可写」，导致日志 / 设置 / 工程缓存静默写入失败。改为真实写入临时探针文件验证，不可写时正确回退 `%LOCALAPPDATA%\ustPlayer`。
- **视频导出取消即时生效**：进入 ffprobe 探测时长 / ffmpeg 混流阶段后，取消请求现在约 0.2 秒内生效（此前需等子进程自然结束，混流最长可达 1 小时无响应）；导出失败或中途取消时**自动清理半成品 MP4 与孤儿 .uprd 文件**。
- ffmpeg 混流失败的错误信息现在附带 stderr 尾部内容，便于排查；ffprobe / ffmpeg 改以 PATH 解析出的绝对路径调用，规避 Windows 按当前目录搜索带来的劫持风险。

## 🏗️ 底层重构（面向开发者的变更）

- 类型检查：修复 pyright Standard 模式下的残留类型错误（当前 0 error）。
- 文档同步：AGENTS.md 补上自动化测试说明与 language 设置子域、lupdate 三语命令；README / CONTRIBUTING 中 `UPDATELOG.md` 引用统一更正为 `ChangeLog.md`（原文件已更名）。
- 转换器输出的 `Info.json` 不再包含新版已移除的 `show_phoneme` / `show_midinote` / `show_waveform` 字段。
- 新增 `.gitattributes` 统一跨平台换行符处理，消除 git diff 时的 CRLF/LF 警告。

## 🚀 构建与发布

- Nuitka 打包补充简体中文翻译资源（此前仅打包英 / 文言两个 .qm）；移除 setup-python 中未实际使用的 pip 缓存配置。
- 「提交信息含 close #N 自动关闭 Issue」在 dev 分支同样生效（此前只在 main 分支触发，而实际开发均在 dev 进行）。
- 发版机制调整：转换器（`uplr_converter`）改为**独立压缩包**发布，不再并入 ustPlayer 主包；ustPlayer 主包只打包编译产物文件夹内的内容，不再包含外层目录。
- 发版说明提取：ChangeLog 小节标题里的空格与 tag 名里的连字符现在可互相匹配（手动推送 `v*` 标签触发发版时不再丢失更新说明）。
- **修复自动发版机制**：CHANGELOG 小节标题匹配改为容错（`# v1.1.0 Beta 2` / `# 1.1.0 Beta 2` / `#1.1.0-Beta-2` 均可，此前要求必须带 `v` 前缀导致永远匹配失败）；找不到对应小节时**直接中止发版**并提示现有标题，不再静默发布占位符说明的错误版本。
- 发版新增**版本一致性校验**：提交信息/tag 推导出的版本与 `contracts.APP_VERSION` 不一致时中止（拦截手滑写错版本号的误发布）。
- Release 改为**草稿创建**，人工核对标题/说明/产物后手动 Publish；修正 checkout 浅克隆导致已有同名 tag 检测失效的问题。
- Release 说明末尾自动附加**SHA256 校验表**：列出每个发布附件的文件名与哈希值（`[!important]` 提示框），用户下载后可离线核对完整性。
- **发版触发方式变更**：改为**仅由标签推送触发**（`git tag <版本> && git push origin <版本>`，标签名带不带 `v` 前缀均可），Release 直接挂在推送的标签上；提交信息不再参与发版判定，杜绝「消息手滑写错版本号导致误发版」。
- **修复 Nuitka 打包后伴奏音频无法播放**：Nuitka `pyside6` 插件只打包了 `QtMultimedia.pyd` 与 Qt 库，未打包 QtMultimedia 的媒体后端插件（`plugins/multimedia/ffmpegmediaplugin.dll` / `windowsmediaplugin.dll`）与 FFmpeg 动态库（`avcodec-61.dll` 等），导致编译后的 `QMediaPlayer` 找不到任何后端、音频静音。现已在构建流程中把这两类运行资源一并复制进产物树（`build.yml`）。