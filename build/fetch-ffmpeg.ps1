# fetch-ffmpeg.ps1 — 获取内置 FFmpeg（ffmpeg + ffprobe）
#
# 背景：渲染器 uPlRender 的编码器**只从 PATH 查找 ffmpeg**（见 BundledFfmpegPathScope），
# 因此没有 ffmpeg 时视频导出会直接失败：
#     up_begin_export 失败：编码失败（ffmpeg init failed: ffmpeg executable not found in PATH）
# 程序目录下的 ffmpeg/ 就是为此准备的（CI 打包时放入，实体不入库）。
#
# 本脚本服务**本地开发**：从官方构建包取出 ffmpeg / ffprobe 放到各工程的 ffmpeg/ 目录，
# 之后由工程的 MSBuild 项复制到输出目录（与 renderer/ 的做法一致）。
#
# 用法：
#   pwsh -File build/fetch-ffmpeg.ps1                 # 缺什么补什么
#   pwsh -File build/fetch-ffmpeg.ps1 -Force          # 强制重新下载
#   pwsh -File build/fetch-ffmpeg.ps1 -Destination D:\somewhere
#
# 退出码：0 = 已就位（含"本来就有，跳过"）；1 = 下载或解压失败。
#
# 仅支持 Windows：macOS / Linux 请用系统包管理器安装 ffmpeg 后，
# 把 ffmpeg 与 ffprobe 复制到 <工程>/ffmpeg/ 目录（或用 -SourceDirectory 指向已下载的目录）。

[CmdletBinding()]
param(
    # 目标目录；默认为两个工程的 ffmpeg/ 子目录。
    [string[]]$Destination,

    # 已下载好的、含 ffmpeg/ffprobe 的目录；给了就不下载。
    [string]$SourceDirectory,

    # 下载地址，可用环境变量覆盖（镜像 / 内网）。
    [string]$Url = $(if ($env:USTPLAYER_FFMPEG_URL) { $env:USTPLAYER_FFMPEG_URL } else { 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' }),

    # 即使已存在也重新获取。
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 的 Invoke-WebRequest 会为每个数据块刷新进度条，下载大文件时慢到不可用
# （实测 111MB 的 FFmpeg 包跑 10 分钟仍未完成）。关掉进度显示并改用 WebClient。
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Destination -or $Destination.Count -eq 0) {
    $Destination = @(
        (Join-Path $repoRoot 'ustPlayer.Desktop\ffmpeg'),
        (Join-Path $repoRoot 'ustPlayer.Tests\ffmpeg')
    )
}

$isWindows = $env:OS -eq 'Windows_NT' -or $IsWindows
$names = if ($isWindows) { @('ffmpeg.exe', 'ffprobe.exe') } else { @('ffmpeg', 'ffprobe') }

function Test-AllPresent {
    param([string]$Directory)

    foreach ($name in $names) {
        if (-not (Test-Path (Join-Path $Directory $name))) {
            return $false
        }
    }

    return $true
}

# ---------- 1. 判断是否还需要获取 ----------

$needsFetch = $false
foreach ($directory in $Destination) {
    if ($Force -or -not (Test-AllPresent -Directory $directory)) {
        $needsFetch = $true
    }
}

if (-not $needsFetch) {
    Write-Host 'ffmpeg 与 ffprobe 已就位，跳过下载。'
    foreach ($directory in $Destination) {
        Write-Host "  $directory"
    }

    exit 0
}

# ---------- 2. 拿到含 ffmpeg/ffprobe 的源目录 ----------

$temporaryRoot = $null
$sourceRoot = $SourceDirectory

try {
    if (-not $sourceRoot) {
        if (-not $isWindows) {
            Write-Host '本脚本的自动下载仅支持 Windows。'
            Write-Host '请用系统包管理器安装 ffmpeg，然后用 -SourceDirectory 指向其所在目录：'
            Write-Host '  pwsh -File build/fetch-ffmpeg.ps1 -SourceDirectory /usr/bin'
            exit 1
        }

        $temporaryRoot = Join-Path $env:TEMP "ustplayer-ffmpeg-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

        $archivePath = Join-Path $temporaryRoot 'ffmpeg.zip'
        Write-Host "下载 FFmpeg：$Url"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        (New-Object Net.WebClient).DownloadFile($Url, $archivePath)

        $sizeMb = [math]::Round((Get-Item $archivePath).Length / 1MB, 1)
        Write-Host "已下载 $sizeMb MB，解压 ffmpeg / ffprobe ..."

        # 只取需要的两个可执行文件：整包解压有几百 MB，没必要
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)

        try {
            $extractRoot = Join-Path $temporaryRoot 'bin'
            New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

            foreach ($name in $names) {
                $entry = $archive.Entries |
                    Where-Object { $_.FullName -like "*/bin/$name" } |
                    Select-Object -First 1

                if (-not $entry) {
                    throw "压缩包中未找到 bin/$name —— 下载源可能不是预期的 FFmpeg 构建包。"
                }

                [System.IO.Compression.ZipFileExtensions]::ExtractToFile(
                    $entry, (Join-Path $extractRoot $name), $true)
            }
        }
        finally {
            $archive.Dispose()
        }

        $sourceRoot = $extractRoot
    }

    # ---------- 3. 分发到各目标目录 ----------

    foreach ($directory in $Destination) {
        if (-not (Test-Path $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        foreach ($name in $names) {
            $from = Join-Path $sourceRoot $name
            if (-not (Test-Path $from)) {
                throw "源目录中缺少 $name：$sourceRoot"
            }

            $to = Join-Path $directory $name

            if ($Force -or -not (Test-Path $to)) {
                Copy-Item -Path $from -Destination $to -Force
                $mb = [math]::Round((Get-Item $to).Length / 1MB, 1)
                Write-Host "已放入 $to（$mb MB）"
            }
            else {
                Write-Host "已存在，跳过：$to"
            }
        }
    }

    Write-Host '完成。实体文件不入库（见根 .gitignore），构建时会自动复制到输出目录。'
    exit 0
}
catch {
    Write-Host "获取 FFmpeg 失败：$($_.Exception.Message)"
    Write-Host '可手动下载后指定已解压目录：'
    Write-Host '  pwsh -File build/fetch-ffmpeg.ps1 -SourceDirectory <含 ffmpeg.exe 的目录>'
    exit 1
}
finally {
    if ($temporaryRoot -and (Test-Path $temporaryRoot)) {
        Remove-Item $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
