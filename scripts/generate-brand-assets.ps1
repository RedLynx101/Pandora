# Compatibility entrypoint. Prefer generate-pandora-brand.ps1 for new integrations.
& (Join-Path $PSScriptRoot "generate-pandora-brand.ps1") @args
