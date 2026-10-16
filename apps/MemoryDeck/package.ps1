#Requires -Version 5.1
<#
.SYNOPSIS
    MemoryDeck 发布打包脚本。把「可运行包」「素材包」「源码包」整理到 dist\ 下。

.DESCRIPTION
    输出结构：
      dist\
        MemoryDeck-<版本>-win-x64\     可运行包（解压即用）
          MemoryDeck.exe
          README.md
          快速上手.txt
          模板\                        首次使用的干净模板
            MEMORY.md
            AGENTS.md
        MemoryDeck-<版本>-素材\         发布会 / 分享用素材
          icon\                        图标全套
          mainwindow.png               界面截图
          export-dialog.png            导出对话框截图
          social-preview.png           社交预览图
        MemoryDeck-<版本>-win-x64.zip
        MemoryDeck-<版本>-素材.zip
        MemoryDeck-<版本>-源码.zip

.PARAMETER Version
    版本号。缺省时从 publish\MemoryDeck.exe 的文件版本里读。

.PARAMETER SkipZip
    只整理目录，不压缩。

.PARAMETER Clean
    先清空整个 dist\ 再打包。发正式版时用它，避免旧版本的残留文件混在一起。

.PARAMETER AsciiZipNames
    zip 用纯 ASCII 名字（-assets / -source）。
    要往 GitHub Release 上传时必须加这个：实测 CJK 文件名会让
    gh release upload 报 404 或把资产名截断成 xxx-.zip。

.EXAMPLE
    .\package.ps1
    .\package.ps1 -Clean -AsciiZipNames
    .\package.ps1 -Version 1.2.2
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipZip,
    [switch]$Clean,
    [switch]$AsciiZipNames
)

$ErrorActionPreference = 'Stop'

$AppDir   = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $AppDir '..\..')).Path
$DistDir  = Join-Path $RepoRoot 'dist'
$ExePath  = Join-Path $AppDir 'publish\MemoryDeck.exe'
$IconDir  = Join-Path $AppDir 'tools\icon'
$DocsDir  = Join-Path $AppDir 'docs'

function Step([string]$t) { Write-Host ''; Write-Host "── $t" -ForegroundColor Cyan }
function Ok([string]$t)   { Write-Host "  $t" -ForegroundColor Green }
function Warn([string]$t) { Write-Host "  $t" -ForegroundColor Yellow }

# ---------- 可选：清空 dist ----------
if ($Clean -and (Test-Path $DistDir)) {
    Write-Host '清空 dist\ ...' -ForegroundColor Yellow
    Get-ChildItem $DistDir | ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
}

# ---------- 版本号 ----------
if (-not $Version) {
    if (-not (Test-Path $ExePath)) { throw "找不到 $ExePath，先跑 build.ps1" }
    $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
    $Version = ($vi.FileVersion -split '\.')[0..2] -join '.'
}
Write-Host "MemoryDeck 打包" -ForegroundColor White
Write-Host "  版本  ：$Version"
Write-Host "  源目录：$AppDir"
Write-Host "  输出  ：$DistDir"

$AppName    = "MemoryDeck-$Version"
$RunDir     = Join-Path $DistDir "$AppName-win-x64"
$AssetDir   = Join-Path $DistDir "$AppName-素材"

