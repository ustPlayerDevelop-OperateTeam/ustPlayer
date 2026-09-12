>[!NOTE]
>
>欢迎您加入ustPlayer的dev分支开发！贡献准则请看<a href="CONTRIBUTING.md">CONTRIBUTING.md</a>！

<div align="center">

<image src="icon.png" height="90" width="90"/>

# ustPlayer

中文的、面向多样音声合成编辑器工程文件的可视化工具。

![GitHub Release](https://img.shields.io/github/v/release/ustPlayerDevelop-OperateTeam/ustPlayer?style=for-the-badge)
![GitHub All Releases](https://img.shields.io/github/downloads/ustPlayerDevelop-OperateTeam/ustPlayer/total?style=for-the-badge)
![Star](https://img.shields.io/github/stars/ustPlayerDevelop-OperateTeam/ustPlayer?style=for-the-badge)


[配布视频](https://www.bilibili.com/video/BV1YjcwzVEcX "bilibili弹幕网") | <a href="CHANGELOG.md">更新日志</a>

</div>

> [!WARNING]
> 
> 我们将不再提供 .msi 安装包，也不再打算提供 .app 格式程序。

## 构建与运行

主线版本（2.0）是 **C# / .NET 10 + Avalonia 11 + FluentAvalonia**，工作目录为仓库根目录：

```powershell
dotnet build UstPlayer.slnx -c Debug      # 构建（0 警告是硬要求）
dotnet test  UstPlayer.slnx -c Debug      # 测试
dotnet run --project ustPlayer.Desktop    # 运行

pwsh -File build/sync-native-assets.ps1   # 渲染器原生库（视频渲染必需）
pwsh -File build/fetch-ffmpeg.ps1         # 内置 FFmpeg（视频导出必需）
```

更细的约定、易踩的坑与真实进程验证方式见 [`AGENTS.md`](AGENTS.md)。

`pysourcecode/` 保留的是**已冻结的 1.1.x 实现**（Python / PySide6），
作为 2.0 的行为与格式比对基准；它的命令一律以该目录为工作目录。

## 致谢
### 资源

2.0（主线）：

- [Avalonia](https://avaloniaui.net/) 与 [FluentAvalonia](https://github.com/amwx/FluentAvalonia)
- [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp)（伴奏播放，选型理由见 [`docs/adr-0003-audio-backend.md`](docs/adr-0003-audio-backend.md)）
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [uPlRender](https://github.com/ustPlayerDevelop-OperateTeam/uPlRender)（画面与视频渲染）
- [FFmpeg](https://ffmpeg.org/)（视频编码与混流）

1.1.x（`pysourcecode/`，已冻结）：

- [PySide6](https://www.qt.io/)
- [PySide6-Fluent-Widgets](https://github.com/zhiyiYo/PyQt-Fluent-Widgets/tree/PySide6)
- [loguru](https://github.com/delgan/loguru)

### 贡献者

<a href="https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=ustPlayerDevelop-OperateTeam/ustPlayer" />
</a>

## 补充说明
**使用前请务必阅读并同意相关使用协议。**

本工具在开发过程中使用了 AI 工具进行辅助开发。
项目使用`GPL v3.0`协议开源，详情可查看：
- 程序目录 / 仓库下`LICENSE`
- 或软件内入口：`设置 > 协议与许可 > 开源协议`

## 贡献

如果你有什么好的想法/想修复Bug，请复刻（Fork）本仓库，修改完后也请不要忘记提交[拉取请求（Pull Request）](https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer/pulls)，或者简单的在[议题（Issue）](https://github.com/ustPlayerDevelop-OperateTeam/ustPlayer/issues)说说你的想法/遇到的Bug。

我们会由衷的感谢您为仓库做出贡献！

---
感谢使用，玩得开心！
