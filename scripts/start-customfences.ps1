param(
    [switch]$Settings,
    [switch]$Restart,
    [switch]$FromPublish,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appName = "CustomFences.App"

$running = Get-Process $appName -ErrorAction SilentlyContinue
if ($running -and $Restart) {
    $running | Stop-Process
    Start-Sleep -Milliseconds 500
    $running = $null
}

if ($running) {
    Write-Host "CustomFences is already running. Use -Restart to relaunch it."
    exit 0
}

if ($FromPublish) {
    $exe = Join-Path $root "artifacts\CustomFences-win-x64\CustomFences.App.exe"
    if (-not (Test-Path $exe)) {
        & (Join-Path $PSScriptRoot "publish-portable.ps1") | Write-Host
    }
} else {
    if (-not $NoBuild) {
        dotnet build (Join-Path $root "CustomFences.sln")
    }
    $exe = Join-Path $root "src\CustomFences.App\bin\Debug\net8.0-windows\CustomFences.App.exe"
}

if (-not (Test-Path $exe)) {
    throw "App executable was not found: $exe"
}

$arguments = @()
if ($Settings) {
    $arguments += "--settings"
}

if ($arguments.Count -gt 0) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe) -PassThru
} else {
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
}
Write-Host "Started CustomFences PID $($process.Id)"