# 每次重来，避免残留上一版的旧文件
foreach ($d in @($RunDir, $AssetDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
New-Item -ItemType Directory -Path $AssetDir -Force | Out-Null

# ---------- 1. 可运行包 ----------
Step '可运行包'

Copy-Item $ExePath (Join-Path $RunDir 'MemoryDeck.exe')
Ok 'MemoryDeck.exe'

Copy-Item (Join-Path $AppDir 'README.md') (Join-Path $RunDir 'README.md')
Ok 'README.md'

$quickStart = Join-Path $DocsDir '快速上手.txt'
if (Test-Path $quickStart) {
    Copy-Item $quickStart $RunDir
    Ok '快速上手.txt'
} else {
    Warn '快速上手.txt 不存在，跳过'
}

$templateDir = Join-Path $RunDir '模板'
New-Item -ItemType Directory -Path $templateDir -Force | Out-Null

# 模板放在 apps\MemoryDeck\templates\ 下，用 .template.md 后缀：
# 直接叫 AGENTS.md 的话，工具工作在这个目录里时会把它当指令文件自动加载。
# 打包时改回真名。
$templates = Join-Path $AppDir 'templates'
if (Test-Path $templates) {
    Copy-Item (Join-Path $templates 'MEMORY.template.md') (Join-Path $templateDir 'MEMORY.md')
    Copy-Item (Join-Path $templates 'AGENTS.template.md') (Join-Path $templateDir 'AGENTS.md')
    Ok '模板\（MEMORY.md / AGENTS.md）'
} else {
    Warn '未找到 templates\ 目录，跳过模板'
}

# 关键：publish\memorydeck.config.json 里写着开发机的绝对路径，
# 绝对不能进发布包。这里显式删掉它，防止后续有人手滑拷贝整个 publish 目录。
$leak = Join-Path $RunDir 'memorydeck.config.json'
if (Test-Path $leak) { Remove-Item $leak -Force }

# ---------- 2. 素材包 ----------
Step '素材包'

if (Test-Path $IconDir) {
    $iconOut = Join-Path $AssetDir 'icon'
    New-Item -ItemType Directory -Path $iconOut -Force | Out-Null

    # 只发正式产物，不带上对比稿和预览拼版
    Get-ChildItem $IconDir -File |
        Where-Object { $_.Name -notlike '_*' } |
        ForEach-Object { Copy-Item $_.FullName $iconOut }
    Ok ("icon\（" + (Get-ChildItem $iconOut -File).Count + " 个文件）")
} else {
    Warn "未找到图标目录，跳过"
}

foreach ($shot in 'mainwindow.png', 'export-dialog.png', 'social-preview.png') {
    $src = Join-Path $DocsDir $shot
    if (Test-Path $src) {
        Copy-Item $src $AssetDir
        Ok $shot
    } else {
        Warn "$shot 不存在，跳过"
    }
}

# ---------- 3. 源码包 ----------
Step '源码包'

$srcStage = Join-Path $env:TEMP "MemoryDeck-src-$Version"
if (Test-Path $srcStage) { Remove-Item $srcStage -Recurse -Force }
New-Item -ItemType Directory -Path $srcStage -Force | Out-Null

# 用 git 的归档能力，自动遵守 .gitignore
Push-Location $RepoRoot
try {
    $hasGit = Test-Path (Join-Path $RepoRoot '.git')
    if ($hasGit) {
        & git archive --format=zip --output="$srcStage\src.zip" HEAD 2>&1 | Out-Null
        $srcZip = Join-Path $srcStage 'src.zip'
        if (Test-Path $srcZip) {
            Copy-Item $srcZip (Join-Path $DistDir "$AppName-源码.zip")
            Ok '源码包（git archive，已遵守 .gitignore）'
        }
    } else {
        Warn '不是 git 仓库，源码包跳过'
    }
}
finally {
    Pop-Location
}

# ---------- 4. 压缩 ----------
if (-not $SkipZip) {
    Step '压缩'

    # 注意：GitHub Release 的资产名不要用中文。
    # 实测 `gh release upload` 遇到 CJK 文件名会 404 或把名字截断成 `xxx-.zip`。
    # 本地目录名用中文没问题，只有上传用的 zip 名必须是 ASCII。
    $assetZipName = if ($AsciiZipNames) { "$AppName-assets.zip" } else { "$AppName-素材.zip" }
    if ($AsciiZipNames -and (Test-Path (Join-Path $DistDir "$AppName-源码.zip"))) {
        Move-Item (Join-Path $DistDir "$AppName-源码.zip") (Join-Path $DistDir "$AppName-source.zip") -Force
        Ok '源码包改名为 ASCII'
    }

    foreach ($pair in @(
            @{ Src = $RunDir;   Zip = "$AppName-win-x64.zip" },
            @{ Src = $AssetDir; Zip = $assetZipName })) {

        $target = Join-Path $DistDir $pair.Zip
        if (Test-Path $target) { Remove-Item $target -Force }
        Compress-Archive -Path (Join-Path $pair.Src '*') -DestinationPath $target -Force
        $kb = [math]::Round((Get-Item $target).Length / 1KB, 1)
        Ok "$($pair.Zip)  ($kb KB)"
    }
}

# ---------- 5. 泄漏自检 ----------
Step '泄漏自检'

# 这几个串出现在发布包里就是事故：开发机路径、作者账号
$forbidden = @('F:\DSH', '924050554', 'C:\Users\闫')
$violations = @()

Get-ChildItem $RunDir, $AssetDir -Recurse -File | ForEach-Object {
    if ($_.Extension -in '.exe', '.ico', '.png') { return }   # 二进制跳过
    $text = Get-Content $_.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if (-not $text) { return }
    foreach ($p in $forbidden) {
        if ($text -match [regex]::Escape($p)) {
            $violations += "$($_.FullName.Replace($DistDir + '\', ''))  命中「$p」"
        }
    }
}

if ($violations.Count -eq 0) {
    Ok '未发现开发机路径 / 作者账号泄漏'
} else {
    Write-Host '  发现泄漏：' -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    throw '发布包里有个人数据，已中止'
}

# ---------- 汇总 ----------
Write-Host ''
Write-Host '完成。dist\ 内容：' -ForegroundColor Green
Get-ChildItem $DistDir | ForEach-Object {
    if ($_.PSIsContainer) {
        $size = (Get-ChildItem $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum
        Write-Host ("  {0,-42} {1,8:N0} KB" -f ($_.Name + '\'), ($size / 1KB))
    } else {
        Write-Host ("  {0,-42} {1,8:N0} KB" -f $_.Name, ($_.Length / 1KB))
    }
}
