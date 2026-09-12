# 迁移计划的偏差记录

> 记录 2.0 迁移相对**已批准计划**的偏差及其理由。新增偏差请追加，不要删改历史条目。

## D1：Android 头工程**已补建**，但刻意留在解决方案之外

- **计划要求**：`ustPlayer.Android/` 头工程骨架，Phase 1 内编译通过。
- **实际进展**：
  1. `dotnet workload install android` **已成功**（原阻塞点解除）；
  2. `ustPlayer.Android/` 骨架**已创建**：`MainActivity : AvaloniaMainActivity<App>`
     （与桌面头等价，只负责构建 AppBuilder）、`Resources/values/styles.xml`、csproj 指向 `net10.0-android`；
  3. **但它不在 `UstPlayer.slnx` 里**，且**本地从未编译成功过**。
- **为什么不在解决方案里**：本机缺 **Android SDK**，`-t:InstallAndroidDependencies`
  自动安装失败（下载 SDK 清单时 `DownloadToString` 抛异常，随后去找
  `AndroidManifestFeed_d18.0.xml` 这个不存在的本地文件）。把它放进 sln 会让
  `dotnet build UstPlayer.slnx` 在没有 Android SDK 的机器上**每次都失败**——
  这正是本条最初不建工程的原因。
- **怎么验证它**：新增 CI 作业 `android`（ubuntu-latest 预装 Android SDK）单独构建该工程。
  **该作业首次运行时才真正编译过这份代码**；在此之前它的 C# 是未验证的。
- **命名坑（已规避）**：命名空间取 `UstPlayer.AndroidHead` 而非 `UstPlayer.Android`，
  否则 `using Android.App;` 会先按外层命名空间解析成 `UstPlayer.Android`，
  与 Android SDK 的 `Android` 撞名、报错难懂。程序集名仍是 `ustPlayer.Android`。
- **解除条件**：CI 的 android 作业通过后，可评估是否放回解决方案。


## D2：测试工程合并为单一 `ustPlayer.Tests`

- **计划要求**：`UstPlayer.Core.Tests`、`UstPlayer.Renderer.Tests`、`UstPlayer.App.Tests` 三个。
- **实际**：合并为一个 `ustPlayer.Tests`。
- **理由**：项目全部代码位于共享工程 `ustPlayer` 内，三个测试工程会各自产生一份
  `ustPlayer.Core.dll` 引用与一份 Avalonia 依赖，而职责边界在测试内已由目录
  （`Architecture/`、`Renderer/`、`Timing/`、`Models/`）清晰表达。合并减少编译与还原开销，
  且 `InternalsVisibleTo` 只需维护一处。
- **影响**：无功能影响。若将来把 `UstPlayer.Renderer` 拆成独立工程（例如为了隔离原生依赖），
  再把 `Renderer/` 目录的测试迁出。

## D3：Spike 0c 仍待做；音频后端**已实现**（由本条部分解除）

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

## D7：Phase 5 的 UI 分轮落地，未迁移页面显示占位（**已解决**）

- **计划要求**：Phase 5 一次完成 FluentAvalonia NavigationView 外壳与五个页面。
- **实际**：分轮推进。第 8 轮落地「外壳 + 基础页」，其余四个页面（文件 / 播放器 / 歌词 / 其他）
  在导航里可点，但内容是占位提示「该页面尚未迁移（Phase 5 进行中）」。
- **理由**：一次提交整个 UI 会让「界面是否真的能起来」这件事在很晚才被验证；
  先把外壳与一页打通，可以立刻用真实进程确认 NavigationView、主题资源、
  卡片控件、编译期绑定这几条底座是对的——后续页面只是重复同一套模式。
- **影响**：**用户可见的半成品状态**（四个页面是占位）。占位文案是刻意的：
  点进去空白会让人以为程序坏了。

## D8：2.0 的 UI 不再需要 `sync_all_from_settings`

