param(
    [string]$SourceDirectory = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Pandora'),
    [Parameter(Mandatory)][string]$BackupDirectory
)
$ErrorActionPreference = 'Stop'

# A packaged PowerShell may see a redirected AppData view, even for an absolute path.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PandoraMigrationPackage {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    public static extern int GetCurrentPackageFullName(ref uint length, System.Text.StringBuilder name);
}
'@
[uint32]$packageLength = 0
if ([PandoraMigrationPackage]::GetCurrentPackageFullName([ref]$packageLength, $null) -ne 15700) {
    throw 'Run migration from an unpackaged PowerShell (for example Windows Start), not a packaged terminal or Codex child process.'
}
if (Get-Process -Name 'Pandora.App' -ErrorAction SilentlyContinue) { throw 'Close Pandora and its startup supervisor before migration.' }
$source = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
$target = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.pandora'
$backup = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
if (-not [IO.Path]::IsPathRooted($SourceDirectory) -or -not [IO.Path]::IsPathRooted($BackupDirectory)) { throw 'Use absolute paths.' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'workspace.json'))) { throw 'Source workspace.json is missing.' }
if (Test-Path -LiteralPath $target) { throw "Destination already exists; refusing to merge or overwrite $target." }
if (Test-Path -LiteralPath $backup) { throw 'Backup directory must be new.' }
if ($backup.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or $backup -eq $source -or
    $backup.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or $backup -eq $target) { throw 'Backup must be outside source and destination.' }

# Enumerate one directory at a time so junctions cannot silently escape the selected tree.
$pending = [Collections.Generic.Stack[string]]::new()
$pending.Push($source)
$files = [Collections.Generic.List[string]]::new()
while ($pending.Count -gt 0) {
    $directory = $pending.Pop()
    if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked directory: $directory" }
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked item: $($item.FullName)" }
        if ($item.PSIsContainer) { $pending.Push($item.FullName) } else { $files.Add($item.FullName) }
    }
}
$null = Get-Content -LiteralPath (Join-Path $source 'workspace.json') -Raw | ConvertFrom-Json
$snapshot = Join-Path $backup 'original'
$prepared = Join-Path $backup 'prepared'
New-Item -ItemType Directory -Path $snapshot, $prepared | Out-Null
$manifest = foreach ($file in $files) {
    $relative = $file.Substring($source.Length + 1)
    foreach ($copyRoot in @($snapshot, $prepared)) {
        $copy = Join-Path $copyRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $copy) -Force | Out-Null
        Copy-Item -LiteralPath $file -Destination $copy
        if ((Get-FileHash -LiteralPath $file).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) { throw "Copy verification failed: $relative" }
    }
    [pscustomobject]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $file).Hash }
}

# Rebase only internal workspace references whose copied target actually exists.
# External dashboards/music/library paths and all layout values remain untouched.
$script:rebased = [Collections.Generic.List[string]]::new()
function Rebase-Value($value) {
    if ($value -is [string]) {
        $expanded = [Environment]::ExpandEnvironmentVariables($value)
        if ($expanded.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
            $relative = $expanded.Substring($source.Length + 1)
            if (Test-Path -LiteralPath (Join-Path $prepared $relative)) {
                $script:rebased.Add($value)
                return (Join-Path $target $relative)
            }
        }
        return $value
    }
    if ($value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $value.PSObject.Properties) { $property.Value = Rebase-Value $property.Value }
    } elseif ($value -is [array]) {
        for ($i=0; $i -lt $value.Count; $i++) { $value[$i] = Rebase-Value $value[$i] }
        return ,$value
    }
    return $value
}
$workspaceFile = Join-Path $prepared 'workspace.json'
$workspace = Get-Content -LiteralPath $workspaceFile -Raw | ConvertFrom-Json
$workspace = Rebase-Value $workspace
if ($script:rebased.Count -gt 0) { $workspace | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $workspaceFile -Encoding UTF8 }
@{ source=$source; destination=$target; files=@($manifest); rebasedReferences=@($script:rebased); at=[DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8

# Exact new directory only; no legacy delete, merge, or overwrite. Recheck at commit time.
if (Test-Path -LiteralPath $target) { throw 'Destination appeared during migration; leaving prepared copy in backup.' }
if ((Resolve-Path -LiteralPath $prepared).Path -ne (Join-Path ([IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')) 'prepared')) { throw 'Prepared path changed.' }
Move-Item -LiteralPath $prepared -Destination $target
Write-Output "Migrated $($files.Count) files to $target. Original data retained at $source and $snapshot."
