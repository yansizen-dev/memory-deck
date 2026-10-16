#Requires -Version 5.1
<#
.SYNOPSIS
    MemoryDeck 一键验收脚本：跑自检 + 检查安装状态 + 打印当前数据。
.DESCRIPTION
    在你手动点界面之前/之后各跑一次，用来对账。

    · 点界面之前跑：记录基线（条目数、哈希）
    · 点界面之后跑：看条目数是否按预期变化、有没有生成备份

.PARAMETER Baseline
    把当前数据状态存成基线快照，供之后比对。

.PARAMETER Compare
    与最近一次基线快照比对，报告差异。
#>
[CmdletBinding()]
param(
    [switch]$Baseline,
    [switch]$Compare
)

$ErrorActionPreference = 'Continue'

$AppDir     = $PSScriptRoot
$RepoRoot   = (Resolve-Path (Join-Path $AppDir '..\..')).Path
$Exe        = Join-Path $AppDir 'publish\MemoryDeck.exe'
$DebugExe   = Join-Path $AppDir 'bin\Debug\net10.0-windows\MemoryDeck.exe'
$MemoryPath = Join-Path $RepoRoot 'MEMORY.md'
$AgentsPath = Join-Path $RepoRoot 'AGENTS.md'
$BackupDir  = Join-Path $RepoRoot '.memory-deck\backups'
$SnapshotDir = Join-Path $env:TEMP 'MemoryDeck'
$BaselinePath = Join-Path $SnapshotDir 'acceptance-baseline.json'

function Section([string]$Title) {
    Write-Host ''
    Write-Host "── $Title" -ForegroundColor Cyan
}

function Ok([string]$Text)   { Write-Host "  [OK]   $Text" -ForegroundColor Green }
function Bad([string]$Text)  { Write-Host "  [BAD]  $Text" -ForegroundColor Red }
function Info([string]$Text) { Write-Host "         $Text" -ForegroundColor Gray }

# ============================================================ 数据读取

function Get-State {
    $entries = @()
    if (Test-Path $MemoryPath) {
        foreach ($line in (Get-Content $MemoryPath -Encoding UTF8)) {
            if ($line -match '^\s*\|\s*(M-\d+)\s*\|') {
                $cells = $line.Trim().Trim('|') -split '\|'
                $entries += [pscustomobject]@{
                    Id       = $cells[0].Trim()
                    Category = if ($cells.Count -gt 1) { $cells[1].Trim() } else { '' }
                    Content  = if ($cells.Count -gt 2) { $cells[2].Trim() } else { '' }
                    Time     = if ($cells.Count -gt 4) { $cells[4].Trim() } else { '' }
                }
            }
        }
    }

    [pscustomobject]@{
        Entries    = $entries
        Count      = $entries.Count
        MemoryHash = if (Test-Path $MemoryPath) { (Get-FileHash $MemoryPath -Algorithm SHA256).Hash.Substring(0, 16) } else { $null }
        AgentsHash = if (Test-Path $AgentsPath) { (Get-FileHash $AgentsPath -Algorithm SHA256).Hash.Substring(0, 16) } else { $null }
        MemoryTime = if (Test-Path $MemoryPath) { (Get-Item $MemoryPath).LastWriteTime } else { $null }
        Backups    = if (Test-Path $BackupDir) { (Get-ChildItem $BackupDir -Filter '*.md').Count } else { 0 }
        Captured   = Get-Date
    }
}

# ============================================================ 基线 / 比对

if ($Baseline -or $Compare) {
    New-Item -ItemType Directory -Path $SnapshotDir -Force | Out-Null
    $current = Get-State

    if ($Baseline) {
        $current | ConvertTo-Json -Depth 5 | Set-Content $BaselinePath -Encoding UTF8
        Section '已记录基线'
        Ok "条目数 $($current.Count)，写入 $BaselinePath"
        $current.Entries | Format-Table Id, Category, Time -AutoSize
        return
    }

    Section '与基线比对'
    if (-not (Test-Path $BaselinePath)) {
        Bad '还没有基线。先跑一次 -Baseline。'
        return
    }

    $base = Get-Content $BaselinePath -Raw -Encoding UTF8 | ConvertFrom-Json

    Write-Host "  基线：$($base.Count) 条，MEMORY.md 哈希 $($base.MemoryHash)，备份 $($base.Backups) 份"
    Write-Host "  现在：$($current.Count) 条，MEMORY.md 哈希 $($current.MemoryHash)，备份 $($current.Backups) 份"
    Write-Host ''

    $delta = $current.Count - $base.Count
    if ($delta -eq 0 -and $current.MemoryHash -eq $base.MemoryHash) {
        Info '条目数和文件哈希都没变（如果你刚做了增删，说明没生效）'
    }
    elseif ($current.MemoryHash -eq $base.MemoryHash) {
        Info '文件哈希未变，但条目数统计有差异（检查解析）'
    }
    else {
        Ok "MEMORY.md 已变化，条目数 $($base.Count) -> $($current.Count)（Δ$delta）"
    }

    # 新增了哪些
    $baseIds = @($base.Entries | ForEach-Object { $_.Id })
    $new = @($current.Entries | Where-Object { $_.Id -notin $baseIds })
    $gone = @($base.Entries | Where-Object { $_.Id -notin @($current.Entries | ForEach-Object { $_.Id }) })

    if ($new.Count -gt 0) {
        Write-Host ''
        Ok "新增 $($new.Count) 条："
        $new | Format-Table Id, Category, Content -AutoSize
    }
    if ($gone.Count -gt 0) {
        Write-Host ''
        Ok "删除 $($gone.Count) 条："
        $gone | Format-Table Id, Content -AutoSize
    }

    if ($current.Backups -gt $base.Backups) {
        Ok "新增 $($current.Backups - $base.Backups) 份自动备份"
    }
    return
}