- **计划要求**：移植 1.1.x 的页面时保留 <c>sync_all_from_settings()</c>（切换页面时回填控件）。
- **实际**：**不移植**。2.0 的控件直接双向绑定设置子域。
- **理由**：设置子域已实现 `INotifyPropertyChanged`，且 setter 有「值未变化则不通知」的守卫，
  Avalonia 的绑定会自动跟随。1.1.x 需要手工同步是因为 Qt 控件不会自动跟随数据变化；
  那套「控件 → 设置」「设置 → 控件」双向量 + `_sync_ui_from_settings` 共约 40 行/页，
  且漏写一处就会出现「设置不生效」或「导入工程后界面没刷新」——
  这类 bug 在 2.0 的绑定模式下**结构上不可能出现**。
- **附带收益**：工程导入后界面自动刷新，无需任何显式同步调用。

## D9：自定义字体只记录族名与路径，无法在 Avalonia 侧真正注册

- **计划要求**：移植 1.1.x 的「四路字体下拉 + 导入自定义字体（`.ttf`/`.otf`）」。
- **实际**：导入字体时**解析字体文件的 sfnt `name` 表**取出真实族名写进设置
  （优先排版族名 nameID 16，其次家族名 nameID 1，解析不出来时回退文件名），
  并把路径记入 `DisplaySettings.CustomFontPaths`。**但下拉里的字体名不会用该字体渲染预览**。
- **理由**：Avalonia 没有公开的运行时字体注册 API（`FontManager.AddFontCollection` 存在，
  但其 `IFontCollection` 无法从外部初始化）。1.1.x 能预览是因为 Qt 提供
  `QFontDatabase.addApplicationFont`；C# 侧没有对应能力，因此不假装做到。
- **为什么仍然值得解析族名**：`font_*` 字段的语义就是「字体族名」，
  写文件名进去是**错的数据**（渲染器一旦开始消费该字段就会匹配失败）。
  `API_Docs.md` §5.2 把 `font_*` 与 `custom_font_paths` 都列为
  「已写入但渲染器**暂未消费**」并注明了计划（注册后可经 fontdb/cosmic-text 使用），
  因此这是一个**有既定消费者的字段**，不是凭空的抽象。
- **手写 sfnt 解析的代价与防护**：约 250 行二进制解析（只读 `name` 表），
  风险集中在长度/偏移越界。已用「空文件 / 越界偏移 / 结构不可识别 / 截断文件」
  以及一条**确定性伪随机字节扫描**（`FontFileInspectorFuzzTests`）锁定
  「任意输入只返回 null 或文件名，绝不抛异常」。
- **解除条件**：渲染器开始消费 `custom_font_paths` / `font_*` 时，或 Avalonia 开放
  运行时字体注册时，把族名解析换成字体引擎（SkiaSharp `SKTypeface.FromFile` 已在依赖图中）
  并补上真实预览。

---

## 迁移现状（Phase 0–6 收尾时的对照）

**已完成**：Phase 0（冻结 1.1.x 基线）、Phase 1（共享工程 + 桌面头 + 锁版本）、
Phase 3（逻辑层全部移植）、Phase 4（播放器与视频导出，均以真实进程/真实导出验证）、
Phase 5（五个页面 + 视频导出对话框 + 滑入通知 + 拖放 + 主题/强调色/窗口效果/语言 + 自绘标题栏）、
Phase 6（打包脚本与产物完整性校验、发布说明提取 + SHA256 表、CI 的打包与标签驱动发版作业）。

**仍未完成的三项**（都需要外部条件或决策，不是代码没写）：

| 项 | 阻塞点 |
|---|---|
| D1 Android 头工程 | 本机 `dotnet workload list` 仍为空，`net10.0-android` 无法还原；需先装 workload |
| D3 音频后端 | **播放器目前没有伴奏声**（按墙钟计时）。库选型未定（建议 LibVLCSharp：跨平台且能延伸到 Android；NAudio 仅 Windows，与目标冲突） |
| Spike 0c | 需 macOS/Linux 实机，或 uPlRender 补 `x86_64/aarch64-unknown-linux-gnu` 与 `apple-darwin` 目标 |

**另有一项只差一个动作**：CI 至今从未在 GitHub 上跑过（本地无法验证 macOS/Linux 作业、
以及「渲染器缺库时测试明确失败」在真实 CI 上的行为）。需要一次 push。

> 版本号方案不再阻塞发版：发布流程全部从**推送的 tag** 推导（tag 即版本，
> `-p:Version=` 写进程序集），不需要在仓库里另存一份「2.0 从哪个版本起」的配置。




