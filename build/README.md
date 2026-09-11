# build/ — 构建与验证脚本

## 脚本清单

| 脚本 | 用途 |
|---|---|
| `verify-app-launch.ps1` | 以真实进程启动 `ustPlayer.exe`，验证窗口能正常创建（见下方说明） |

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

### 2. 兼容 PowerShell 5.1 与 7+

CI（GitHub Actions）使用 `pwsh`（PowerShell 7），本地可能只有 `powershell.exe`（5.1）。
脚本避免使用仅 7+ 支持的语法（如 `??`、`?.` 空合并运算符）。

## 为什么需要 `verify-app-launch.ps1`

FluentAvalonia 的 `AppWindow` 构造函数会无条件创建
`FluentAvalonia.UI.Windowing.Win32WindowManager`，在 Avalonia 的 **headless** 平台上会因
重复注册 Win32 属性而抛异常：

```
System.ArgumentException : An item with the same key has already been added.
    Key: FluentAvalonia.Interop.Win32.HWND
```

该问题只影响 headless 单元测试，不影响真实桌面运行（详见
`docs/adr-0002-window-chrome.md`）。因此窗口的实例化与显示**无法**用 headless 单测覆盖，
只能以真实进程启动来验证——这就是本脚本存在的意义。

CI 中应在构建后运行它，作为 Phase 1 骨架的验收项之一。
