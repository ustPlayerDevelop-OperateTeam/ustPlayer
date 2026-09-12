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
