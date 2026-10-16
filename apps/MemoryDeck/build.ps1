#Requires -Version 5.1
<#
.SYNOPSIS
    MemoryDeck 构建 / 自检 / 发布 / 安装脚本。

.DESCRIPTION
    一键走完：编译 -> 自检 -> 单文件发布 -> 写配置 -> 建桌面快捷方式。
    全程不需要联网安装任何东西（除了首次发布时恢复运行时包）。

.PARAMETER TestOnly
    只跑自检，不编译不发布。

.PARAMETER Install
    发布后重建桌面快捷方式并设置 MEMORYDECK_DATA 环境变量。

.PARAMETER DataDirectory
    数据目录，默认是仓库根（本脚本所在目录往上两级）。

.PARAMETER SkipBuild
    跳过编译，直接用现有 publish 产物。

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Install
    .\build.ps1 -TestOnly -Verbose
#>
[CmdletBinding()]
param(
    [switch]$TestOnly,
    [switch]$Install,
    [switch]$SkipBuild,
    [string]$DataDirectory
)

$ErrorActionPreference = 'Stop'

$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir 'MemoryDeck.csproj'
$PublishDir = Join-Path $ProjectDir 'publish'
$ExePath = Join-Path $PublishDir 'MemoryDeck.exe'
$ConfigPath = Join-Path $PublishDir 'memorydeck.config.json'

if (-not $DataDirectory) {
    $DataDirectory = (Resolve-Path (Join-Path $ProjectDir '..\..')).Path
}
$DataDirectory = $DataDirectory.TrimEnd('\')

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "── $Text" -ForegroundColor Cyan
}

function Invoke-SelfTest([string]$Exe) {
    if (-not (Test-Path $Exe)) { throw "找不到可执行文件：$Exe" }

    $proc = Start-Process -FilePath $Exe -ArgumentList '--selftest' -PassThru -Wait
    $log = Join-Path $env:TEMP 'MemoryDeck\selftest-last.log'

    if (Test-Path $log) {
        Get-Content $log -Encoding UTF8 | Select-String -Pattern '通过 |结果：|\[FAIL\]' |
            ForEach-Object { Write-Host ('  ' + $_.Line) }
    }

    if ($proc.ExitCode -ne 0) {
        Write-Host "自检失败（退出码 $($proc.ExitCode)），日志：$log" -ForegroundColor Red
        throw '自检未通过，中止。'
    }

    Write-Host "  自检通过（退出码 0）" -ForegroundColor Green
}

function New-Shortcut([string]$Target, [string]$WorkingDirectory) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $link = Join-Path $desktop '记忆卡片管理器.lnk'

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = 'MemoryDeck — 记忆卡片管理器（Markdown 为数据源）'
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Save()

    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    Write-Host "  桌面快捷方式：$link" -ForegroundColor Green
}

# ============================================================ 主流程

Write-Host 'MemoryDeck 构建脚本' -ForegroundColor White
Write-Host "  项目  ：$ProjectFile"
Write-Host "  数据目录：$DataDirectory"

if ($TestOnly) {
    Write-Step '自检现有产物'
    $target = if (Test-Path $ExePath) { $ExePath }
              else { Join-Path $ProjectDir 'bin\Debug\net10.0-windows\MemoryDeck.exe' }
    Invoke-SelfTest $target
    return
}

if (-not $SkipBuild) {
    Write-Step '发布（Release / win-x64 / 单文件）'

    # 正在运行的实例会锁住 publish\MemoryDeck.exe，导致 GenerateBundle 报
    # UnauthorizedAccessException。这里先清理，避免"编译成功但产物是旧的"这种假象。
    $running = @(Get-Process -Name 'MemoryDeck' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Host "  检测到 $($running.Count) 个正在运行的 MemoryDeck，先关闭以免锁住输出文件" -ForegroundColor Yellow
        foreach ($proc in $running) {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                Wait-Process -Id $proc.Id -Timeout 10 -ErrorAction SilentlyContinue
                Write-Host "    已关闭 PID $($proc.Id)"
            }
            catch {
                Write-Host "    关闭 PID $($proc.Id) 失败：$($_.Exception.Message)" -ForegroundColor Red
            }
        }
        Start-Sleep -Milliseconds 500
    }

    Push-Location $ProjectDir
    try {
        # 注意：这里不要接管道。把输出接给 Select-Object / Select-String 会把
        # 失败信息过滤掉，造成"看起来构建成功"的假象，而实际 $LASTEXITCODE 是 1。
        & dotnet publish $ProjectFile -c Release -r win-x64 --self-contained false -o $PublishDir --nologo -v minimal
        $publishExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($publishExit -ne 0) {
        Write-Host "  发布失败（退出码 $publishExit），未更新产物。" -ForegroundColor Red
        throw "dotnet publish 失败（退出码 $publishExit）"
    }

    Write-Host '  产物：' -NoNewline
    Get-ChildItem $PublishDir | ForEach-Object {
        Write-Host ("$($_.Name) ($([math]::Round($_.Length / 1KB, 1)) KB)") -ForegroundColor Green
    }
}

Write-Step '自检'
Invoke-SelfTest $ExePath

Write-Step '写数据目录配置'
$config = [ordered]@{
    DataDirectory            = $DataDirectory
    MemoryFileNameOverride   = 'MEMORY.md'
    AgentsFileNameOverride   = 'AGENTS.md'
}
$configJson = $config | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText($ConfigPath, $configJson, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  $ConfigPath" -ForegroundColor Green

if ($Install) {
    Write-Step '设置用户环境变量 MEMORYDECK_DATA'
    [Environment]::SetEnvironmentVariable('MEMORYDECK_DATA', $DataDirectory, 'User')
    Write-Host "  MEMORYDECK_DATA = $DataDirectory" -ForegroundColor Green

    Write-Step '创建桌面快捷方式'
    New-Shortcut -Target $ExePath -WorkingDirectory $PublishDir
}

Write-Host ''
Write-Host '完成。' -ForegroundColor Green
