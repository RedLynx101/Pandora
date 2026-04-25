param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishedExe = Join-Path $repoRoot "artifacts\CustomFences-win-x64\OrbitDock.Cli.exe"

if (Test-Path $publishedExe) {
    & $publishedExe @Arguments
    exit $LASTEXITCODE
}

& dotnet run --project (Join-Path $repoRoot "src\OrbitDock.Cli\OrbitDock.Cli.csproj") -- @Arguments
exit $LASTEXITCODE
