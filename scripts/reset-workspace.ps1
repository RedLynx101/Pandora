param(
    [switch]$NoBackup
)

$ErrorActionPreference = "Stop"
$workspace = Join-Path $env:APPDATA "OrbitDock\workspace.json"
$legacyWorkspace = Join-Path $env:APPDATA "CustomFences\workspace.json"

& (Join-Path $PSScriptRoot "stop-pandora.ps1")

if (-not (Test-Path $workspace)) {
    Write-Host "No workspace file exists yet."
    if (Test-Path $legacyWorkspace) {
        Write-Host "Legacy CustomFences workspace still exists at $legacyWorkspace"
    }
    exit 0
}

if (-not $NoBackup) {
    $backup = "$workspace.$((Get-Date).ToString('yyyyMMdd-HHmmss')).bak"
    Copy-Item -LiteralPath $workspace -Destination $backup
    Write-Host "Backed up workspace to $backup"
}

Remove-Item -LiteralPath $workspace
Write-Host "Removed workspace. It will be recreated on next launch."
