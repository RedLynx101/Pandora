$ErrorActionPreference = "Stop"

$processes = Get-Process CustomFences.App -ErrorAction SilentlyContinue
if (-not $processes) {
    & (Join-Path $PSScriptRoot "show-desktop-icons.ps1") | Write-Host
    Write-Host "CustomFences is not running."
    exit 0
}

& (Join-Path $PSScriptRoot "show-desktop-icons.ps1") | Write-Host
$processes | Stop-Process
Write-Host "Stopped CustomFences."
