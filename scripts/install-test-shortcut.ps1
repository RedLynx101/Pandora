param(
    [switch]$SettingsShortcut,
    [switch]$StartupShortcut
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$exe = Join-Path $root "artifacts\OrbitDock-win-x64\OrbitDock.App.exe"

if (-not (Test-Path $exe)) {
    & (Join-Path $PSScriptRoot "publish-portable.ps1") | Write-Host
}

$desktop = [Environment]::GetFolderPath("DesktopDirectory")
$startup = [Environment]::GetFolderPath("Startup")
$legacyShortcutNames = @("CustomFences.lnk", "CustomFences Test.lnk", "CustomFences Settings.lnk")
foreach ($name in $legacyShortcutNames) {
    Remove-Item -LiteralPath (Join-Path $desktop $name) -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $startup $name) -ErrorAction SilentlyContinue
}

$shell = New-Object -ComObject WScript.Shell
$workingDirectory = Split-Path $exe
$iconPath = Join-Path $workingDirectory "Assets\Brand\OrbitDock.ico"
$iconLocation = if (Test-Path -LiteralPath $iconPath) { "$iconPath,0" } else { "$exe,0" }

function Set-OrbitDockShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Arguments = "",
        [string]$Description = "Launch OrbitDock desktop organizer"
    )

    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $exe
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $workingDirectory
    $shortcut.IconLocation = $iconLocation
    $shortcut.Description = $Description
    $shortcut.Save()
    Write-Host "Created $Path"
}

$shortcutPath = Join-Path $desktop "OrbitDock.lnk"
Set-OrbitDockShortcut -Path $shortcutPath

if ($SettingsShortcut) {
    $settingsShortcutPath = Join-Path $desktop "OrbitDock Settings.lnk"
    Set-OrbitDockShortcut -Path $settingsShortcutPath -Arguments "--settings" -Description "Open OrbitDock settings"
}

if ($StartupShortcut) {
    $startupShortcutPath = Join-Path $startup "OrbitDock.lnk"
    Set-OrbitDockShortcut -Path $startupShortcutPath

    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $startupApprovedRunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
    $startupApprovedFolderKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
    foreach ($valueName in @("OrbitDock", "CustomFences")) {
        Remove-ItemProperty -Path $runKey -Name $valueName -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $startupApprovedRunKey -Name $valueName -ErrorAction SilentlyContinue
    }

    New-Item -Path $startupApprovedFolderKey -Force | Out-Null
    New-ItemProperty -Path $startupApprovedFolderKey -Name "OrbitDock.lnk" -PropertyType Binary -Value ([byte[]](0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00)) -Force | Out-Null
    Write-Host "Registered OrbitDock startup shortcut"
}
