# capture-window.ps1 — 把主窗口某个页面截成 PNG（开发/核对界面用）
#
# 为什么需要它：FluentAvalonia 的卡片外观、导航项位置这类东西**只能看渲染结果**，
# 在无头单元测试里既测不了也看不见（见 docs/adr-0002-window-chrome.md）。
# 只靠读 XAML 数值推断版式，已经有过判断错的情况，因此留一个能自己核对的手段。
#
# 用法：
#   pwsh -File build/capture-window.ps1 -Page settings -Output artifacts/ui-shots/settings.png
#   pwsh -File build/capture-window.ps1 -Page basic -Output artifacts/ui-shots/basic.png
#
#   # 版式自检：额外渲染一张由 SettingsRow 画的参照卡片（见 SettingsPage.axaml）
#   pwsh -File build/capture-window.ps1 -Page settings -Environment USTPLAYER_UI_PROBE=1
#
# 退出码：0 = 已截图；1 = 窗口没起来 / 截图失败。

[CmdletBinding()]
param(
    [string]$Page = 'basic',
    [string]$Configuration = 'Debug',
    [string]$Output,
    [int]$WaitSeconds = 6,
    [string[]]$Environment
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot "ustPlayer.Desktop\bin\$Configuration\net10.0\ustPlayer.exe"

if (-not (Test-Path $exe)) {
    Write-Host "未找到可执行文件：$exe。请先运行 dotnet build UstPlayer.slnx -c $Configuration"
    exit 1
}

if (-not $Output) {
    $Output = Join-Path $repoRoot "artifacts\ui-shots\$Page.png"
}

$outputDir = Split-Path -Parent $Output
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Add-Type -AssemblyName System.Drawing

# PrintWindow 的 PW_RENDERFULLCONTENT：让窗口自己重画一遍到我们给的 DC 上。
# 用 BitBlt 抓屏幕不行——窗口可能被遮住或不在前台，抓到的就是别的东西。
Add-Type -Namespace UstShot -Name Native -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

[DllImport("user32.dll")]
public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

[DllImport("user32.dll")]
public static extern bool SetForegroundWindow(IntPtr hwnd);

[StructLayout(LayoutKind.Sequential)]
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

Write-Host "启动：$exe --page $Page"

# Start-Process -Environment 只覆盖列出的变量（其余照旧继承），但它是 PowerShell 7.4+
# 才有的参数，因此这里按版本分支：7+ 直接用它；5.1 用「临时改本进程环境 → 启动 → 还原」
# （子进程继承当前环境，效果相同），这样两种 PowerShell 都能跑。
#
# 参数写成 `键=值` 数组而不是哈希表：`-File` 调用只能传字符串，传哈希表会报
# 「无法将 System.Collections.Hashtable 转换为 System.String」。
$restore = @{}
if ($Environment) {
    foreach ($pair in $Environment) {
        $parts = $pair.Split('=', 2)
        if ($parts.Count -ne 2) {
            Write-Host "环境变量写法应为 键=值：$pair"
            exit 1
        }

        $restore[$parts[0]] = [System.Environment]::GetEnvironmentVariable($parts[0])
        [System.Environment]::SetEnvironmentVariable($parts[0], $parts[1])
    }
}

$proc = if ($PSVersionTable.PSVersion.Major -ge 7 -and $Environment) {
    # 7+ 上改用 -Environment，省掉「改了再还原」这一步（5.1 没有这个参数）
    $table = @{}
    foreach ($pair in $Environment) {
        $parts = $pair.Split('=', 2)
        $table[$parts[0]] = $parts[1]
    }

    Start-Process -FilePath $exe -ArgumentList '--page', $Page -PassThru -Environment $table
} else {
    Start-Process -FilePath $exe -ArgumentList '--page', $Page -PassThru
}

# 子进程已经拿到环境块，立刻还原，避免影响后续调用
foreach ($key in $restore.Keys) {
    [System.Environment]::SetEnvironmentVariable($key, $restore[$key])
}

Start-Sleep -Seconds $WaitSeconds

if ($proc.HasExited) {
    Write-Host "应用在 $WaitSeconds 秒内退出（退出码 $($proc.ExitCode)），无法截图"
    exit 1
}

$hwnd = $proc.MainWindowHandle

# 窗口句柄要等窗口真正创建出来；MainWindowHandle 偶尔会晚上一拍
for ($i = 0; $i -lt 20 -and $hwnd -eq [IntPtr]::Zero; $i++) {
    Start-Sleep -Milliseconds 250
    $proc.Refresh()
    $hwnd = $proc.MainWindowHandle
}

if ($hwnd -eq [IntPtr]::Zero) {
    Write-Host '拿不到窗口句柄，无法截图'
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

[void][UstShot.Native]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 700

$rect = New-Object UstShot.Native+RECT
[void][UstShot.Native]::GetWindowRect($hwnd, [ref]$rect)

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

if ($width -le 0 -or $height -le 0) {
    Write-Host "窗口尺寸异常：${width}x${height}"
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()

# 2 = PW_RENDERFULLCONTENT（DirectComposition 内容也要）
$ok = [UstShot.Native]::PrintWindow($hwnd, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

# 句柄要在进程还活着的时候用，因此先截图再结束进程
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

if (-not $ok) {
    Write-Host 'PrintWindow 失败'
    $bitmap.Dispose()
    exit 1
}

$bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Host "已截图：$Output（${width}x${height}）"
exit 0
