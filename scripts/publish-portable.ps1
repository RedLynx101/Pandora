param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\CustomFences.App\CustomFences.App.csproj"
$output = Join-Path $root "artifacts\CustomFences-$Runtime"
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --output $output `
    --self-contained:$selfContainedValue `
    -p:PublishSingleFile=false

Write-Host "Published CustomFences to $output"
