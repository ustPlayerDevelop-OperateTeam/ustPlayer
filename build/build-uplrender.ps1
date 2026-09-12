# build-uplrender.ps1 — 检出并编译 uPlRender 渲染器（供 CI 与本地使用）
#
# 背景：ustplayer_renderer.{dll,dylib,so} 是运行/测试的必需原生件，但**实体不入库**
#       （见根 .gitignore）。因此干净环境里必须现构建一份：
#       CI 的 windows / package / release 三个作业都依赖它。
#
# 本脚本只负责「把渲染器编出来」，**不负责分发**——
# 分发交给 build/sync-native-assets.ps1（它知道该复制到哪几个目录）。
#
# 产物位置：<工作目录>/uPlRender/target/release/<平台渲染器名>
# 脚本结束时会把该目录路径与完整文件路径写到标准输出，便于调用方取用。
#
# 用法：
#   pwsh -File build/build-uplrender.ps1
#   pwsh -File build/build-uplrender.ps1 -WorkDirectory $env:TEMP\uPlRenderWork
#
# 退出码：0 = 编译成功；1 = 失败（克隆 / 编译 / 找不到产物）。
#
# 注意：本文件必须保存为 UTF-8 with BOM（Windows PowerShell 5.1 会按 GBK 读无 BOM 脚本）。

[CmdletBinding()]
param(
    # 检出与编译的工作目录；默认放在系统临时目录下。
    [string]$WorkDirectory,

    # 渲染器仓库地址（一般不需要改）。
    [string]$RepositoryUrl = 'https://github.com/ustPlayerDevelop-OperateTeam/uPlRender.git',

    # 已存在的检出目录；给了就跳过克隆（本机开发常用）。
    [string]$SourceDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# 与 UplRenderLoader 一致的各平台产物名
if ($env:OS -eq 'Windows_NT' -or $IsWindows) {
    $fileName = 'ustplayer_renderer.dll'
}
elseif ($IsMacOS) {
    $fileName = 'libustplayer_renderer.dylib'
}
else {
    $fileName = 'libustplayer_renderer.so'
}

if (-not $WorkDirectory) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'ustplayer-uplrender'
}

$checkout = if ($SourceDirectory) { $SourceDirectory } else { Join-Path $WorkDirectory 'uPlRender' }

# ---------- 检出源码 ----------
if (Test-Path (Join-Path $checkout 'Cargo.toml')) {
    Write-Host "复用已有检出：$checkout"
}
else {
    if (Test-Path $checkout) {
        Write-Host "目录存在但不是 uPlRender 检出，先清掉：$checkout"
        Remove-Item -Recurse -Force $checkout
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $checkout) | Out-Null

    Write-Host "克隆 uPlRender：$RepositoryUrl"
    git clone --depth 1 $RepositoryUrl $checkout

    if ($LASTEXITCODE -ne 0) {
        Write-Host "克隆失败（git 退出码 $LASTEXITCODE）"
        exit 1
    }
}

# ---------- 编译 ----------
# 仓库内的 rust-toolchain.toml 会指定工具链，rustup 会自动安装/切换，
# 因此这里**不**覆盖工具链：渲染器自己钉的版本才是有依据的那个。
Write-Host '编译 uPlRender（cargo build --release）…'
cargo build --release --manifest-path (Join-Path $checkout 'Cargo.toml')

if ($LASTEXITCODE -ne 0) {
    Write-Host "编译失败（cargo 退出码 $LASTEXITCODE）"
    exit 1
}

# ---------- 定位产物 ----------
$releaseDir = Join-Path $checkout 'target/release'
$artifact = Join-Path $releaseDir $fileName

if (-not (Test-Path $artifact -PathType Leaf)) {
    # 退一步：release 目录里找任一渲染器文件（不同平台/命名差异时给出可读提示）
    $found = Get-ChildItem $releaseDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*renderer*' } |
        Select-Object -First 1

    if ($found) {
        Write-Host "未找到 $fileName，但目录里有：$($found.Name)"
        Write-Host '若产物名与预期不符，请同步更新本脚本与 UplRenderLoader。'
    }
    else {
        Write-Host "编译成功但未在 $releaseDir 找到渲染器产物"
    }

    exit 1
}

$size = (Get-Item $artifact).Length
Write-Host "渲染器已就绪：$artifact（$size 字节）"

# 供调用方取用的机器可读输出（CI 里用它设置 UPLRENDER_RELEASE_DIR）
Write-Host "UPLRENDER_RELEASE_DIR=$releaseDir"
Write-Host "UPLRENDER_ARTIFACT=$artifact"
exit 0
