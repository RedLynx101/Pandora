param(
    [switch]$NoBackup
)

$ErrorActionPreference = "Stop"
$workspace = Join-Path $env:APPDATA "Pandora\workspace.json"

& (Join-Path $PSScriptRoot "stop-pandora.ps1")

if (-not (Test-Path $workspace)) {
    Write-Host "No workspace file exists yet."
    exit 0
}

if (-not $NoBackup) {
    $backup = "$workspace.$((Get-Date).ToString('yyyyMMdd-HHmmss')).bak"
    Copy-Item -LiteralPath $workspace -Destination $backup
    Write-Host "Backed up workspace to $backup"
}

Remove-Item -LiteralPath $workspace
Write-Host "Removed workspace. It will be recreated on next launch."
