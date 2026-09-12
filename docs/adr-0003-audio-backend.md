# ADR 0003：伴奏音频后端选用 LibVLCSharp

- **状态**：已采纳（已实现并通过集成测试）
- **日期**：2026-09-12
- **相关**：`docs/plan-deviations.md` 的 D3、`ustPlayer/Platform/LibVlcAudioBackend.cs`、
  `ustPlayer/Platform/AudioBackendFactory.cs`

## 背景

播放器要按伴奏（`ProjectSettings.MusicPath`）的真实播放位置驱动时间轴，
并在音频不可用时降级为墙钟计时。1.1.x 用的是 QtMultimedia——而 **Avalonia 没有任何音频 API**，
因此 2.0 必须自己选一个后端。

已经就位的约束（选型只需要落在它们后面）：

- `Platform/IAudioBackend` 是一个**窄接口**（`Ready`/`Ended`/`Failed` 事件 +
  位置/时长/媒体阶段查询），播放器只依赖它，不关心实现；
- 时序状态机（`Timing/PlaybackSession`）**已经写好并有测试**，包含四条 1.1.x 踩坑后加的守卫
  （就绪只播一次、播完锚点只记一次、已播完优先排除、降级重锚定）。
  也就是说：**后端只需要如实汇报媒体状态**，不含任何降级判断；
- 无伴奏 / 后端不可用时按墙钟计时是**已验证的正常路径**，不是错误路径。

## 选项

| 方案 | 跨平台 | Android | 说明 |
|---|---|---|---|
| **LibVLCSharp** | Windows / macOS / Linux | **有官方包** | 自带解码与输出，`--no-video` 可只做音频 |
| NAudio | **仅 Windows** | 无 | 与「跨平台」这一硬目标直接冲突 |
| FFmpeg.AutoGen | 可跨平台 | 可 | 只解决**解码**，输出设备仍需另配（等于自己写音频引擎） |
| SDL2 / SDL_mixer | 可跨平台 | 可 | 需要额外的 native 依赖与解码器，生态不如 libvlc |

## 决定

选用 **LibVLCSharp 3.10.1**（+ 平台原生包），理由按优先级：

1. **唯一能延伸到 Android 的完整方案**——2.0 的目标明确包含「Android 架构就绪」，
   而 `VideoLAN.LibVLC.Android` 官方包已存在；
2. 解码与输出一体，不需要自己拼装音频引擎；
3. 三个桌面平台都有成熟支持，与 Avalonia 无冲突。

## 后果

**接受的成本**

- 每个平台要带原生库：Windows / macOS 走 NuGet（`VideoLAN.LibVLC.*`），
  **Linux 没有 NuGet 包**，需系统安装 libvlc（如 `apt install libvlc-dev vlc-plugin-base`）。
  因此 csproj 只在有包的平台上引用，**不硬失败**——否则 Linux 构建直接挂；
- 发布产物显著变大（win-x64 自包含 305 MB，其中 ffmpeg 约 196 MB、libvlc 也占一部分）；
- 许可为 LGPL-2.1+：本仓库是 GPL-3.0，动态链接使用没有问题。

**必须遵守的实现约束（已在代码注释里写明）**

- **绝不在 libvlc 的回调线程上回调调用方**：libvlc 事件发生在它自己的线程上，
  在回调里再调用 libvlc API（例如播放器收到 `Ready` 后立刻 `Play()`）会**死锁**。
  因此所有事件都经线程池转投出去再触发。

**验证方式**

- 后端的集成测试**真实加载原生 libvlc**（用程序生成的 1 秒静音 WAV），
  断言「可解析、时长正确、就绪/失败按约定汇报」；
- 刻意**不测「真的放出声音」**：那需要音频设备，会让测试在无声卡环境（含 CI）失败，
  而「有没有声音」本就无法断言。播放本身由真实进程验证。

## 备选方案的复核条件

若将来出现以下情况，应重新评估：

- 需要在不带系统 libvlc 的 Linux 发行版上开箱可用（当前需用户自行安装）；
- 发布体积成为主要矛盾（可考虑 `-p:SelfContained=false` 或换用更小的解码方案）；
- Android 头实际落地时发现 libvlc 的包体/启动开销不可接受。
