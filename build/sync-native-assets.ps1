# sync-native-assets.ps1 — 把原生运行件同步到构建输出目录
#
# 背景：以下两类原生文件不入库（见根 .gitignore），但运行时需要放在程序目录下：
#   · renderer/ustplayer_renderer.{dll,dylib,so} —— uPlRender 视频渲染器
#   · ffmpeg/ffmpeg(.exe)、ffprobe(.exe)         —— 混流与媒体时长探测
# 本脚本从本机已知位置查找渲染器并复制到各工程，避免每次手工拷贝。
#
# 用法：
#   pwsh -File build/sync-native-assets.ps1              # 自动查找并复制
#   pwsh -File build/sync-native-assets.ps1 -RendererPath <路径>
#   pwsh -File build/sync-native-assets.ps1 -WhatIfOnly  # 只报告，不复制
#
# 注意：本文件必须保存为 UTF-8 with BOM（Windows PowerShell 5.1 会按 GBK 读无 BOM 脚本）。

[CmdletBinding()]
param(
    [string]$RendererPath,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# 各平台渲染器文件名（与 RendererLoader / ADR 0001 一致）
$platformFileNames = @{
    Windows = 'ustplayer_renderer.dll'
    MacOS   = 'libustplayer_renderer.dylib'
    Linux   = 'libustplayer_renderer.so'
}

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $fileName = $platformFileNames.Windows
}
elseif ($IsMacOS) {
    $fileName = $platformFileNames.MacOS
}
else {
    $fileName = $platformFileNames.Linux
}

# 本机已知的候选位置（开发机常见布局）
$candidates = @(
    $RendererPath
    (Join-Path $env:USERPROFILE "Downloads\uPlRender\target\release\$fileName")
    'E:\code\uPlRender\target\release\ustplayer_renderer.dll'
    'D:\Code\ustPlayer\renderer\ustplayer_renderer.dll'
    (Join-Path $repoRoot "pysourcecode\renderer\$fileName")
    (Join-Path $repoRoot "ustPlayer.Desktop\renderer\$fileName")
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $candidates) {
    Write-Warning "未找到渲染器（$fileName）。视频导出将不可用，播放器仍可跑纯可视化计时。"
    Write-Warning "请用 -RendererPath 指定路径，或从 GitHub Release 的 Windows 包中取 renderer/ 目录。"
    exit 0
}

$source = $candidates[0]
$hash = (Get-FileHash $source -Algorithm SHA256).Hash
Write-Host "渲染器来源：$source"
Write-Host "SHA256    ：$hash"

# 目标：桌面头的 renderer/（随构建输出复制）+ 测试工程的 renderer/（集成测试用）
$targets = @(
    (Join-Path $repoRoot "ustPlayer.Desktop\renderer\$fileName")
    (Join-Path $repoRoot "ustPlayer.Tests\renderer\$fileName")
)

foreach ($target in $targets) {
    $dir = Split-Path -Parent $target
    if (-not (Test-Path $dir)) {
        if ($WhatIfOnly) { Write-Host "[WhatIf] 将创建目录 $dir" } else { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    }

    $needsCopy = $true
    if (Test-Path $target) {
        $needsCopy = (Get-FileHash $target -Algorithm SHA256).Hash -ne $hash
        if (-not $needsCopy) { Write-Host "已是最新：$target" }
    }

    if ($needsCopy) {
        if ($WhatIfOnly) {
            Write-Host "[WhatIf] 将复制到 $target"
        }
        else {
            Copy-Item -LiteralPath $source -Destination $target -Force
            Write-Host "已复制到 $target"
        }
    }
}

Write-Host '完成。'
