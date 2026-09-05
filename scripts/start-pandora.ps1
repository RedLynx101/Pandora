param(
    [switch]$Settings,
    [switch]$Restart,
    [switch]$FromPublish,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$running = @(Get-Process Pandora.App -ErrorAction SilentlyContinue)

if ($running.Count -gt 0 -and $Restart) {
    & (Join-Path $PSScriptRoot "stop-pandora.ps1")
    $running = @()
}

if ($running.Count -gt 0) {
    if ($Settings) {
        # Ask the running Pandora instance to open its settings.
        $signal = [System.Threading.EventWaitHandle]::OpenExisting("Pandora.ShowSettings")
        try { [void]$signal.Set() } finally { $signal.Dispose() }
        Write-Host "Opened Pandora settings in the running instance."
    } else {
        Write-Host "Pandora is already running. Use -Settings or -Restart."
    }
    return
}

if ($FromPublish) {
    $exe = Join-Path $repoPath "artifacts\Pandora-win-x64\Pandora.App.exe"
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        & (Join-Path $PSScriptRoot "publish-portable.ps1")
    }
} else {
    if (-not $NoBuild) {
        & dotnet build (Join-Path $repoPath "Pandora.sln")
        if ($LASTEXITCODE -ne 0) { throw "Pandora build failed with exit code $LASTEXITCODE." }
    }
    $exe = Join-Path $repoPath "src\Pandora.App\bin\Debug\net8.0-windows\Pandora.App.exe"
}

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "App executable was not found: $exe"
}

$launchOptions = @{ FilePath = $exe; WorkingDirectory = (Split-Path $exe); PassThru = $true }
if ($Settings) { $launchOptions.ArgumentList = "--settings" }
# This is the visible desktop app the user asked to launch, not a background helper.
$process = Start-Process @launchOptions
Write-Host "Started Pandora PID $($process.Id)"
