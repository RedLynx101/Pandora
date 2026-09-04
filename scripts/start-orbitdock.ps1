param(
    [switch]$Settings,
    [switch]$Restart,
    [switch]$FromPublish,
    [switch]$NoBuild
)
# Compatibility entrypoint. Prefer start-pandora.ps1 for new integrations.
& (Join-Path $PSScriptRoot "start-pandora.ps1") @PSBoundParameters
