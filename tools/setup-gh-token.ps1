#Requires -Version 5.1
<#
.SYNOPSIS
    用剪贴板里的 GitHub 令牌完成 gh 登录（自动走本地代理并验证）。

.DESCRIPTION
    1. 强制设置本地代理环境变量（Clash 默认混合端口 7890）
    2. 从剪贴板读取令牌（不经过命令行历史，不用粘进对话框）
    3. 调用 gh auth login --with-token 登录
    4. 立刻调用 gh auth status 验证

    用法：
      先把令牌复制到剪贴板，然后在 PowerShell 里执行：
        .\setup-gh-token.ps1

      或显式指定代理端口：
        .\setup-gh-token.ps1 -ProxyPort 7891
#>
[CmdletBinding()]
param(
    [int]$ProxyPort = 7890,
    [string]$Token,
    [switch]$UseHttpProxy
)

$ErrorActionPreference = 'Stop'

Write-Host '=== GitHub CLI 令牌登录 ===' -ForegroundColor Cyan

# ---------- 1. 代理 ----------
# 实测结论：本机 Clash 的系统代理 / TUN 模式已经覆盖 GitHub，
# 再额外设置 HTTPS_PROXY 反而会让稳定性从 6/6 掉到 1/4。
# 所以默认「不设置」代理环境变量，只在显式加 -UseHttpProxy 时才设。
$proxy = "http://127.0.0.1:$ProxyPort"
Write-Host ''
Write-Host '[1/4] 代理检查'

if ($UseHttpProxy) {
    $proxyAlive = (Test-NetConnection -ComputerName 127.0.0.1 -Port $ProxyPort `
            -InformationLevel Quiet -WarningAction SilentlyContinue)
    if (-not $proxyAlive) {
        Write-Host "      端口 $ProxyPort 没开，无法启用显式代理。" -ForegroundColor Red
        exit 1
    }

    $env:HTTPS_PROXY = $proxy
    $env:HTTP_PROXY = $proxy
    Write-Host "      已启用显式代理 $proxy（仅本进程）" -ForegroundColor Yellow
}
else {
    # 清掉本进程里可能残留的代理变量，让流量走系统代理
    $env:HTTPS_PROXY = $null
    $env:HTTP_PROXY = $null
    Write-Host '      不设置代理环境变量，走系统代理 / TUN（实测最稳）' -ForegroundColor Green
}

# 顺手把之前可能写错的用户级代理变量清掉
$userProxy = [Environment]::GetEnvironmentVariable('HTTPS_PROXY', 'User')
if ($userProxy) {
    [Environment]::SetEnvironmentVariable('HTTPS_PROXY', $null, 'User')
    [Environment]::SetEnvironmentVariable('HTTP_PROXY', $null, 'User')
    Write-Host '      已清除用户级 HTTPS_PROXY / HTTP_PROXY（它会拖慢稳定性）'
}

# ---------- 2. 取令牌 ----------
Write-Host ''
Write-Host '[2/4] 读取令牌'

if (-not $Token) {
    try { $Token = Get-Clipboard -Raw } catch { }
}

if (-not $Token) {
    Write-Host '      剪贴板是空的。' -ForegroundColor Red
    Write-Host '      请先在浏览器里生成并复制令牌（ghp_ 开头的字符串），再运行本脚本。' -ForegroundColor Yellow
    Write-Host '      提示：先复制令牌，再敲命令 —— 反过来会覆盖剪贴板。' -ForegroundColor Yellow
    exit 1
}

# 剪贴板里常常混着别的文字（命令行、换行、说明）。
# 优先用正则从整块文本里把令牌抠出来，而不是整坨当令牌用。
$raw = $Token
$Token = ''

foreach ($pattern in @('github_pat_[A-Za-z0-9_]{20,}', 'gh[opsur]_[A-Za-z0-9]{20,}')) {
    $match = [regex]::Match($raw, $pattern)
    if ($match.Success) { $Token = $match.Value; break }
}

if (-not $Token) {
    # 退化处理：去掉空白字符后整体尝试
    $stripped = ($raw -replace '\s', '')
    if ($stripped -match '^(gh[opsur]_|github_pat_)') { $Token = $stripped }
}

if (-not $Token) {
    Write-Host '      剪贴板内容里找不到 GitHub 令牌。' -ForegroundColor Red
    Write-Host ("      剪贴板前 60 个字符：" + $raw.Substring(0, [Math]::Min(60, $raw.Length))) -ForegroundColor Yellow
    Write-Host '      请重新复制令牌本身（形如 ghp_xxxxxxxxxxxxxxxx），再运行本脚本。' -ForegroundColor Yellow
    Write-Host '      注意：复制令牌后不要再用鼠标选中任何文字，否则剪贴板会被覆盖。' -ForegroundColor Yellow
    exit 1
}

Write-Host ("      从剪贴板提取到令牌，共 " + $Token.Length + " 个字符，前缀 " + $Token.Substring(0, [Math]::Min(11, $Token.Length)) + '…')

# ---------- 3. 登录 ----------
Write-Host ''
Write-Host '[3/4] 登录'

$Token | gh auth login --hostname github.com --git-protocol https --with-token 2>&1 | ForEach-Object {
    Write-Host ('      ' + $_)
}
$loginExit = $LASTEXITCODE

if ($loginExit -ne 0) {
    Write-Host "      登录失败（退出码 $loginExit）" -ForegroundColor Red
    Write-Host '      常见原因：令牌已撤销 / 令牌无 repo 权限 / 代理未生效。' -ForegroundColor Yellow
    exit $loginExit
}
Write-Host '      登录成功' -ForegroundColor Green

# ---------- 4. 验证 ----------
Write-Host ''
Write-Host '[4/4] 验证'

# 注意：gh auth status 会把账号信息写到 stderr，这是它的正常行为。
# 在本脚本的 $ErrorActionPreference='Stop' 下会被 PowerShell 当成致命错误抛出来，
# 所以这里临时放开，并且完全靠退出码判断成败。
$oldPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$statusText = (& gh auth status 2>&1 | Out-String) -replace '^\s*gh\s*:\s*', ''
$statusExit = $LASTEXITCODE
$ErrorActionPreference = $oldPreference

foreach ($line in ($statusText -split "`r?`n")) {
    if ($line.Trim()) { Write-Host ('      ' + $line.Trim()) }
}

Write-Host ''
if ($statusExit -eq 0) {
    Write-Host '完成。gh 已可用。' -ForegroundColor Green

    # 权限检查：token scopes 为 none 时能登录但无法建仓库 / 推送
    if ($statusText -match 'Token scopes:\s*none') {
        Write-Host ''
        Write-Host '⚠ 警告：令牌没有任何权限（scopes: none）。' -ForegroundColor Yellow
        Write-Host '  可以读取公开数据、可以登录，但**无法创建仓库、无法推送代码**。' -ForegroundColor Yellow
        Write-Host '  请重新生成令牌并勾选 repo / workflow / read:org，然后重跑本脚本。' -ForegroundColor Yellow
        exit 4
    }

    Write-Host ''
    Write-Host '提示：本机 git / gh 走系统代理即可，不要额外设 http.proxy。' -ForegroundColor Cyan
}
else {
    Write-Host '登录命令返回成功，但验证失败（退出码 ' -NoNewline -ForegroundColor Red
    Write-Host "$statusExit" -NoNewline -ForegroundColor Red
    Write-Host '），请把上面的输出发出来。' -ForegroundColor Red
    exit $statusExit
}
