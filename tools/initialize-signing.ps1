param([Parameter(Mandatory=$true)][string]$KeyFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$signerDirectory = Join-Path $projectRoot 'build\signing'
New-Item -ItemType Directory -Path $signerDirectory -Force | Out-Null
$signer = Join-Path $signerDirectory 'ReleaseSigner.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo "/out:$signer" /reference:System.Security.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'ReleaseSigner.cs') (Join-Path $projectRoot 'src\Updates\ReleaseTrust.cs') (Join-Path $projectRoot 'src\Updates\ReleasePublicKey.cs')
if ($LASTEXITCODE -ne 0) { throw 'Unable to build release signer.' }
$operation = if (Test-Path -LiteralPath $KeyFile) { 'public' } else { 'init' }
$publicPath = Join-Path $projectRoot 'src\Updates\ReleasePublicKey.cs'
$existing = [IO.File]::ReadAllText($publicPath)
if ($operation -eq 'init' -and $existing -notmatch 'Xml = ""') { throw 'An established public key exists. Restore its private key; do not rotate it implicitly.' }
$public = (& $signer $operation $KeyFile).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Signing-key initialization failed.' }
$source = '// Public verification key only. Keep the matching DPAPI-protected private key outside this project.' + "`n" + 'internal static class ReleasePublicKey { internal const string Xml = ' + (ConvertTo-Json -InputObject $public -Compress) + '; }' + "`n"
if ($existing -notmatch 'Xml = ""' -and $existing -ne $source) { throw 'This key does not match the established public verification key.' }
[IO.File]::WriteAllText($publicPath, $source)
Write-Host 'Public verification key is ready. The private key remains DPAPI-protected outside the project.'
