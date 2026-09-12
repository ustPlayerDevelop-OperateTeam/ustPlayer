# sync-native-assets.ps1 — 把渲染器原生库同步到各工程目录
#
# 背景：ustplayer_renderer.{dll,dylib,so} 不入库（见根 .gitignore），但运行时需要放在
# 程序目录下（或其 renderer/ 子目录）。本脚本把已构建好的渲染器复制到：
#   · ustPlayer.Desktop/renderer/   —— 随构建输出，供程序加载
#   · ustPlayer.Tests/renderer/     —— 供集成测试加载
#
# 渲染器来源（按优先级）：
#   1) -RendererPath 参数（文件或所在目录）
#   2) 环境变量 USTPLAYER_RENDERER_PATH
#   3) 仓库内已同步过的位置（pysourcecode/renderer/、ustPlayer.Desktop/renderer/）
#   4) 环境变量 UPLRENDER_RELEASE_DIR 指向的目录（本机开发常用）
#
# 说明：**不处理 ffmpeg**——内置 FFmpeg 由 CI 在打包阶段放入产物，本地开发按需手工放置
# （见 ustPlayer.Desktop/ffmpeg/README.md）。
#
# 用法：
#   powershell -File build/sync-native-assets.ps1
#   powershell -File build/sync-native-assets.ps1 -RendererPath <路径>
#   $env:UPLRENDER_RELEASE_DIR='<uPlRender>/target/release'; powershell -File build/sync-native-assets.ps1
#
# 注意：本文件必须保存为 UTF-8 with BOM（Windows PowerShell 5.1 会按 GBK 读无 BOM 脚本）。

[CmdletBinding()]
param(
    [string]$RendererPath,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# 各平台的渲染器文件名（与 UplRenderLoader 一致）
if ($env:OS -eq 'Windows_NT' -or $IsWindows) {
    $fileName = 'ustplayer_renderer.dll'
}
elseif ($IsMacOS) {
    $fileName = 'libustplayer_renderer.dylib'
}
else {
    $fileName = 'libustplayer_renderer.so'
}

$candidates = @()

if ($RendererPath) {
    if (Test-Path $RendererPath -PathType Container) {
        $candidates += Join-Path $RendererPath $fileName
    }
    else {
        $candidates += $RendererPath
    }
}

if ($env:USTPLAYER_RENDERER_PATH) { $candidates += $env:USTPLAYER_RENDERER_PATH }

# 仓库内已同步过的位置
$candidates += Join-Path $repoRoot "pysourcecode\renderer\$fileName"
$candidates += Join-Path $repoRoot "ustPlayer.Desktop\renderer\$fileName"

# 本机 uPlRender 构建目录（经环境变量指定，不写死个人路径）
if ($env:UPLRENDER_RELEASE_DIR) {
    $candidates += Join-Path $env:UPLRENDER_RELEASE_DIR $fileName
}

$source = $candidates | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) } | Select-Object -First 1

if (-not $source) {
    Write-Host "未找到渲染器（$fileName）。"
    Write-Host '视频导出将不可用；播放器仍可跑纯可视化计时。'
    Write-Host ''
    Write-Host '可用的指定方式（任选其一）：'
    Write-Host '  -RendererPath <渲染器文件或所在目录>'
    Write-Host '  $env:USTPLAYER_RENDERER_PATH = <渲染器文件路径>'
    Write-Host '  $env:UPLRENDER_RELEASE_DIR   = <uPlRender 的 target/release 目录>'
    Write-Host '也可从 GitHub Release 的 Windows 包中取出 renderer/ 目录手工放置。'
    # 刻意不作为构建失败：纯逻辑测试不应因缺少原生件而红
    exit 0
}

$hash = (Get-FileHash $source -Algorithm SHA256).Hash
Write-Host "渲染器来源：$source"
Write-Host "SHA256    ：$hash"

$targets = @(
    (Join-Path $repoRoot "ustPlayer.Desktop\renderer\$fileName")
    (Join-Path $repoRoot "ustPlayer.Tests\renderer\$fileName")
)

foreach ($target in $targets) {
    $dir = Split-Path -Parent $target
    if (-not (Test-Path $dir)) {
        if ($WhatIfOnly) {
            Write-Host "[WhatIf] 将创建目录 $dir"
        }
        else {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
        }
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
