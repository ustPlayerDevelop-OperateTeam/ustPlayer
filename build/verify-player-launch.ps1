# verify-player-launch.ps1 — 真实进程验证「播放链路」
#
# 为什么需要它：播放窗口继承 FluentAvalonia 的 AppWindow，其构造函数会无条件创建
# Win32WindowManager，在 headless 下必崩（见 verify-app-launch.ps1 的说明）。
# 因此 PlayerWindow.Show 这条路径**无法**用单元测试覆盖，只能用真实进程验证。
#
# 本脚本用 --play 启动一个临时生成的 UST，然后检查日志里出现：
#   1. 「已进入直接播放模式」—— UST 解析成功且走的是播放模式；
#   2. 「播放器帧合成器就绪」—— 渲染器已被配置（原生库可用）；
#   3. 「播放器已启动」      —— PlayerWindow.Show 真的把窗口显示出来了；
#   4. 「首帧已渲染」        —— 渲染器确实出了帧并拷进了位图。
# 并且要求**没有** ERROR 级日志：帧渲染失败只停掉帧循环、窗口仍开着，
# 只看前三条会把「一帧都没画出来」误判为通过。
# 只检查「进程还活着」同样不够：解析失败时不会开窗，进程也会安静地留在后台。
#
# 用法：pwsh -File build/verify-player-launch.ps1
# 退出码：0 = 通过；1 = 失败。

[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [int]$ObserveSeconds = 8
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$binDir = Join-Path $repoRoot "ustPlayer.Desktop\bin\$Configuration\net10.0"
$exe = Join-Path $binDir 'ustPlayer.exe'

if (-not (Test-Path $exe)) {
    Write-Host "未找到可执行文件：$exe。请先运行 dotnet build UstPlayer.slnx -c $Configuration"
    exit 1
}

# 生成一个足够长的临时 UST：40 个 480tick 音符 @120BPM = 20 秒内容 + 1 秒结束停留。
# 必须长于 $ObserveSeconds，否则播放结束、窗口自动关闭，进程退出会被误判为启动失败。
$ustDir = Join-Path $env:TEMP "ustplayer-verify-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $ustDir -Force | Out-Null
$ustPath = Join-Path $ustDir 'verify.ust'

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('[#VERSION]')
$lines.Add('UST Version1.2')
$lines.Add('[#SETTING]')
$lines.Add('Tempo=120.00')
$lines.Add('Tracks=1')
$lines.Add('ProjectName=verify')

for ($i = 0; $i -lt 40; $i++) {
    $lines.Add(('[#{0:0000}]' -f $i))
    $lines.Add('Length=480')
    $lines.Add('Lyric=a')
    $lines.Add('NoteNum=60')
}

Set-Content -Path $ustPath -Value $lines -Encoding ASCII

# 日志写在 exe 旁（不可写时回退用户数据目录）。这里只按 exe 旁查找，
# 找不到就明确报出来，避免"什么都没检查到"却显示通过。
$logPath = Join-Path $binDir 'ustPlayer.log'
$offsetBefore = 0

if (Test-Path $logPath) {
    $offsetBefore = (Get-Item $logPath).Length
}

$stdout = Join-Path $env:TEMP 'ustplayer-player-stdout.txt'
$stderr = Join-Path $env:TEMP 'ustplayer-player-stderr.txt'
Remove-Item $stdout, $stderr -ErrorAction SilentlyContinue

Write-Host "启动（--play）：$ustPath"
$proc = Start-Process -FilePath $exe `
    -ArgumentList @('--play', "`"$ustPath`"") `
    -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

Start-Sleep -Seconds $ObserveSeconds

$exited = $proc.HasExited
$exitCode = if ($exited) { $proc.ExitCode } else { $null }

if (-not $exited) {
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Milliseconds 300
}

# 只读本次运行新增的日志（日志是追加写的，历史内容是上一次运行的）
$newLog = ''
if (Test-Path $logPath) {
    $stream = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($offsetBefore, 'Begin') | Out-Null
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
        $newLog = $reader.ReadToEnd()
    }
    finally {
        $stream.Dispose()
    }
}
else {
    Write-Host "未找到日志文件：$logPath"
}

$markers = @(
    '已进入直接播放模式',
    '播放器帧合成器就绪',
    '播放器已启动',
    '首帧已渲染'
)

$missing = @()
foreach ($marker in $markers) {
    if ($newLog -notlike "*$marker*") {
        $missing += $marker
    }
}

# ERROR 级日志一律视为失败：帧渲染失败只停帧不关窗，上面四个标记仍会全部出现。
# 用 Contains 而不是 -like：PowerShell 通配符里的 [ERROR] 是"字符集合"，
# 会匹配任意一个 E/R/O 字符，从而把 [INFO] 也误判成错误。
$errors = @()
foreach ($line in ($newLog -split "`r?`n")) {
    if ($line.Contains('[ERROR]')) {
        $errors += $line
    }
}

# 内容约 20 秒、只观察 $ObserveSeconds 秒，因此此刻播放**必须仍在进行**。
# 这条断言专门捕捉"时间轴没锚定"这类 bug：那时第一帧就判定播完，
# 窗口在 1 秒内自动关闭、进程退出，而上面四个标记**依然会全部出现**。
$endedEarly = $exited -or
    $newLog.Contains('播放完成') -or
    $newLog.Contains('播放器已关闭')

if ($missing.Count -eq 0 -and $errors.Count -eq 0 -and -not $endedEarly) {
    Write-Host '播放链路验证通过：UST 解析 → 渲染器就绪 → 播放窗口已显示 → 首帧已渲染 → 播放进行中'
    Remove-Item $ustDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 0
}

Write-Host "进程已退出：$exited（退出码 $exitCode）"

if ($endedEarly) {
    Write-Host "播放提前结束：内容约 20 秒、只观察 $ObserveSeconds 秒，此时本应仍在播放。" `
        '最常见原因是时间轴零点未锚定（把"开机以来的秒数"当成了播放位置）。'
}

if ($missing.Count -gt 0) {
    Write-Host "缺少日志标记：$($missing -join ' / ')"
}

if ($errors.Count -gt 0) {
    Write-Host '出现 ERROR 级日志：'
    $errors | ForEach-Object { Write-Host "  $_" }
}

if ($newLog) {
    Write-Host '--- 本次运行日志 ---'
    $newLog -split "`r?`n" | Select-Object -First 40 | ForEach-Object { Write-Host $_ }
}

$errText = Get-Content $stderr -ErrorAction SilentlyContinue -Encoding UTF8
if ($errText) {
    Write-Host '--- stderr ---'
    $errText | Select-Object -First 40 | ForEach-Object { Write-Host $_ }
}

# 显式 exit 1：Write-Error 不会中断脚本，否则会落到末尾静默通过
Remove-Item $ustDir -Recurse -Force -ErrorAction SilentlyContinue
exit 1
