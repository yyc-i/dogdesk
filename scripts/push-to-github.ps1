<#
.SYNOPSIS
  将 MediaBarWallpaper 一键推送到你的 GitHub（创建远程仓库 + 首次推送）。
  在本项目根目录（MediaBarWallpaper/）下，用 PowerShell 运行。

.方式一（推荐：已安装 GitHub CLI gh）
  1) winget install --id GitHub.cli      # 若未安装 gh
  2) gh auth login                       # 浏览器登录
  3) .\scripts\push-to-github.ps1 -Username 你的用户名

.方式二（无 gh，用 Personal Access Token）
  先在 github.com -> Settings -> Developer settings -> Personal access tokens
  生成一个勾选 repo 权限的 token，然后：
  .\scripts\push-to-github.ps1 -Username 你的用户名 -Token ghp_xxxxx -Visibility public
  推送完成后请立即回到 GitHub 撤销该 token。
#>
param(
    [Parameter(Mandatory = $true)][string]$Username,
    [string]$Token = "",
    [ValidateSet("public", "private")][string]$Visibility = "public",
    [string]$RepoName = "MediaBarWallpaper"
)

$ErrorActionPreference = "Stop"
function Assert-Ok($msg) { if ($LASTEXITCODE -ne 0) { throw $msg } }

# 确保在仓库根（包含 src 目录）
if (-not (Test-Path "src")) {
    throw "请在项目根目录（含 src 的那一层）运行本脚本。"
}

# 提交身份（仅本仓库）
if (-not (git config user.name)) { git config user.name  $Username | Out-Null }
if (-not (git config user.email)) { git config user.email "$Username@users.noreply.github.com" | Out-Null }

git add -A
git commit -m "MediaBarWallpaper v0.3.0" 2>$null
git branch -M main

$repoUrl = "https://github.com/$Username/$RepoName.git"

# ---- 方式一：gh CLI 已登录 ----
$ghReady = $false
if (Get-Command gh -ErrorAction SilentlyContinue) {
    gh auth status 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $ghReady = $true }
}

if ($ghReady) {
    git remote remove origin 2>$null
    gh repo create $RepoName --$Visibility --source=. --remote=origin --push
    Assert-Ok "gh 创建/推送失败"
    Write-Host "完成：$repoUrl" -ForegroundColor Green
    return
}

# ---- 方式二：Token ----
if ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Host "未检测到 gh 登录，也未提供 -Token。" -ForegroundColor Yellow
    Write-Host "请先在 GitHub 手动新建一个【空】仓库（名 $RepoName，不要勾选 README/.gitignore），然后执行："
    Write-Host "  git remote add origin $repoUrl"
    Write-Host "  git push -u origin main"
    return
}

# 用 API 创建仓库
$headers = @{ Authorization = "token $Token"; Accept = "application/vnd.github+json" }
$body = @{ name = $RepoName; private = ($Visibility -eq "private") } | ConvertTo-Json
try {
    Invoke-RestMethod -Headers $headers -Method Post -Uri "https://api.github.com/user/repos" -Body $body | Out-Null
} catch {
    $st = $_.Exception.Response.StatusCode.value__
    if ($st -eq 422) { Write-Host "仓库可能已存在，继续推送…" -ForegroundColor Yellow }
    else { throw "创建仓库失败（HTTP $st）：$($_.Exception.Message)" }
}

# 推送（token 仅用于本次，推送后把 remote 改回不含 token 的干净地址）
git remote remove origin 2>$null
git remote add origin "https://${Username}:$Token@github.com/$Username/$RepoName.git"
git push -u origin main
Assert-Ok "推送失败，请检查用户名/token 的 repo 权限"
git remote set-url origin $repoUrl

Write-Host "完成：$repoUrl（请尽快撤销所用 token）" -ForegroundColor Green
