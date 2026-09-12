# extract-release-notes.ps1 — 从 CHANGELOG.md 提取某个版本的发布说明，并附 SHA256 校验表
#
# 为什么需要它：1.1.x 的 CI 会从 CHANGELOG 提取 Release 说明（找不到对应小节就直接失败，
# 不静默发布占位符），并由 CI 在末尾追加全部发布附件的 SHA256 表。2.0 的 CI 还没有这一环。
#
# 版本匹配规则（沿用 1.1.x，见 AGENTS.md）：
#   · `v` 前缀可省略
#   · 连字符与空格互通（1.1.0-beta-2 ≡ 1.1.0 Beta 2）
#   · 大小写不敏感
#   · 顶部 `## Unreleased` 之类的二级标题不会被当作版本小节（只认一级 `# `）
#
# 用法：
#   pwsh -File build/extract-release-notes.ps1 -Tag v1.1.0-beta-2
#   pwsh -File build/extract-release-notes.ps1 -Tag 2.0.0 -Artifacts artifacts/*.zip -OutputFile notes.md
#
# 退出码：0 = 提取成功；1 = 找不到该版本小节（发版必须中止，而不是发占位内容）。

[CmdletBinding()]
param(
    # 标签名或版本号（如 v1.1.0-beta-2 / 1.1.0 Beta 2）。
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    # CHANGELOG 路径；默认为仓库根目录的 CHANGELOG.md。
    [string]$ChangeLog,

    # 要计算 SHA256 的发布附件（zip 等）；给空则只输出说明正文。
    [string[]]$Artifacts,

    # 输出文件；不给则打印到标准输出。
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ChangeLog) {
    $ChangeLog = Join-Path $repoRoot 'CHANGELOG.md'
}

if (-not (Test-Path $ChangeLog)) {
    Write-Host "未找到 CHANGELOG：$ChangeLog"
    exit 1
}

# ===================== 版本名规范化 =====================

function ConvertTo-NormalizedVersion {
    param([string]$Value)

    $text = $Value.Trim()

    # 去掉可选的 v 前缀
    if ($text -match '^[vV](?=\d)') {
        $text = $text.Substring(1)
    }

    # 连字符视作空格，再压缩空白，便于「1.1.0-beta-2」与「1.1.0 Beta 2」互通
    $text = $text -replace '-', ' '
    $text = $text -replace '\s+', ' '

    return $text.Trim().ToLowerInvariant()
}

$wanted = ConvertTo-NormalizedVersion $Tag

# ===================== 切分小节 =====================

$lines = Get-Content $ChangeLog -Encoding UTF8

$sections = @{}          # 规范名 → 正文行
$order = New-Object System.Collections.Generic.List[string]

$currentKey = $null
$currentBody = New-Object System.Collections.Generic.List[string]

function Close-Section {
    if ($null -ne $script:currentKey) {
        $script:sections[$script:currentKey] = $script:currentBody
    }
}

foreach ($line in $lines) {
    # 只认一级标题作为版本小节；`## Unreleased` 等二级标题不参与
    if ($line -match '^#\s+(.+?)\s*$') {
        Close-Section
        $currentKey = ConvertTo-NormalizedVersion $Matches[1]
        $currentBody = New-Object System.Collections.Generic.List[string]
        $order.Add($currentKey)
        continue
    }

    if ($null -ne $currentKey) {
        $currentBody.Add($line)
    }
}

Close-Section

if (-not $sections.ContainsKey($wanted)) {
    Write-Host "CHANGELOG 中没有版本「$Tag」（规范化后为「$wanted」）的小节。"
    Write-Host '可用的小节：'
    $order | ForEach-Object { Write-Host "  - $_" }
    Write-Host '发版必须中止：找不到对应小节时发布占位内容会让用户看不到真实更新说明。'
    exit 1
}

# 去掉小节首尾的空行
$body = $sections[$wanted]
while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[0])) { $body.RemoveAt(0) }
while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[$body.Count - 1])) { $body.RemoveAt($body.Count - 1) }

$notes = New-Object System.Collections.Generic.List[string]
$notes.AddRange([string[]]$body)

# ===================== SHA256 校验表 =====================

if ($Artifacts -and $Artifacts.Count -gt 0) {
    # 必须逐项展开成**具体文件**：Test-Path / Get-FileHash 都接受通配符，
    # 直接把通配符喂进去会在一个「路径」上返回多个哈希，于是被拼成一行乱码，
    # 整个校验表失效（实测：传 *.zip 得到的是 `*.zip` + 两段哈希拼接）。
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]

    foreach ($pattern in $Artifacts) {
        $matched = @(Get-Item -Path $pattern -ErrorAction SilentlyContinue |
            Where-Object { -not $_.PSIsContainer })

        if ($matched.Count -eq 0) {
            Write-Host "警告：没有匹配到任何文件：$pattern"
            continue
        }

        foreach ($item in $matched) {
            $files.Add($item)
        }
    }

    if ($files.Count -eq 0) {
        Write-Host '给了 -Artifacts 但一个文件都不存在，无法生成校验表。'
        exit 1
    }

    $notes.Add('')
    $notes.Add('> [!important]')
    $notes.Add('> 下载后请核对 SHA256，确认文件完整未被篡改。')
    $notes.Add('')
    $notes.Add('| 文件 | SHA256 |')
    $notes.Add('| --- | --- |')

    foreach ($file in $files | Sort-Object -Property Name) {
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        $notes.Add("| ``$($file.Name)`` | ``$hash`` |")
    }
}

# ===================== 输出 =====================

$text = ($notes -join [Environment]::NewLine).TrimEnd() + [Environment]::NewLine

if ($OutputFile) {
    # 发版说明交给 GitHub Actions 读取，用无 BOM 的 UTF-8（带 BOM 会让首行标题渲染异常）
    [System.IO.File]::WriteAllText($OutputFile, $text, [System.Text.UTF8Encoding]::new($false))
    Write-Host "已写入发布说明：$OutputFile"
    exit 0
}

Write-Output $text
exit 0
