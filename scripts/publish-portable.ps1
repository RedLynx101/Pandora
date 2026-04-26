param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\OrbitDock.App\OrbitDock.App.csproj"
$cliProject = Join-Path $root "src\OrbitDock.Cli\OrbitDock.Cli.csproj"
$output = Join-Path $root "artifacts\OrbitDock-$Runtime"
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --output $output `
    --self-contained:$selfContainedValue `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "App publish failed with exit code $LASTEXITCODE. Stop OrbitDock before publishing if files are locked."
}

dotnet publish $cliProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --output $output `
    --self-contained:$selfContainedValue `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "CLI publish failed with exit code $LASTEXITCODE."
}

Write-Host "Published OrbitDock to $output"
