$ErrorActionPreference = "Stop"

# Include the legacy binary during migration; never stop processes by a wildcard.
$processes = @(Get-Process Pandora.App, OrbitDock.App -ErrorAction SilentlyContinue)
& (Join-Path $PSScriptRoot "show-desktop-icons.ps1")
if ($processes.Count -eq 0) {
    Write-Host "Pandora is not running."
    return
}

foreach ($process in $processes) {
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(2000)) {
        Stop-Process -Id $process.Id -ErrorAction Stop
        $process.WaitForExit()
    }
}
Write-Host "Stopped Pandora (including any legacy OrbitDock instance)."
