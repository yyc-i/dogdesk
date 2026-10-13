# dogdesk 封装：应用 ZenDesktop 视觉效果（仅调用 Windhawk 官方 CLI，不复制其 mod 代码）
$ErrorActionPreference = "Stop"

$candidates = @(
  (Join-Path $env:LOCALAPPDATA "Windhawk\windhawk.exe"),
  "windhawk.exe"
)
$wh = $candidates | Where-Object {
  if ($_ -like "*\*") { Test-Path $_ } else { $null -ne (Get-Command $_ -ErrorAction SilentlyContinue) }
} | Select-Object -First 1

if (-not $wh) {
  Write-Host "[dogdesk] 未检测到 Windhawk。请先到 https://windhawk.net 安装 Windhawk，再回来应用。"
  exit 0
}

Write-Host "[dogdesk] 使用 Windhawk: $wh"
try {
  # 探测 CLI 能力：常见为 `windhawk.exe mods install <id>`；不同版本参数略有差异，失败时友好提示。
  & $wh mods install "ZenDesktop"
  Write-Host "[dogdesk] 已尝试安装 ZenDesktop mod。若 CLI 不支持自动安装，请在 Windhawk 界面手动搜索安装。"
} catch {
  Write-Host "[dogdesk] 自动安装失败（版本/参数差异）：$($_.Exception.Message)"
  Write-Host "[dogdesk] 请打开 Windhawk，在界面搜索 ZenDesktop 并安装。"
}
