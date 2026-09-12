# build/ — 构建与验证脚本

## 脚本清单

| 脚本 | 用途 | 何时运行 |
|---|---|---|
| `verify-app-launch.ps1` | 以真实进程启动 `ustPlayer.exe`，验证窗口能正常创建 | 本地改动窗口/主题后；CI 的 Windows 作业 |
| `verify-player-launch.ps1` | 以真实进程跑一遍**播放链路**（`--play` + 日志标记校验） | 本地改动播放器/时序/渲染后；CI 的 Windows 作业 |
| `sync-native-assets.ps1` | 把渲染器原生库同步到各工程的 `renderer/` 目录 | 本地开发（由测试工程的 MSBuild 目标自动调用一次） |
| `fetch-ffmpeg.ps1` | 下载 `ffmpeg`/`ffprobe` 到各工程的 `ffmpeg/` 目录 | 本地开发首次；CI 的 Windows 作业 |
| `publish.ps1` | 打成可分发产物（zip + SHA256），并**校验产物完整性** | 本地出包验证；Phase 6 的发布流程 |
| `extract-release-notes.ps1` | 从 CHANGELOG 提取某版本小节并附 SHA256 校验表 | 发版时（CI 的 release 作业） |

## 约定

### 1. 脚本文件必须带 UTF-8 BOM

Windows PowerShell 5.1 在读取**不带 BOM** 的 `.ps1` 时会按系统 ANSI 代码页（中文系统为 GBK）
解码，导致脚本里的中文注释与字符串被读坏、进而报出
`The string is missing the terminator` 之类的语法错误。

因此本目录下的 `.ps1` 文件统一保存为 **UTF-8 with BOM**。新增或修改脚本后请确认：

```powershell
$path = 'build/your-script.ps1'
$text = [System.IO.File]::ReadAllText($path, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($true))
```

> **易踩的坑**：多数编辑器与「写入文件」类工具在保存时会**丢掉 BOM**（本仓库已因此
> 坏过一次 `verify-app-launch.ps1`：PS 5.1 按 GBK 解码中文注释后报
> `The string is missing the terminator`；写 `verify-player-launch.ps1` 时又踩了两次）。
> 改完脚本务必跑一次语法检查：
>
> ```powershell
> $errors = $null
> [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $path).Path, [ref]$null, [ref]$errors)
> if ($errors) { $errors | ForEach-Object { $_.Message } } else { 'ok' }
> ```
>
> 另外有测试守着这一点：`ustPlayer.Tests/BuildScriptsTests.cs` 的
> `构建脚本必须带_UTF8_BOM` 会检查本目录下每个 `.ps1`。

### 2. 兼容 PowerShell 5.1 与 7+

CI（GitHub Actions）使用 `pwsh`（PowerShell 7），本地可能只有 `powershell.exe`（5.1）。
脚本需避免仅 7+ 支持的语法（如 `??`、`?.` 空合并运算符）。
`$IsWindows` / `$IsMacOS` / `$IsLinux` 是 7+ 的自动变量，5.1 下为 `$null`，
因此平台判断要带兜底条件（例如 `$env:OS -eq 'Windows_NT' -or $IsWindows`）。

### 3. 不要写死个人机器路径

`sync-native-assets.ps1` 通过参数与环境变量定位渲染器
（`-RendererPath` / `USTPLAYER_RENDERER_PATH` / `UPLRENDER_RELEASE_DIR`），
不得把 `E:\code\...` 之类的个人路径写进脚本。

## 为什么需要 `verify-app-launch.ps1`

FluentAvalonia 的 `AppWindow` 构造函数会无条件创建
`FluentAvalonia.UI.Windowing.Win32WindowManager`，在 Avalonia 的 **headless** 平台上会因
重复注册 Win32 属性而抛异常：

```
System.ArgumentException: An item with the same key has already been added.
    Key: FluentAvalonia.Interop.Win32.HWND
```

该问题只影响 headless 单元测试，不影响真实桌面运行（详见
`docs/adr-0002-window-chrome.md`）。因此窗口的实例化与显示**无法**用 headless 单测覆盖，
只能以真实进程启动来验证。脚本退出码：**0 = 启动成功、1 = 启动失败**。

## 为什么还需要 `verify-player-launch.ps1`

播放窗口同样是 `AppWindow`，所以 `PlayerWindow.Show` 这条路径也无法用单测覆盖。
本脚本用 `--play <临时 UST>` 真跑一遍，并要求日志里出现全部标记：

| 标记 | 说明 |
|---|---|
| `已进入直接播放模式` | UST 解析成功且走的是播放模式 |
| `播放器帧合成器就绪` | 渲染器已被配置（原生库可用） |
| `播放器已启动` | `PlayerWindow.Show` 真的把窗口显示出来了 |
| `首帧已渲染` | 渲染器确实出了帧并拷进了位图 |

并且要求**没有** ERROR 级日志、且**播放仍在进行中**。后两条是必需的，不是锦上添花：

- 帧渲染失败只停掉帧循环、窗口仍然开着 → 前四条标记依然齐全；
- 时间轴零点未锚定时第一帧就判定播完、窗口 1 秒内自动关闭 → 前四条标记**同样**齐全。

这个脚本上线即体现价值：它当场暴露了「时间轴未锚定」这个真实 bug——真实时钟以系统启动为
零点，而假时钟从 0 开始，恰好等价于「已锚定」，因此单元测试全绿而实际播放瞬间结束。
临时 UST 有约 20 秒内容、只观察数秒，所以「此刻仍在播放」本身就是有效断言。

## `sync-native-assets.ps1` 的定位

