# verify-app-launch.ps1 — 真实进程启动验证
#
# 为什么需要它：FluentAvalonia 的 AppWindow 构造函数会无条件创建
# FluentAvalonia.UI.Windowing.Win32WindowManager，在 Avalonia 的 headless 平台上
# 会因重复注册 Win32 属性而抛异常：
#     ArgumentException: An item with the same key has already been added. Key: FluentAvalonia.Interop.Win32.HWND
# 因此窗口的实例化与显示**无法**在 headless 单元测试里覆盖，只能以真实进程启动验证。
#
# 用法：pwsh -File build/verify-app-launch.ps1
# 退出码：0 = 启动成功；1 = 启动失败或异常退出。

[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [int]$ObserveSeconds = 6
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot "ustPlayer.Desktop\bin\$Configuration\net10.0\ustPlayer.exe"

if (-not (Test-Path $exe)) {
    Write-Host "未找到可执行文件：$exe。请先运行 dotnet build UstPlayer.slnx -c $Configuration"
    exit 1
}

$stdout = Join-Path $env:TEMP 'ustplayer-launch-stdout.txt'
$stderr = Join-Path $env:TEMP 'ustplayer-launch-stderr.txt'
Remove-Item $stdout, $stderr -ErrorAction SilentlyContinue

Write-Host "启动：$exe"
$proc = Start-Process -FilePath $exe -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

Start-Sleep -Seconds $ObserveSeconds

if ($proc.HasExited) {
    $code = $proc.ExitCode
    Write-Host "进程已退出，退出码 $code"
    $errText = Get-Content $stderr -ErrorAction SilentlyContinue
    if ($errText) {
        Write-Host '--- stderr ---'
        $errText | Select-Object -First 40 | ForEach-Object { Write-Host $_ }
    }

    Write-Host "应用启动失败：进程在 $ObserveSeconds 秒内退出"
    # 必须显式 exit 1：Write-Error 只写错误流，不会中断脚本（且 $ErrorActionPreference
    # 在这里不足以让 CI 正确判定），否则脚本会落到末尾的 exit 0 → CI 静默通过。
    exit 1
}

Write-Host "启动成功且稳定运行（PID $($proc.Id)）"
Stop-Process -Id $proc.Id -Force
Start-Sleep -Milliseconds 300
Write-Host '已结束进程'
exit 0
