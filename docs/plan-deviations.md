# 迁移计划的偏差记录

> 记录 2.0 迁移相对**已批准计划**的偏差及其理由。新增偏差请追加，不要删改历史条目。

## D1：Android 头工程暂不纳入解决方案

- **计划要求**：`ustPlayer.Android/` 头工程骨架，Phase 1 内编译通过。
- **实际**：已实测本机 `.NET SDK 10.0.400` **未安装任何 workload**（`dotnet workload list` 为空），
  `net10.0-android` 目标无法还原与编译。
- **处理**：暂不创建 Android 工程，避免在每次 `dotnet build UstPlayer.slnx` 时制造失败步骤。
  `Directory.Packages.props` 已预置 `Avalonia.Android` 版本；共享工程的分层约束测试
  （`共享工程不得出现平台条件编译`）已就位，保证 Android 接入时不需要重构。
- **解除条件**：安装 `dotnet workload install android` 后补建工程骨架，并把
  `ApplicationManifest` 一类的 Windows 专属设置按平台条件隔离（见 Desktop 工程的既有做法）。

## D2：测试工程合并为单一 `ustPlayer.Tests`

- **计划要求**：`UstPlayer.Core.Tests`、`UstPlayer.Renderer.Tests`、`UstPlayer.App.Tests` 三个。
- **实际**：合并为一个 `ustPlayer.Tests`。
- **理由**：项目全部代码位于共享工程 `ustPlayer` 内，三个测试工程会各自产生一份
  `ustPlayer.Core.dll` 引用与一份 Avalonia 依赖，而职责边界在测试内已由目录
  （`Architecture/`、`Renderer/`、`Timing/`、`Models/`）清晰表达。合并减少编译与还原开销，
  且 `InternalsVisibleTo` 只需维护一处。
- **影响**：无功能影响。若将来把 `UstPlayer.Renderer` 拆成独立工程（例如为了隔离原生依赖），
  再把 `Renderer/` 目录的测试迁出。

## D3：Spike 0c 与音频后端实现延后

- **计划要求**：Phase 2 完成 Spike 0a / 0b / 0c 与音频后端实现。
- **实际**：**Spike 0a、0b 与 Spike 1（接口 + 时序状态机的四条行为验收）均已完成**；
  Spike 0c 与音频后端选型待做。
- **理由**：
  - **0c**（跨平台渲染器验证）需 macOS/Linux 实机，或由 uPlRender 仓库补
    `x86_64/aarch64-unknown-linux-gnu` 与 `apple-darwin` 目标；本机仅有 Windows `.dll`，
    属外部依赖，无法自行推进。在该项完成前，渲染器在非 Windows 的字体回退与
    `AppWindow` 的跨平台行为在两份 ADR 中均标注为**未验证**。
  - **音频后端选型**需在 `LibVLCSharp` / `NAudio` / `FFmpeg.AutoGen` 之间定夺，
    属需要决策的事项；接口（`IAudioBackend`）与状态机已先行落地，选型不阻塞其余工作。

## D4：`IRenderer` 抽象暂以 `UplRenderContext` 承担

- **计划要求**：定义 `IRenderer`（`Configure` / `SetUst` / `SetLrc` / `RenderToBuffer` /
  `BeginExport` / `RenderFrame` / `EndExport`）+ `ManagedRenderer` 测试替身。
- **实际**：直接实现 `Interop/UplRenderContext`（窄接口 + 真实集成测试）。
- **理由**：在只有一种实现、且真实 DLL 已可在本地与 CI 加载的前提下，提前引入接口会增加
  一层无消费方的抽象（Speculative Generality）。真正需要抽象的信号是**出现第二种实现或
  需要无原生件的测试路径**——目前通过「真实集成测试 + 缺件时明确失败」已覆盖。
- **解除条件**：Phase 4 接播放器时若发现需要在无原生 DLL 环境跑时序集成测试，
  再抽 `IRenderer` 并补 `ManagedRenderer`。

## D5：组合根命名为 `AppServices`（而非 1.1.x 的 `AppContext`）

- **计划要求**：移植 1.1.x `ustplayer/context.py` 的 `AppContext`。
- **实际**：类型名为 `AppServices`（`ustPlayer/AppServices.cs`，根命名空间 `UstPlayer`）。
- **理由**：.NET 已有 `System.AppContext`。同名类型会在 `UstPlayer.*` 命名空间内**静默遮蔽**
  BCL 类型——落地时立即触发：`LayeringTests` 与 `TranslatorTests` 里的
  `AppContext.BaseDirectory` 编译失败（`CS0117: "AppContext"未包含"BaseDirectory"的定义`），
  报错指向本类而非根因。改名后这类冲突不再可能发生。
- **影响**：无功能影响，仅命名。保留文档注释说明与 1.1.x 的对应关系，便于对照移植。

## D6：Phase 4 增加临时 GUI 入口与 `--play` 命令行入口

- **计划要求**：Phase 4 完成播放器与视频导出；UI 外壳属 Phase 5。
- **实际**：Phase 4 期间即加入两个入口——主窗口上的临时「打开 UST 并播放」按钮，
  以及 `ustPlayer.exe --play <UST 路径>`（跳过主窗口直接全屏播放）。
- **理由**：
  - **没有入口就没有端到端**。播放链路（设置 → 参数 → 渲染 → 窗口）在 Phase 4 已全部就位，
    但没有任何东西能触发它，等同于「无法验证的完成」。
  - **`--play` 是唯一能真实验证播放窗口的手段**。播放窗口继承 `AppWindow`，
    在 headless 平台构造即崩（见 ADR 0002），单元测试无法覆盖
    `PlayerWindow.Show` 这条路径；`build/verify-player-launch.ps1` 因此用真实进程 + 日志标记验证。
    该项已立即体现价值：它暴露了「时间轴零点未锚定」——真实时钟以系统启动为零点，
    导致第一帧就判定播完、播放瞬间结束；而假时钟从 0 开始，恰好等价于「已锚定」，
    所有单元测试因此全绿（修复见 `PlaybackSession.Advance` 的兜底锚定与两个回归测试）。
- **处理**：主窗口的临时内容标注为 Phase 5 会整体替换（届时播放入口迁到对应页面，
  门面 `PlayerLauncher` 不变）。`--play` 与两个验证脚本保留为长期资产。
- **不做**：不实现「裸路径参数」（1.1.x 允许把 `.uplr`/`.uprd` 路径直接作为首个参数）——
  那需要「导入工程 → 触发设置信号 → UI 同步」整套流程，
  半实现会让人误以为工程导入已可用，故只认显式开关 `--play`。

