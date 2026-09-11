# ffmpeg/ — 内置 FFmpeg 可执行文件

本目录存放随程序分发的 **FFmpeg**（用于视频导出的混流与伴奏时长探测），
程序优先使用程序目录下的内置版本，缺失时回退到系统 `PATH`。

## 期望文件名

| 平台 | 文件 |
|---|---|
| Windows | `ffmpeg.exe`、`ffprobe.exe` |
| macOS / Linux | `ffmpeg`、`ffprobe` |

## 如何获得

- **CI**：由构建流程下载对应平台的 FFmpeg 发行包并复制到本目录后随产物打包。
- **本地**：从 [ffmpeg.org](https://ffmpeg.org/) 或 gyan.dev / BtbN 的构建包中取
  `ffmpeg` / `ffprobe` 放入本目录。

## 缺失时的行为

程序不会崩溃：视频导出仍可渲染出无声 MP4，仅在需要混入伴奏或探测音频时长时报错。

> 实体文件不入库（见根 `.gitignore`），本说明仅为占位。
