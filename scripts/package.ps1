param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../dist'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $output ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
dotnet run --project (Join-Path $repo 'tests/GamePass2Steam.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
dotnet publish (Join-Path $repo 'src/GamePass2Steam') -c Release -r win-x64 --self-contained true -o $stage -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
foreach ($name in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination (Join-Path $stage 'docs') -Recurse
$archive = Join-Path $output 'GamePass2Steam-0.1.0-win-x64.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($archive)) -Encoding ascii
Write-Output "Release: $archive"
Write-Output "Staging: $stage"
Write-Output "SHA256: $hash"
