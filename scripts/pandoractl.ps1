param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"
$repoPath = Split-Path -Parent $PSScriptRoot
$publishedExe = Join-Path $repoPath "artifacts\Pandora-win-x64\Pandora.Cli.exe"

if (Test-Path -LiteralPath $publishedExe -PathType Leaf) {
    & $publishedExe @Arguments
    exit $LASTEXITCODE
}

& dotnet run --project (Join-Path $repoPath "src\Pandora.Cli\Pandora.Cli.csproj") -- @Arguments
exit $LASTEXITCODE
