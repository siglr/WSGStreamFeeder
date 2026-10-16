param([Parameter(Mandatory=$true)][string]$SigningKey, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    [xml]$props = Get-Content -LiteralPath Directory.Build.props
    $version = [string]$props.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
    $upload = Join-Path $PSScriptRoot 'dist\upload'
    $previousUpload = $null
    if (Test-Path -LiteralPath $upload) {
        $previous = Get-Content -Raw -LiteralPath (Join-Path $upload 'WSG-Stream-Feeder.version.json') | ConvertFrom-Json
        if ($previous.product -ne 'WSG-Stream-Feeder' -or $previous.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Unrecognized existing upload directory.' }
        if ([version]$version -le [version]$previous.version) { throw 'Increment the release version before packaging; existing releases are preserved.' }
        $previousUpload = Join-Path $PSScriptRoot ('dist\upload-' + $previous.version)
        if (Test-Path -LiteralPath $previousUpload) { throw 'Previous upload backup already exists; inspect it before packaging.' }
    }
    $staging = Join-Path $PSScriptRoot ('build\release-upload-' + $version)
    if (Test-Path -LiteralPath $staging) { throw 'An incomplete upload staging directory already exists; inspect it before retrying.' }
    # Explicit production key selection; never search another product's key store.
    if (-not (Test-Path -LiteralPath $SigningKey)) { throw 'Signing key does not exist. Run tools/initialize-signing.ps1 first.' }
    & tools/initialize-signing.ps1 -KeyFile $SigningKey
    & .\build.ps1 -OfficialRelease -SkipTests:$SkipTests
    $signer = Join-Path $PSScriptRoot 'build\signing\ReleaseSigner.exe'
    $package = Join-Path $PSScriptRoot ('dist\release-' + $version)
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    $appFiles = @('WSG Stream Feeder.exe','WSG Stream Feeder.dll','WSG Stream Feeder.deps.json','WSG Stream Feeder.runtimeconfig.json','WSG Stream Feeder.Updater.exe','WSG Stream Feeder.Updater.dll','WSG Stream Feeder.Updater.deps.json','WSG Stream Feeder.Updater.runtimeconfig.json','Microsoft.FlightSimulator.SimConnect.dll','SimConnect.dll','README.md')
    foreach ($name in $appFiles) { Copy-Item -LiteralPath (Join-Path 'dist\WSG Stream Feeder' $name) -Destination $package }
    Copy-Item -LiteralPath NOTICE.md -Destination $package
    Copy-Item -LiteralPath notices\UPDATER-LICENSE.md -Destination $package
    Copy-Item -LiteralPath src\settings.json -Destination (Join-Path $package 'settings.example.json')
    [IO.File]::WriteAllText((Join-Path $package 'VERSION'), "$version`n")
    $files = [ordered]@{}
    Get-ChildItem -LiteralPath $package -File | Where-Object Name -ne 'app-files.json' | Sort-Object Name | ForEach-Object { $files[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    if ($files.Count -ne 15) { throw 'Release directory contains unexpected files. Use a new version/package directory.' }
    $inventory = Join-Path $package 'app-files.json'
    [IO.File]::WriteAllText($inventory, (@{schema=1;product='WSG-Stream-Feeder';version=$version;files=$files} | ConvertTo-Json -Depth 4))
    & $signer sign $SigningKey $inventory
    if ($LASTEXITCODE -ne 0) { throw 'Inventory signing failed.' }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $archive = Join-Path $staging 'WSG-Stream-Feeder.zip'
    # No settings.json, Output folder, signer, private key, or development tools in releases.
    Add-Type -AssemblyName System.IO.Compression
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $package -File | Sort-Object Name)) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $file.Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
    $feed = Join-Path $staging 'WSG-Stream-Feeder.version.json'
    [IO.File]::WriteAllText($feed, (@{schema=1;product='WSG-Stream-Feeder';version=$version;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant();size=(Get-Item -LiteralPath $archive).Length} | ConvertTo-Json))
    & $signer sign $SigningKey $feed
    if ($LASTEXITCODE -ne 0) { throw 'Release signing failed.' }
    & .\tools\verify-release.ps1 -PackageDirectory $package -UploadDirectory $staging
    & .\tools\prepare-web.ps1 -Destination $staging
    [IO.File]::WriteAllText((Join-Path $staging 'WSG-Stream-Feeder.zip.sha256'), (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() + '  WSG-Stream-Feeder.zip' + "`n")
    # Check all absolute move destinations before promoting the verified upload bundle.
    $distRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist'))
    $buildRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'build'))
    $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
    if ([IO.Path]::GetDirectoryName($resolvedStaging) -ne $buildRoot) { throw 'Upload staging path leaves build.' }
    foreach ($directory in @($distRoot, $buildRoot, $resolvedStaging)) {
        if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked release working directory refused.' }
    }
    foreach ($target in @($upload, $previousUpload) | Where-Object { $_ }) {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($target)) -ne $distRoot) { throw 'Upload move target leaves dist.' }
    }
    if ($previousUpload) { Move-Item -LiteralPath $upload -Destination $previousUpload }
    try { Move-Item -LiteralPath $staging -Destination $upload }
    catch {
        if ($previousUpload -and -not (Test-Path -LiteralPath $upload)) { Move-Item -LiteralPath $previousUpload -Destination $upload }
        throw
    }
    & .\tools\clean-dist.ps1 -KeepVersion $version
    Write-Host "Signed release ready: $upload"
} finally { Pop-Location }
