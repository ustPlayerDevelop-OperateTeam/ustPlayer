# ADR 0001：播放器画面来源——渲染器实时出帧 vs Avalonia 自绘

- 状态：**Spike 0a 通过，方案已采纳**（Spike 0b 端到端复核待做）
- 日期：Phase 2
- 相关：`docs/adr-0002-window-chrome.md`

## 背景

1.1.x 有两套绘制实现：

| 场景 | 实现 | 代码 |
|---|---|---|
| 播放器全屏预览 | Qt `QPainter` 自绘（文字、音高折线、LRC…） | `core/player.py`（806 行） |
| 导出 MP4 | Rust 渲染器（tiny-skia + cosmic-text）逐帧 | `uPlRender` + `core/renderer_ffi.py` |

迁移到 Avalonia 后，播放器画面有两条路：

- **A. 渲染器实时出帧**：`up_render_to_buffer` → RGBA 字节 → `WriteableBitmap` → `Image` 控件；
- **B. Avalonia 自绘**：把 `QPainter` 那段直译成 `Control.Render` + `DrawingContext`。

方案 A 若成立，收益是双重的：

1. **跨平台的字体/文本排版问题整体消失**——排版在 Rust 渲染器内，各平台同一套代码，
   不必处理 Avalonia 的 `FormattedText` 不可变、字体族解析、CJK 回退、`drawPolyline` 几何重建；
2. **「预览所见 = 导出所得」从架构上成立**——播放与导出共用同一渲染路径。

关键证据（`API_Docs.md`）：`up_render_to_buffer` 与 `up_render_frame` **共用同一套显示状态
计算**，且字号为「点值 × 96/72 像素换算（与播放器 QPainter 一致），随分辨率等比缩放」。
接口 `up_render_to_buffer` 在 1.1.x 中已绑定却**从未被调用**，属于现成但闲置的能力。

风险：软件渲染单帧能否撑住 60fps。这就是 Spike 0 要回答的问题。

## 实测（Spike 0a）

探针：`pysourcecode/tools/probe_render_buffer.py`（纯标准库 + ctypes）。

- 渲染器：`E:\code\uPlRender\target\release\ustplayer_renderer.dll`
  （SHA256 `2A4F38A4F59EC5DD6890FEB861402EA10A26440EE21EBBDE8B24E6F9D314D88B`，
  与 1.1.0 Beta 2 发布包内 `renderer/ustplayer_renderer.dll` **哈希一致**）
- 合成工程：500 音符、每音符带 5 点音高曲线、每 8 个音符含一个休止符（覆盖 silent 分支）、
  三行 LRC（含一行多时间戳与无毫秒写法）
- 采样：每档 600 帧 + 30 帧预热；宿主 ctypes 调用开销实测 0.0005 ms（可忽略）

| 分辨率 | 冷启动首帧 | mean | median | **P95** | P99 | max | 可维持帧率 | 判定 |
|---|---|---|---|---|---|---|---|---|
| **1920×1080** | 7.56 ms | 4.88 ms | 4.87 ms | **5.96 ms** | 6.68 ms | 7.75 ms | **205 fps** | ✅ 通过 |
| 3840×2160 | 21.42 ms | 18.69 ms | 18.80 ms | 22.30 ms | 23.39 ms | 24.03 ms | 53.5 fps | ⚠️ 不足 60 |

判定线为 1080p P95 ≤ 8 ms（60fps 预算 16.67 ms 的一半）：

- **1080p：5.96 ms，用掉 36% 预算，余量充足** → 方案 A 成立。
- **4K：22.30 ms，超过 16.67 ms 预算** → 4K 实时 60fps 不成立。

4K 的结果符合预期：tiny-skia 是纯 CPU 渲染，帧面积是 1080p 的 4 倍。
**但这不影响导出**——导出是离线流程（`up_render_frame` 逐帧写编码器），慢只影响耗时，
不影响正确性；受影响的只有「4K 全屏实时预览」。

## 实测（Spike 0b：Avalonia 侧端到端）

