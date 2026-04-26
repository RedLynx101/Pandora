$ErrorActionPreference = "Stop"

$processes = Get-Process OrbitDock.App -ErrorAction SilentlyContinue
if (-not $processes) {
    & (Join-Path $PSScriptRoot "show-desktop-icons.ps1") | Write-Host
    Write-Host "OrbitDock is not running."
    exit 0
}

& (Join-Path $PSScriptRoot "show-desktop-icons.ps1") | Write-Host
$processes | Stop-Process
Write-Host "Stopped OrbitDock."