# ============================================================ 完整验收

Write-Host 'MemoryDeck 验收检查' -ForegroundColor White
Write-Host ('=' * 58)

Section '1. 安装状态'
if (Test-Path $Exe) {
    $size = [math]::Round((Get-Item $Exe).Length / 1KB, 1)
    Ok "发布版存在：$Exe ($size KB)"
} else {
    Bad "发布版不存在：$Exe（先跑 build.ps1）"
}
if (Test-Path $DebugExe) { Ok "调试版存在（可用于 -TestOnly）" }

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) '记忆卡片管理器.lnk'
if (Test-Path $shortcut) { Ok "桌面快捷方式存在" } else { Bad "桌面快捷方式不存在" }

$envData = [Environment]::GetEnvironmentVariable('MEMORYDECK_DATA', 'User')
if ($envData) { Ok "MEMORYDECK_DATA = $envData" } else { Info 'MEMORYDECK_DATA 未设置（会走推断逻辑）' }

Section '2. 数据文件'
if (Test-Path $MemoryPath) { Ok "MEMORY.md 存在" } else { Bad "MEMORY.md 不存在" }
if (Test-Path $AgentsPath) { Ok "AGENTS.md 存在" } else { Bad "AGENTS.md 不存在" }

$agentsText = if (Test-Path $AgentsPath) { Get-Content $AgentsPath -Raw -Encoding UTF8 } else { '' }
if ($agentsText -match '<!-- MEMORY-DECK:SUMMARY:BEGIN -->') { Ok 'AGENTS.md 含摘要标记区' }
else { Bad 'AGENTS.md 缺少摘要标记区（程序无法自动同步）' }

Section '3. 自检'
$target = if (Test-Path $Exe) { $Exe } else { $DebugExe }
if ($target -and (Test-Path $target)) {
    $proc = Start-Process -FilePath $target -ArgumentList '--selftest' -PassThru -Wait
    $log = Join-Path $SnapshotDir 'selftest-last.log'
    if (Test-Path $log) {
        Get-Content $log -Encoding UTF8 | Select-String -Pattern '通过 |结果：|\[FAIL\]' |
            ForEach-Object { Info $_.Line.Trim() }
    }
    if ($proc.ExitCode -eq 0) { Ok '自检通过' } else { Bad "自检失败（退出码 $($proc.ExitCode)）" }
}

Section '4. 当前数据'
$state = Get-State
Write-Host "  条目数：$($state.Count)"
Write-Host "  自动备份：$($state.Backups) 份"
if ($state.MemoryTime) { Write-Host "  MEMORY.md 最后写入：$($state.MemoryTime)" }
Write-Host ''
if ($state.Count -gt 0) {
    $state.Entries | Format-Table Id, Category, Content, Time -AutoSize
}

Section '5. 验收建议操作'
Write-Host @'
  在界面上依次试：
    1. 双击桌面「记忆卡片管理器」→ 窗口应正常出现
    2. Ctrl+N 新增一条 → 填内容 → Ctrl+S 保存 → 表格应立刻多一行
    3. 选中新条目 → 双击进入编辑 → 改内容 → Ctrl+S
    4. 搜索框输入关键词 → 只显示匹配行
    5. 类别下拉 → 按类别过滤
    6. 删除选中条目（会弹确认）
    7. 用记事本改 MEMORY.md 加一行 → 等 2 秒 → 界面应自动刷新

  每一步之后回到这里跑：
    .\acceptance.ps1 -Compare      # 与基线比对
'@