探针：`ustPlayer.Tests/Renderer/RenderBufferBlitTests.cs`（headless Avalonia，真实加载 DLL）。

流程与宿主播放器一致：`up_render_to_buffer` → 复用同一份 `byte[]` → `WriteableBitmap.Lock()`
→ 整块拷入帧缓冲 → `Dispose` 提交。1920×1080、240 帧采样（20 帧预热）：

| 阶段 | mean | P95 |
|---|---|---|
| 渲染（原生 CPU） | 5.09 ms | 6.00 ms |
| 上传（`Lock()` + 8.3 MB 拷贝） | 2.43 ms | 2.81 ms |
| **合计** | **7.52 ms** | **8.61 ms** |
| 60fps 预算 | 16.67 ms | 用掉约 **52%** |

要点：

1. **格式可直接对接**：渲染器输出 RGBA8888 预乘，与 `PixelFormat.Rgba8888` +
   `AlphaFormat.Premul` 完全匹配，**无需逐像素转换**。
2. **上传不是免费的**：8.3 MB 整块拷贝稳定占约 2.4 ms（单帧成本的 32%）。
   当前可接受；若将来成为瓶颈，可改为渲染线程与 UI 线程之间双缓冲 + 只提交最新帧。
3. P95 8.61 ms 仍在预算内，余量约 48%，足以容纳 Avalonia 的组合与呈现开销。

## 决定

**采纳方案 A**：播放器以渲染器出帧显示。

1. `IRenderer` 抽象提供 `RenderToBuffer(elapsed, width, height)`；`UplRenderInterop` 实现它，
   另有 `ManagedRenderer` 测试替身，使逻辑层可在无原生 DLL 环境下测试。
2. 播放窗口用 `Image` + `WriteableBitmap`：`Lock()` 取 `Span<byte>` 直接拷贝渲染器输出，
   **复用同一个位图与缓冲**，避免每帧分配。
3. **播放渲染尺寸与窗口尺寸解耦**：按窗口实际尺寸做 clamp，默认上限 1920×1080。
   4K 屏上以 1080p 渲染后由 Avalonia 放大显示（视觉差异在全屏视频场景可接受），
   以此保证任何屏幕下都能 60fps。
4. 导出路径不变，仍走 `up_begin_export` / `up_render_frame` / `up_end_export`。
5. 若实测发现 4K 屏放大观感不可接受，再补一条「按屏幕分辨率渲染 + 满帧则丢帧」的策略；
   帧率自适应逻辑收敛在 `Timing/PlaybackSession`。

## 后果

- `player.py` 那 806 行的迁移量从「QPainter → DrawingContext 全量直译 + 字体系统重做」
  降级为「一个 blit 循环 + 保留时序/降级状态机」，预计省下 3–5 周。
- 跨平台不再需要解决 Avalonia 侧的字体与文本排版（含 CJK 回退）；
  macOS/Linux 上的字体回退由渲染器负责（Spike 0c 需在真实平台复核）。
- 「预览 = 导出」由同一实现保证，消除了 1.1.x 里两套绘制人工对齐的隐患。
- 4K 实时预览帧率不足 60，已由「渲染尺寸 clamp」规避；该取舍记录在此 ADR。

## Spike 状态

- **Spike 0a（原生单帧性能）**：✅ 通过（1080p P95 5.96 ms）。
- **Spike 0b（Avalonia 端到端）**：✅ 通过（渲染 + 上传 P95 8.61 ms，格式直接对接）。
  原计划「若 0b 失败则回退方案 B（自绘，额外 2–4 周）」的退路**不再需要**。
- **Spike 0c（跨平台）**：⏳ 待做——需在 macOS / Linux 上加载对应 `.dylib` / `.so`，
  验证 P/Invoke 解析、字体回退与 buffer 输出。**前置依赖**：uPlRender 需补
  `x86_64/aarch64-unknown-linux-gnu` 与 `apple-darwin` 目标，或提供对应平台实机。
  在该项完成前，`AppWindow` 的跨平台行为（见 ADR 0002）与渲染器字体回退均属**未验证**。
