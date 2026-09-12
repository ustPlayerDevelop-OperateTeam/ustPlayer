# ffmpeg/ — 内置 FFmpeg 可执行文件

本目录存放随程序分发的 **FFmpeg**，程序优先使用程序目录下的内置版本，
缺失时回退到系统 `PATH`。

## 期望文件名

| 平台 | 文件 |
|---|---|
| Windows | `ffmpeg.exe`、`ffprobe.exe` |
| macOS / Linux | `ffmpeg`、`ffprobe` |

## 为什么**导出视频必须有 ffmpeg**

不只是混入伴奏才需要——**渲染器编码 MP4 本身就依赖 ffmpeg**。
uPlRender 内部用 ffmpeg 做编码，且**只从 `PATH` 查找**（它不认程序目录），
所以 `VideoExporter` 会在调用 `up_begin_export` 之前把本目录临时加进 `PATH`
（见 `Video/BundledFfmpegPathScope.cs`）。没有任何 ffmpeg 时的实际报错是：

```
up_begin_export 失败：编码失败（ffmpeg init failed: ffmpeg executable not found in PATH）
```

因此"导出无声 MP4 不需要 ffmpeg"是**错误**的旧说法（本文件此前就是这么写的）；
`ffprobe` 才是只在「混入伴奏 / 探测音频时长」时才用到。

## 如何获得

- **本地**：跑 `pwsh -File build/fetch-ffmpeg.ps1`（自动下载并把 `ffmpeg`/`ffprobe`
  放进各工程的 `ffmpeg/` 目录）。也可自行从 [ffmpeg.org](https://ffmpeg.org/)
  或 gyan.dev / BtbN 的构建包中取出后，用 `-SourceDirectory` 指定已解压目录。
- **CI**：由构建流程下载对应平台的发行包并复制到本目录后随产物打包（Phase 6）。

## 缺失时的行为

程序不会崩溃：**播放器**完全不受影响（无伴奏时按墙钟计时），
但**视频导出会失败**并给出上面那条明确报错，同时清理半成品文件
（不留打不开的 MP4，也不留指向无效视频的 `.uprd`）。

> 实体文件不入库（见根 `.gitignore`），本说明仅为占位。
