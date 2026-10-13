# dogdesk 封装：卸载 ZenDesktop 视觉效果（仅调用 Windhawk 官方 CLI）
$ErrorActionPreference = "Stop"

$candidates = @(
  (Join-Path $env:LOCALAPPDATA "Windhawk\windhawk.exe"),
  "windhawk.exe"
)
$wh = $candidates | Where-Object {
  if ($_ -like "*\*") { Test-Path $_ } else { $null -ne (Get-Command $_ -ErrorAction SilentlyContinue) }
} | Select-Object -First 1

if (-not $wh) {
  Write-Host "[dogdesk] 未检测到 Windhawk，无需卸载。"
  exit 0
}

Write-Host "[dogdesk] 使用 Windhawk: $wh"
try {
  & $wh mods uninstall "ZenDesktop"
  Write-Host "[dogdesk] 已尝试卸载 ZenDesktop mod。若 CLI 不支持，请在 Windhawk 界面手动移除。"
} catch {
  Write-Host "[dogdesk] 自动卸载失败：$($_.Exception.Message)"
}
