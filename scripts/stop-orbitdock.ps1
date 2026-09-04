# Compatibility entrypoint. Prefer stop-pandora.ps1 for new integrations.
& (Join-Path $PSScriptRoot "stop-pandora.ps1") @args
