## Unreleased

### 更新内容

* 重构：所有模块解耦，统一通过 `AppContext` 服务门面与数据契约（`core/contracts.py`）互相调用，页面不再直接依赖核心实现；
* 新增新版 .uplr 工程格式：ZIP 容器（`Info.json` + 工程/歌词/伴奏资源打包），旧版文本 .uplr 仍可导入；
* 新增「伴奏音乐」选项：播放器可播放伴奏并与画面同步，无音频或解码失败时自动降级为纯可视化；
* 复活「音素 / MIDI号 / 波形」三个显示开关并真正接线（播放器渲染 + uplr 导入导出）；
* 新增 C++ 转换器 `tools/uplr_converter`（旧版 .uplr → 新版，零第三方依赖），随 Release 发布；
* 修复：「编码检查」现在会真实检测当前编码能否读取文件，失败时提示切换编码；
* 修复：ERcode008/009 已登记进 ERcode.txt；
* 修复：配置读写失败改用日志记录，打包版不再因缺少控制台而崩溃；
* 修复：日志文件位置调整，优先写入程序根目录，不可写时回退用户数据目录；
* 清理：移除冗余文件 Terms.txt；
* 入口迁移至 `ustplayer.app`，`main.py` 仅作启动薄壳。

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
