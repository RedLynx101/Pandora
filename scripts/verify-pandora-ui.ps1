[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
if (-not [System.IO.Path]::IsPathFullyQualified($EvidenceDirectory)) {
    throw 'EvidenceDirectory must be an explicit absolute path. The harness creates an isolated child directory per run.'
}
$verificationProject = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/Pandora.App.Tests/Pandora.App.Tests.csproj'))
& dotnet run --project $verificationProject -c Release -- --output $EvidenceDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Pandora WPF verification failed with exit code $LASTEXITCODE. See the evidence report printed above."
}
