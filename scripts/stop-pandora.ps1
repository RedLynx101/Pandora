$ErrorActionPreference = "Stop"

# A managed installation needs the scheduler's pending restart canceled as well.
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$task = $null
try { $task = $scheduler.GetFolder('\').GetTask('Pandora-' + $sid) }
catch { if ($_.Exception.GetBaseException().HResult -ne -2147024894) { throw } }
if ($task) {
    & (Join-Path $PSScriptRoot 'startup-pandora.ps1') -Mode Stop
    return
}

# Match Pandora's exact process name; never stop processes by a wildcard.
$processes = @(Get-Process Pandora.App -ErrorAction SilentlyContinue)
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
Write-Host "Stopped Pandora."
