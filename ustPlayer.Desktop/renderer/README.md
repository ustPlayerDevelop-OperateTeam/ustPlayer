# renderer/ — uPlRender 渲染器原生库

本目录存放视频渲染器 **uPlRender**（Rust cdylib）的构建产物，运行时由程序的
`UplRenderLoader` 从「程序根目录」或本 `renderer/` 子目录查找并加载。

## 期望文件名

| 平台 | 文件名 |
|---|---|
| Windows | `ustplayer_renderer.dll` |
| macOS | `libustplayer_renderer.dylib` |
| Linux | `libustplayer_renderer.so` |

## 如何获得

- **CI**：由 `.github/workflows/` 检出 [uPlRender](https://github.com/ustPlayerDevelop-OperateTeam/uPlRender)
  并 `cargo build --release`，按目标平台复制到本目录后随产物打包。
- **本地**：从 GitHub Release 的 Windows 包中解出 `renderer/ustplayer_renderer.dll`，
  或安装 Rust 工具链后本地构建。

## 缺失时的行为

程序不会崩溃：视频导出功能报错提示，播放器仍可用（走纯可视化计时）。

> 实体文件不入库（见根 `.gitignore`），本说明仅为占位。
