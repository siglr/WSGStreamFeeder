param([string]$PackageDirectory, [string]$UploadDirectory)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (-not $PackageDirectory) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $project 'Directory.Build.props')
    $PackageDirectory = Join-Path $project ('dist\release-' + $props.Project.PropertyGroup.Version)
}
if (-not $UploadDirectory) { $UploadDirectory = Join-Path $project 'dist\upload' }
$publicSource = Get-Content -Raw -LiteralPath (Join-Path $project 'src\Updates\ReleasePublicKey.cs')
$xml = [regex]::Match($publicSource, '<RSAKeyValue>.*?</RSAKeyValue>').Value
$rsa = [Security.Cryptography.RSA]::Create()
$rsa.FromXmlString($xml)
$utf8 = [Text.UTF8Encoding]::new($false)
$sha = [Security.Cryptography.SHA256]::Create()
function Digest([byte[]]$bytes) { [Convert]::ToHexString($sha.ComputeHash($bytes)).ToLowerInvariant() }
function Verify-Signature($metadata, [string]$message) {
    if ($metadata.schema -ne 1 -or $metadata.product -ne 'WSG-Stream-Feeder' -or $metadata.keyId -ne (Digest $utf8.GetBytes($xml))) { throw 'Release identity mismatch' }
    if (-not $rsa.VerifyData($utf8.GetBytes($message), [Convert]::FromBase64String($metadata.signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Invalid release signature' }
}
try {
    $feed = Get-Content -Raw -LiteralPath (Join-Path $UploadDirectory 'WSG-Stream-Feeder.version.json') | ConvertFrom-Json
    Verify-Signature $feed ("WSGStreamFeeder release v1`n" + $feed.product + "`n" + $feed.version + "`n" + $feed.sha256 + "`n" + $feed.size.ToString([Globalization.CultureInfo]::InvariantCulture) + "`n")
    $archive = Join-Path $UploadDirectory 'WSG-Stream-Feeder.zip'
    if ((Get-Item -LiteralPath $archive).Length -ne $feed.size -or (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $feed.sha256) { throw 'ZIP mismatch' }
    $inventory = Get-Content -Raw -LiteralPath (Join-Path $PackageDirectory 'app-files.json') | ConvertFrom-Json
    if ($inventory.version -ne $feed.version) { throw 'Version mismatch' }
    [string[]]$names = $inventory.files.PSObject.Properties.Name
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $message = "WSGStreamFeeder inventory v1`n" + $inventory.product + "`n" + $inventory.version + "`n"
    foreach ($name in $names) { $message += $name + [char]0 + $inventory.files.$name + "`n" }
    Verify-Signature $inventory $message
    foreach ($name in $names) {
        if ([IO.Path]::GetFileName($name) -ne $name) { throw 'Unexpected nested package path' }
        if ((Get-FileHash -LiteralPath (Join-Path $PackageDirectory $name)).Hash.ToLowerInvariant() -ne $inventory.files.$name) { throw "File mismatch: $name" }
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        if ($zip.Entries.Count -ne $names.Count + 1) { throw 'Unexpected ZIP entries' }
        foreach ($entry in $zip.Entries) {
            $expected = if ($entry.FullName -eq 'app-files.json') { (Get-FileHash -LiteralPath (Join-Path $PackageDirectory 'app-files.json')).Hash.ToLowerInvariant() } else { $inventory.files.($entry.FullName) }
            if (-not $expected) { throw ('Unlisted ZIP entry: ' + $entry.FullName) }
            $stream = $entry.Open()
            try { $actual = [Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
            if ($actual -ne $expected) { throw ('ZIP entry mismatch: ' + $entry.FullName) }
        }
    } finally { $zip.Dispose() }
    Write-Host "Verified release $($feed.version): both signatures, $($names.Count) file hashes, ZIP contents, size and SHA-256."
    Write-Host "SHA-256: $($feed.sha256)"
} finally { $rsa.Dispose(); $sha.Dispose() }
