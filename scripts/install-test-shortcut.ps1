[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$SettingsShortcut,
    # Repair existing registration only. Enable startup explicitly in app settings.
    [switch]$StartupShortcut,
    [ValidateSet("Aperture", "Selene", "Aster")]
    [string]$IconStyle
)

$ErrorActionPreference = "Stop"
$repoPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$exe = Join-Path $repoPath "artifacts\Pandora-win-x64\Pandora.App.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    if ($WhatIfPreference) {
        Write-Host "What if: publish Pandora before creating shortcuts."
        return
    }
    & (Join-Path $PSScriptRoot "publish-portable.ps1")
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published app not found: $exe" }
}

$desktopPath = [Environment]::GetFolderPath("DesktopDirectory")
$startupPath = [Environment]::GetFolderPath("Startup")
$backupPath = Join-Path $repoPath ("artifacts\shortcut-backups\" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$shell = New-Object -ComObject WScript.Shell
$workingDirectory = Split-Path $exe
$workspacePath = Join-Path $env:USERPROFILE ".pandora\workspace.json"
if (-not $IconStyle -and (Test-Path -LiteralPath $workspacePath -PathType Leaf)) {
    try { $IconStyle = (Get-Content -LiteralPath $workspacePath -Raw | ConvertFrom-Json).settings.iconStyle }
    catch { Write-Warning "Could not read the saved icon choice; using Aperture." }
}
$iconStem = switch ($IconStyle) { "Selene" { "Pandora-Selene" } "Aster" { "Pandora-Aster" } default { "Pandora" } }
$iconPath = Join-Path $workingDirectory "Assets\Brand\$iconStem.ico"
$iconLocation = if (Test-Path -LiteralPath $iconPath) { "$iconPath,0" } else { "$exe,0" }

# Exact known paths only; a similarly named app elsewhere is not ours to replace.
$knownTargets = @($exe)
foreach ($configuration in @("Debug", "Release")) {
    $knownTargets += Join-Path $repoPath "src\Pandora.App\bin\$configuration\net8.0-windows\Pandora.App.exe"
}
$knownTargets = @($knownTargets | ForEach-Object { [IO.Path]::GetFullPath($_) })
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$approvedRunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
$approvedFolderKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"

function Get-RegistryValue {
    param([string]$Path, [string]$Name)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $key = Get-Item -LiteralPath $Path
    try { return ,($key.GetValue($Name, $null)) } finally { $key.Close() }
}

function Get-OwnedShortcut {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $link = $shell.CreateShortcut($Path)
    try {
        $target = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($link.TargetPath))
        if ($knownTargets -notcontains $target -or $link.Arguments -notin @("", "--settings")) {
            Write-Warning "Leaving unrelated shortcut untouched: $Path"
            return $null
        }
        return [PSCustomObject]@{ Path = $Path; TargetPath = $target; Arguments = $link.Arguments }
    } catch {
        Write-Warning "Leaving unreadable shortcut untouched: $Path"
        return $null
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
}

function Backup-Shortcut {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $location = if ((Split-Path $Path) -eq $startupPath) { "Startup" } else { "Desktop" }
    $destination = Join-Path $backupPath $location
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $Path -Destination (Join-Path $destination (Split-Path $Path -Leaf))
    Write-Host "Backed up $Path to $destination"
}

function Set-PandoraShortcut {
    param([string]$Path, [string]$Arguments = "", [string]$Description = "Launch Pandora desktop organizer")
    if ((Test-Path -LiteralPath $Path) -and -not (Get-OwnedShortcut $Path)) {
        throw "Refusing to replace an unrelated shortcut: $Path"
    }
    Backup-Shortcut $Path
    $link = $shell.CreateShortcut($Path)
    try {
        $link.TargetPath = $exe
        $link.Arguments = $Arguments
        $link.WorkingDirectory = $workingDirectory
        $link.IconLocation = $iconLocation
        $link.Description = $Description
        $link.Save()
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
    Write-Host "Created $Path"
}

try {
    $desktopShortcut = Join-Path $desktopPath "Pandora.lnk"
    if ($PSCmdlet.ShouldProcess($desktopShortcut, "Create Pandora shortcut and back up any recognized existing shortcut")) {
        Set-PandoraShortcut $desktopShortcut
    }
    if ($SettingsShortcut) {
        $settingsShortcutPath = Join-Path $desktopPath "Pandora Settings.lnk"
        if ($PSCmdlet.ShouldProcess($settingsShortcutPath, "Create settings shortcut and back up any recognized existing shortcut")) {
            Set-PandoraShortcut $settingsShortcutPath "--settings" "Open Pandora settings"
        }
    }

    if ($StartupShortcut) {
        $registrations = @()
        foreach ($name in @("Pandora")) {
            $linkPath = Join-Path $startupPath "$name.lnk"
            $link = Get-OwnedShortcut $linkPath
            if ($link -and $link.Arguments -eq "") {
                $registrations += [PSCustomObject]@{
                    Kind = "Shortcut"; Name = "$name.lnk"; Path = $linkPath
                    Command = $null; Approval = (Get-RegistryValue $approvedFolderKey "$name.lnk")
                }
            }
            $command = Get-RegistryValue $runKey $name
            if ($command -is [string]) {
                $target = $command.Trim().Trim('"')
                if ($knownTargets -contains $target) {
                    $registrations += [PSCustomObject]@{
                        Kind = "Run"; Name = $name; Path = $null
                        Command = $command; Approval = (Get-RegistryValue $approvedRunKey $name)
                    }
                } else { Write-Warning "Leaving an unrecognized $name startup command untouched." }
            }
        }

        if ($registrations.Count -eq 0) {
            Write-Host "No recognized existing startup registration. Startup remains unchanged; enable it in Pandora settings."
        } else {
            $startupShortcutPath = Join-Path $startupPath "Pandora.lnk"
            if ($PSCmdlet.ShouldProcess($startupShortcutPath, "Repair startup registration while preserving enabled/disabled state")) {
                # Do not reinterpret malformed metadata as an enabled default.
                foreach ($registration in $registrations) {
                    if ($null -ne $registration.Approval -and ($registration.Approval -isnot [byte[]] -or $registration.Approval.Length -lt 4)) {
                        throw "Unrecognized startup approval metadata. Registration was left unchanged; review it in Windows Startup Apps."
                    }
                }
                # Fail closed when registrations disagree: preserve a disabled/unknown state.
                $selected = $registrations | Where-Object { $_.Approval -is [byte[]] -and [BitConverter]::ToUInt32($_.Approval, 0) -notin @(2, 6) } | Select-Object -First 1
                if (-not $selected) { $selected = $registrations[0] }
                New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
                $registrations | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupPath "startup-registration.json") -Encoding UTF8
                Set-PandoraShortcut $startupShortcutPath
                if ($selected.Approval -is [byte[]]) {
                    New-Item -Path $approvedFolderKey -Force | Out-Null
                    New-ItemProperty -Path $approvedFolderKey -Name "Pandora.lnk" -PropertyType Binary -Value $selected.Approval -Force | Out-Null
                }
                foreach ($registration in $registrations) {
                    if ($registration.Kind -eq "Run") {
                        if ((Get-RegistryValue $runKey $registration.Name) -ne $registration.Command) {
                            throw "Startup registration changed during repair; left it untouched."
                        }
                        Remove-ItemProperty -Path $runKey -Name $registration.Name
                    }
                }
                Write-Host "Repaired Pandora startup without changing the existing approval state."
            }
        }
    }
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
