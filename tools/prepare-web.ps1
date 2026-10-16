param([string]$Destination)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$web = Join-Path $project 'web'
if (-not $Destination) { $Destination = Join-Path $project 'dist\upload' }
if (-not (Test-Path -LiteralPath $Destination -PathType Container)) { throw 'Website destination directory must already exist.' }
$utf8 = [Text.UTF8Encoding]::new($false)

# Hash asset contents, not the app version: website-only edits also invalidate caches.
# Update the checked-in HTML as well as the upload copy so neither goes stale.
foreach ($name in @('index.html', 'WSG-Stream-Feeder.html')) {
    $path = Join-Path $web $name
    $html = [IO.File]::ReadAllText($path)
    $updated = [regex]::Replace($html, '(?<prefix>(?:src|href)=")(?<asset>tools\.css|feeder\.js|assets/[^"?]+\.png)(?:\?v=[a-f0-9]+)?"', [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $asset = $match.Groups['asset'].Value
        $hash = (Get-FileHash -LiteralPath (Join-Path $web $asset) -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 16)
        return $match.Groups['prefix'].Value + $asset + '?v=' + $hash + '"'
    })
    if ($updated -ne $html) { [IO.File]::WriteAllText($path, $updated, $utf8) }
}
foreach ($name in @('index.html', 'WSG-Stream-Feeder.html', 'tools.css', 'feeder.js', '.htaccess', 'UPLOAD-INSTRUCTIONS.txt')) {
    Copy-Item -LiteralPath (Join-Path $web $name) -Destination $Destination -Force
}
$assets = Join-Path $Destination 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $web 'assets') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $assets -Force
}
Write-Host "Website prepared with content-versioned assets: $Destination"
