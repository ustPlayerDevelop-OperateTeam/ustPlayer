# 2.0.0 Alpha 1

> [!WARNING]
>
> 我们不推荐您在日常环境和生产环境中使用本版本，请使用[1.0.0](https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer/releases/tag/1.0.0)版本！

> [!IMPORTANT]
> **ustPlayer 2.0.0 Alpha 1 是把整个程序从 Python / PySide6 重写为 C# / .NET 10 + Avalonia 11 的迁移版本。**
> 界面、播放与视频导出的行为以 1.1.x 为基准对齐；设置文件与 `.uplr` / `.uprd` 工程格式保持兼容。
> 1.1.x 的实现已冻结在仓库的 `pysourcecode/` 目录，仍可对照使用。
> 应用的迁移工作基本完成，但是还有很多小bug，欢迎您来协助开发^^


## 🏗️ 底层重构（面向开发者的变更）

- **跨平台**：Windows / macOS / Linux 三平台构建（Android 头工程骨架已就位，见 `docs/plan-deviations.md` D1）。
  原先仅 Windows 的部分（读取系统强调色曾用 `winreg`）改为运行时判断。
- **分层与约束由测试守住**：逻辑层不得引用 UI 框架、共享工程不得出现平台条件编译、
  翻译资产必须部署到程序目录——这些红线都有测试强制，不再依赖人工记得。
- **打包与发版流程**：新增 `build/publish.ps1`（打包并**校验产物完整性**，缺件即失败）与
  `build/extract-release-notes.ps1`（提取本文件的小节并附 SHA256 校验表）；
  CI 增加日常打包校验与标签驱动的草稿发版作业。