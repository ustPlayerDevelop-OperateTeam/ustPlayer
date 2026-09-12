# publish.ps1 — 打成可分发产物（zip + SHA256）
#
# 为什么需要它：CI 目前只做构建与测试，**没有任何发布步骤**。
# 而「资源没打进产物」是这个仓库反复踩过的一类坑（翻译 .ts、ERcode.txt / Terms.txt
# 都曾只存在于仓库里，运行时静默失效）。因此本脚本的重点不是复制（csproj 已经声明了
# 复制项），而是**校验**产物里该有的东西都在，缺一个就明确失败。
#
# 用法：
#   pwsh -File build/publish.ps1                          # win-x64 自包含
#   pwsh -File build/publish.ps1 -RuntimeIdentifier linux-x64
#   pwsh -File build/publish.ps1 -SelfContained:$false    # 依赖框架（体积小，需装 .NET 运行时）
#   pwsh -File build/publish.ps1 -Version 2.0.0-beta1     # 覆盖程序集版本
#
# 退出码：0 = 产物完整；1 = 构建失败或产物缺件。

[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$Version,
    [bool]$SelfContained = $true,
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\ustPlayer-$RuntimeIdentifier"
}

$project = Join-Path $repoRoot 'ustPlayer.Desktop\ustPlayer.Desktop.csproj'

if (-not (Test-Path $project)) {
    Write-Host "未找到工程：$project"
    exit 1
}

if (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}

Write-Host "发布 $RuntimeIdentifier（自包含=$SelfContained，配置=$Configuration）"

$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    "--self-contained=$($SelfContained.ToString().ToLowerInvariant())",
    '-o', $OutputDirectory,
    '--nologo'
)

if ($Version) {
    $arguments += "-p:Version=$Version"
    Write-Host "覆盖版本号：$Version"
}

& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    Write-Host "发布失败（dotnet publish 退出码 $LASTEXITCODE）"
    exit 1
}

# ===================== 产物校验 =====================

$isWindows = $RuntimeIdentifier -like 'win-*'

# 文件名按平台区分（与 build/sync-native-assets.ps1 的约定一致）
$rendererName = if ($isWindows) { 'ustplayer_renderer.dll' }
    elseif ($RuntimeIdentifier -like 'osx-*') { 'libustplayer_renderer.dylib' }
    else { 'libustplayer_renderer.so' }

$ffmpegName = if ($isWindows) { 'ffmpeg.exe' } else { 'ffmpeg' }
$ffprobeName = if ($isWindows) { 'ffprobe.exe' } else { 'ffprobe' }

# 「必须有」：缺了功能会静默失效
$required = @(
    'ustPlayer.exe',
    'i18n\ustplayer_zh_CN.ts',
    'i18n\ustplayer_en_US.ts',
    'i18n\ustplayer_zh_classic.ts',
    'ERcode.txt',
    'Terms.txt',
    'LICENSE'
)

if (-not $isWindows) {
    # 非 Windows 的可执行文件没有 .exe 后缀
    $required[0] = 'ustPlayer'
}

$missing = @()
foreach ($item in $required) {
    if (-not (Test-Path (Join-Path $OutputDirectory $item))) {
        $missing += $item
    }
}

if ($missing.Count -gt 0) {
    Write-Host '产物缺件（这些文件缺失不会报错，只会让功能静默失效）：'
    $missing | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

# 原生件：Windows 上必须有（渲染器与 ffmpeg 是本机可得的）；
# 其他平台目前**确实没有**渲染器产物（Spike 0c 待补目标），因此只提示不失败。
$nativeChecks = @(
    @{ Path = "renderer\$rendererName"; Label = '渲染器'; Fatal = $isWindows },
    @{ Path = "ffmpeg\$ffmpegName"; Label = 'ffmpeg'; Fatal = $isWindows },
    @{ Path = "ffmpeg\$ffprobeName"; Label = 'ffprobe'; Fatal = $isWindows }
)

$nativeMissing = @()
foreach ($check in $nativeChecks) {
    if (-not (Test-Path (Join-Path $OutputDirectory $check.Path))) {
        $nativeMissing += $check
    }
}

foreach ($check in $nativeMissing) {
    if ($check.Fatal) {
        Write-Host "产物缺少$($check.Label)：$($check.Path)"
        Write-Host '  本机可用 build/sync-native-assets.ps1 与 build/fetch-ffmpeg.ps1 就位后重试。'
        exit 1
    }

    Write-Host "提示：本平台产物不含$($check.Label)（$($check.Path)）——渲染器目前只有 Windows 产物，见 docs/plan-deviations.md D3。"
}

# ===================== 打包与校验和 =====================

$totalMb = [math]::Round((Get-ChildItem $OutputDirectory -Recurse -File |
    Measure-Object -Property Length -Sum).Sum / 1MB, 1)

Write-Host "产物已就位：$OutputDirectory（$totalMb MB）"

if ($SkipArchive) {
    exit 0
}

$artifactsDirectory = Join-Path $repoRoot 'artifacts'
$archiveBase = "ustPlayer-$RuntimeIdentifier"
$archivePath = Join-Path $artifactsDirectory "$archiveBase.zip"

if (Test-Path $archivePath) {
    Remove-Item $archivePath -Force
}

Write-Host '正在压缩…'
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

$hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash
$archiveMb = [math]::Round((Get-Item $archivePath).Length / 1MB, 1)

# 校验和写成文件（发版时由 CI 汇总进 Release 说明）
$hashFile = "$archivePath.sha256"
"$hash  $archiveBase.zip" | Set-Content -Path $hashFile -Encoding ASCII

Write-Host "已打包：$archivePath（$archiveMb MB）"
Write-Host "SHA256：$hash"

exit 0
