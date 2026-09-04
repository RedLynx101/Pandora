param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)
# Compatibility entrypoint. Prefer pandoractl.ps1 for new integrations.
& (Join-Path $PSScriptRoot "pandoractl.ps1") @Arguments
exit $LASTEXITCODE
