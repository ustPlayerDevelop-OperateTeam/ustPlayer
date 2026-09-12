# build/ — 构建与验证脚本

## 脚本清单

| 脚本 | 用途 | 何时运行 |
|---|---|---|
| `verify-app-launch.ps1` | 以真实进程启动 `ustPlayer.exe`，验证窗口能正常创建 | 本地改动窗口/主题后；CI 的 Windows 作业 |
| `sync-native-assets.ps1` | 把渲染器原生库同步到各工程的 `renderer/` 目录 | 本地开发（由测试工程的 MSBuild 目标自动调用一次） |

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
> `The string is missing the terminator`）。改完脚本务必跑一次语法检查：
>
> ```powershell
> $errors = $null
> [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $path).Path, [ref]$null, [ref]$errors)
> if ($errors) { $errors | ForEach-Object { $_.Message } } else { 'ok' }
> ```

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

## `sync-native-assets.ps1` 的定位

它服务**本地开发**：渲染器实体不入库（见根 `.gitignore`），本地构建时由
`ustPlayer.Tests` 的 `SyncNativeAssets` 目标调用（带 `Inputs`/`Outputs`，已同步过即跳过）。
找不到渲染器时只输出指引并以 0 退出——纯逻辑测试不应因缺原生件而红。

**打包分发不走这个脚本**：发布产物里的 `renderer/` 与 `ffmpeg/` 由 CI 在打包阶段放入
（CI 检出并编译 uPlRender、下载对应平台的 FFmpeg）。两条来源互不冲突：
脚本只管本地开发就位，CI 只管发布产物就位。
