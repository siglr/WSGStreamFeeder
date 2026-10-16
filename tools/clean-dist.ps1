param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$KeepVersion, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$dist = [IO.Path]::GetFullPath((Join-Path $project 'dist'))

function Assert-PlainTree([string]$path) {
    $item = Get-Item -LiteralPath $path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked cleanup path refused: $path" }
    if ($item.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $path -Force) { Assert-PlainTree $child.FullName }
    }
}

# Never discard the previous build until the current release and upload ZIP verify.
& (Join-Path $PSScriptRoot 'verify-release.ps1') -PackageDirectory (Join-Path $dist ('release-' + $KeepVersion)) -UploadDirectory (Join-Path $dist 'upload')
$feed = Get-Content -Raw -LiteralPath (Join-Path $dist 'upload\WSG-Stream-Feeder.version.json') | ConvertFrom-Json
if ($feed.version -ne $KeepVersion) { throw 'Cleanup version does not match the upload package.' }
$distItem = Get-Item -LiteralPath $dist -Force
if ($distItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked dist directory refused.' }
$targets = @()
foreach ($item in Get-ChildItem -LiteralPath $dist -Directory -Force) {
    $remove = $item.Name -in @('MSFSOverlayFeeder', 'WSG Stream Feeder', 'updater')
    if ($item.Name -match '^release-(\d+\.\d+\.\d+)$') { $remove = [version]$Matches[1] -lt [version]$KeepVersion }
    if ($item.Name -match '^upload-(\d+\.\d+\.\d+)$') { $remove = [version]$Matches[1] -le [version]$KeepVersion }
    if (-not $remove) { continue }
    $resolved = (Resolve-Path -LiteralPath $item.FullName).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne $dist) { throw "Cleanup target leaves dist: $resolved" }
    Assert-PlainTree $resolved
    $targets += $resolved
}
# Validate every target before the first deletion. Unknown folders and newer releases are preserved.
foreach ($target in $targets) {
    if ($Preview) { Write-Host "Would remove: $target" }
    else { Remove-Item -LiteralPath $target -Recurse -Force; Write-Host "Removed: $target" }
}
