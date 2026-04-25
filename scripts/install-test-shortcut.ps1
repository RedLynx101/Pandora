param(
    [switch]$SettingsShortcut
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$exe = Join-Path $root "artifacts\CustomFences-win-x64\CustomFences.App.exe"

if (-not (Test-Path $exe)) {
    & (Join-Path $PSScriptRoot "publish-portable.ps1") | Write-Host
}

$desktop = [Environment]::GetFolderPath("DesktopDirectory")
Remove-Item -LiteralPath (Join-Path $desktop "CustomFences Test.lnk") -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $desktop "CustomFences Settings.lnk") -ErrorAction SilentlyContinue

$shortcutPath = Join-Path $desktop "OrbitDock.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = Split-Path $exe
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = "Launch OrbitDock desktop organizer"
$shortcut.Save()
Write-Host "Created $shortcutPath"

if ($SettingsShortcut) {
    $settingsShortcutPath = Join-Path $desktop "OrbitDock Settings.lnk"
    $settingsLink = $shell.CreateShortcut($settingsShortcutPath)
    $settingsLink.TargetPath = $exe
    $settingsLink.Arguments = "--settings"
    $settingsLink.WorkingDirectory = Split-Path $exe
    $settingsLink.IconLocation = "$exe,0"
    $settingsLink.Description = "Open OrbitDock settings"
    $settingsLink.Save()
    Write-Host "Created $settingsShortcutPath"
}
