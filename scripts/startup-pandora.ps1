[CmdletBinding()]
param(
    [ValidateSet('Install', 'Status', 'Start', 'Stop', 'Enable', 'Disable')]
    [string]$Mode = 'Status'
)

# Windows PowerShell 5.1 compatible. Run as the desktop user, never SYSTEM.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\Pandora-win-x64\Pandora.App.exe'))
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskName = 'Pandora-' + $sid
$description = 'Pandora managed sign-in and crash recovery v1'
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$task = $null
try { $task = $folder.GetTask($taskName) }
catch {
    if ($_.Exception.GetBaseException().HResult -ne -2147024894) { throw }
}

function Assert-OwnedTask($candidate) {
    [xml]$definition = $candidate.Xml
    $actions = @($definition.Task.Actions.ChildNodes)
    $principals = @($definition.Task.Principals.ChildNodes)
    if ($definition.Task.RegistrationInfo.Description -ne $description -or
        $actions.Count -ne 1 -or $actions[0].LocalName -ne 'Exec' -or
        [IO.Path]::GetFullPath([string]$actions[0].Command) -ne $exe -or
        ([string]$actions[0].Arguments -cne '--supervise' -and -not ($Mode -eq 'Install' -and [string]$actions[0].Arguments -ceq '--scheduled')) -or
        $principals.Count -ne 1 -or [string]$principals[0].UserId -ne $sid -or
        [string]$principals[0].LogonType -ne 'InteractiveToken' -or
        [string]$principals[0].RunLevel -notin @('', 'LeastPrivilege')) {
        throw "Task '$taskName' is not owned by this installation. Nothing changed."
    }
}

if ($task) { Assert-OwnedTask $task }
if ($Mode -eq 'Install') {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Publish Pandora first.' }
    # Validate ALL legacy entries before mutating anything. Backups survive updates.
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Pandora.lnk'
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $run = (Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).Pandora
    if ($null -ne $run -and ([string]$run).Trim() -notin @($exe, ('"' + $exe + '"'))) {
        throw 'The Pandora Run entry belongs to another installation. Nothing changed.'
    }
    if (Test-Path -LiteralPath $shortcutPath) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ([IO.Path]::GetFullPath($shortcut.TargetPath) -ne $exe -or $shortcut.Arguments) {
            throw 'The Pandora startup shortcut belongs to another installation. Nothing changed.'
        }
    }
    $backup = Join-Path $root ('artifacts\startup-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $backup | Out-Null
    if ($task) { [IO.File]::WriteAllText((Join-Path $backup 'previous-task.xml'), $task.Xml) }
    if (Test-Path -LiteralPath $shortcutPath) { Copy-Item -LiteralPath $shortcutPath -Destination $backup }
    if ($null -ne $run) { [IO.File]::WriteAllText((Join-Path $backup 'previous-run.txt'), [string]$run) }

    $definition = $scheduler.NewTask(0)
    $definition.RegistrationInfo.Description = $description
    $definition.Principal.UserId = $sid
    $definition.Principal.LogonType = 3 # interactive token; no password stored
    $definition.Principal.RunLevel = 0 # least privilege
    $trigger = $definition.Triggers.Create(9) # current-user logon
    $trigger.UserId = $sid
    $trigger.Delay = 'PT15S'
    $action = $definition.Actions.Create(0)
    $action.Path = $exe
    $action.Arguments = '--supervise'
    $action.WorkingDirectory = Split-Path -Parent $exe
    $definition.Settings.Enabled = $true
    $definition.Settings.AllowDemandStart = $true
    $definition.Settings.StartWhenAvailable = $true
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.ExecutionTimeLimit = 'PT0S'
    $definition.Settings.MultipleInstances = 2 # IgnoreNew
    # The headless parent checks real child exit codes and enforces the 1m/3
    # budget. Native RestartOnFailure did not recover a force-ended app in live
    # testing; do not nest restart budgets or revive an intentionally stopped app.
    $definition.Settings.Priority = 6
    # Register/read back before removing the old owned launch path. No reboot.
    $task = $folder.RegisterTaskDefinition($taskName, $definition, 6, $sid, $null, 3)
    Assert-OwnedTask $task
    [IO.File]::WriteAllText((Join-Path $backup 'installed-task.xml'), $task.Xml)
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
    if ($null -ne $run) { Remove-ItemProperty -LiteralPath $runKey -Name Pandora }
    Write-Host "Installed $taskName. Startup backup: $backup"
}
elseif ($Mode -ne 'Status' -and -not $task) {
    throw 'No managed task exists. Run this script with -Mode Install first.'
}
switch ($Mode) {
    'Enable' { $task.Enabled = $true }
    'Disable' { $task.Enabled = $false } # leaves current app running
    'Start' {
        if (-not $task.Enabled) { throw 'Startup is disabled. Use -Mode Enable first, or launch Pandora directly without recovery.' }
        $null = $task.Run($null)
    }
    'Stop' {
        # Graceful exit preserves layout and returns 0 (no crash retry).
        $supervisorSignal = $null
        if ([Threading.EventWaitHandle]::TryOpenExisting('Pandora.SupervisorStop', [ref]$supervisorSignal)) {
            try { $null = $supervisorSignal.Set() } finally { $supervisorSignal.Dispose() }
        }
        $exitSignal = $null
        if ([Threading.EventWaitHandle]::TryOpenExisting('Pandora.Exit', [ref]$exitSignal)) {
            try { $null = $exitSignal.Set() } finally { $exitSignal.Dispose() }
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $running = @(Get-Process Pandora.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
            if (-not $running.Count) { break }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($running.Count) { throw 'Pandora did not exit gracefully; no process was forcibly killed.' }
        $task.Stop(0) # cancels any pending crash retry; keeps next sign-in enabled
    }
}
if ($task) {
    $task = $folder.GetTask($taskName)
    $recovery = $null
    try {
        $recoveryPath = Join-Path (Split-Path -Parent $exe) 'Diagnostics\startup-recovery.json'
        if (Test-Path -LiteralPath $recoveryPath) { $recovery = Get-Content -LiteralPath $recoveryPath -Raw | ConvertFrom-Json }
    } catch { Write-Verbose 'Recovery status is temporarily unavailable.' }
    [pscustomobject]@{ TaskName = $taskName; Enabled = $task.Enabled; State = $task.State;
        LastRunTime = $task.LastRunTime; LastTaskResult = $task.LastTaskResult; Executable = $exe;
        RecoveryState = $recovery.state; RecoveryStatusAt = $recovery.at;
        DesktopProcessId = $recovery.childPid; RetriesUsed = $recovery.retriesUsed }
} else { Write-Host 'Pandora has no managed startup task.' }
