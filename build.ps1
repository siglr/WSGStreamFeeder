param([string]$Configuration = 'Release', [switch]$OfficialRelease, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SkipTests) {
        dotnet run --project tests/WsgStreamFeeder.Tests.csproj -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Compatibility checks failed.' }
        dotnet run --project window-tests/WsgStreamFeeder.WindowTests.csproj -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Window lifecycle checks failed.' }
        dotnet publish update-tests/WsgStreamFeeder.UpdateTests.csproj -c $Configuration -o build/update-tests
        if ($LASTEXITCODE -ne 0) { throw 'Updater test build failed.' }
        & './build/update-tests/WsgStreamFeeder.UpdateTests.exe'
        if ($LASTEXITCODE -ne 0) { throw 'Updater checks failed.' }
    }
    $official = if ($OfficialRelease) { 'true' } else { 'false' }
    dotnet publish src/WsgStreamFeeder.csproj -c $Configuration -r win-x64 --self-contained false "-p:OfficialRelease=$official" -o 'dist/WSG Stream Feeder'
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    dotnet publish updater/WsgStreamFeeder.Updater.csproj -c $Configuration -r win-x64 --self-contained false -o 'dist/updater'
    if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }
    foreach ($name in @('WSG Stream Feeder.Updater.exe','WSG Stream Feeder.Updater.dll','WSG Stream Feeder.Updater.deps.json','WSG Stream Feeder.Updater.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path 'dist/updater' $name) -Destination 'dist/WSG Stream Feeder'
    }
    Copy-Item -LiteralPath README.md -Destination 'dist/WSG Stream Feeder/README.md'
    Write-Host "Ready: $PSScriptRoot\dist\WSG Stream Feeder\WSG Stream Feeder.exe"
}
finally { Pop-Location }