它服务**本地开发**：渲染器实体不入库（见根 `.gitignore`），本地构建时由
`ustPlayer.Tests` 的 `SyncNativeAssets` 目标调用（带 `Inputs`/`Outputs`，已同步过即跳过）。
找不到渲染器时只输出指引并以 0 退出——纯逻辑测试不应因缺原生件而红。

**打包分发不走这个脚本**：发布产物里的 `renderer/` 与 `ffmpeg/` 由 CI 在打包阶段放入
（CI 检出并编译 uPlRender、下载对应平台的 FFmpeg）。两条来源互不冲突：
脚本只管本地开发就位，CI 只管发布产物就位。

## `fetch-ffmpeg.ps1` 的定位

**视频导出必须有 ffmpeg**——不只是混入伴奏才需要。uPlRender 的编码器**只从 `PATH`
查找 ffmpeg**（它不认程序目录），所以 `VideoExporter` 会在 `up_begin_export` 前把
`<程序目录>/ffmpeg` 临时加进 `PATH`（`Video/BundledFfmpegPathScope.cs`）。
没有任何 ffmpeg 时的实际报错：

```
up_begin_export 失败：编码失败（ffmpeg init failed: ffmpeg executable not found in PATH）
```

因此本脚本把 `ffmpeg`/`ffprobe` 下载到各工程的 `ffmpeg/` 目录，再由工程里的
`<None Include="ffmpeg\**\*" CopyToOutputDirectory="PreserveNewest" />` 复制到输出目录——
与 `renderer/` 完全一致的做法。`ffprobe` 只在「混入伴奏 / 探测音频时长」时用到。

- 自动下载仅支持 Windows（`-Url` 可用环境变量 `USTPLAYER_FFMPEG_URL` 换成镜像）。
  macOS / Linux 请用系统包管理器安装后 `-SourceDirectory` 指过去。
- 已就位时直接跳过；`-Force` 强制重下。
- **不要在 MSBuild 里自动调用它**：下载约 106 MB，构建时静默联网不可接受。
  渲染器（2.8 MB，本地已常有副本）可以自动同步，ffmpeg 不行。

## `publish.ps1` 的定位

把程序打成可分发产物（`artifacts/ustPlayer-<RID>/`，`-SkipArchive` 可只出目录），
并**校验产物完整性**——这是它存在的主要理由，而不是复制：

「资源没打进产物」是本仓库反复踩过的一类坑（翻译 `.ts`、`ERcode.txt` / `Terms.txt`
都曾只存在于仓库里，运行时静默失效、不报错）。因此脚本会逐个检查产物里该有的东西：

- **必须有**（缺任一即失败）：`ustPlayer.exe`、`i18n/ustplayer_*.ts`（三份）、
  `ERcode.txt`、`Terms.txt`、`LICENSE`
- **Windows 上必须**：`renderer/ustplayer_renderer.dll`、`ffmpeg/ffmpeg.exe`、`ffmpeg/ffprobe.exe`
- **非 Windows 只提示不失败**：渲染器目前只有 Windows 产物（见 `docs/plan-deviations.md` D3），
  硬失败会让 macOS / Linux 永远出不了包

用法：

```powershell
pwsh -File build/publish.ps1                            # win-x64 自包含
pwsh -File build/publish.ps1 -RuntimeIdentifier linux-x64
pwsh -File build/publish.ps1 -FrameworkDependent        # 依赖框架，体积小
pwsh -File build/publish.ps1 -Version 2.0.0-beta1       # 覆盖程序集版本（v 前缀会被剥掉）
```

> 用 `-FrameworkDependent` 这种**反向开关**，而不是 `-SelfContained:$false`：
> `powershell.exe/pwsh -File` 传参时不解析 `-X:$false`，会当成字符串再转 bool 而报错（实测踩到）。
> 同理 `-Version` 会剥掉 `v` 前缀——MSBuild 的 `Version` 不接受 `v2.0.0`。

已实测：win-x64 自包含产物 **305 MB**（其中 ffmpeg 约 196 MB，依赖框架则约 229 MB），
产物内 `--play` 能加载渲染器、渲染出首帧并持续播放——即打包布局本身是可用；
`-Version v9.9.9-test` 会写成程序集 `9.9.9.0`。

## `extract-release-notes.ps1` 的定位

发版时把 `CHANGELOG.md` 里对应版本的小节抽出来当 Release 说明，并在末尾附上所有发布附件的
SHA256 校验表（沿用 1.1.x CI 的行为）。

版本匹配规则（与 1.1.x 一致）：`v` 前缀可省略、连字符与空格互通、大小写不敏感；
只认**一级**标题 `# 1.1.0 Beta 2`，顶部的 `## Unreleased` 不会被当作版本小节。

**找不到对应小节时以退出码 1 失败**，并列出可用小节——这是刻意的：
发版时静默发布占位内容会让用户看不到真实更新说明（1.1.x 也是这么做的）。

```powershell
pwsh -File build/extract-release-notes.ps1 -Tag v1.1.0-beta-2
pwsh -File build/extract-release-notes.ps1 -Tag 2.0.0 -Artifacts artifacts/*.zip -OutputFile notes.md
```

已实测：`v1.1.0-beta-2` 与 `v1.1.0-Beta-2` 都能匹配 `# 1.1.0 Beta 2`；`9.9.9` 以退出码 1 失败。

> 实现上有一处必须注意：`-Artifacts` 里的通配符要**先展开成具体文件**再取哈希。
> `Test-Path` / `Get-FileHash` 都接受通配符，直接喂进去会在一个「路径」上返回多个哈希、
> 被拼成一行乱码，整个校验表失效（实测踩到过，已修）。

