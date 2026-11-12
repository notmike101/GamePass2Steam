param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../dist'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $repo 'src/GamePass2Steam/GamePass2Steam.csproj') -Raw)).Project.PropertyGroup.Version
$archive = Join-Path $OutputDirectory "GamePass2Steam-$version-win-x64.zip"
$expected = (Get-Content "$archive.sha256").Split(' ')[0]
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Artifact checksum mismatch' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gp2s-smoke-' + [Guid]::NewGuid().ToString('N'))
$target = Join-Path $testRoot 'Game Pass Café'
Expand-Archive -LiteralPath $archive -DestinationPath $target
$report = Join-Path $testRoot 'discovery.json'
$previousRoot = $env:DOTNET_ROOT
$previousLookup = $env:DOTNET_MULTILEVEL_LOOKUP
try {
    $env:DOTNET_ROOT = Join-Path $testRoot 'no-runtime'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $p = Start-Process -FilePath (Join-Path $target 'GamePass2Steam.exe') -ArgumentList @('--discover', '--output', ('"' + $report + '"')) -Wait -PassThru -WindowStyle Hidden
    if ($p.ExitCode -ne 0) { throw "Packaged discovery failed with exit code $($p.ExitCode)" }
    $data = Get-Content $report -Raw | ConvertFrom-Json
    if ($null -eq $data.Games) { throw 'No discovery response' }
    Write-Output 'PASS package checksum and self-contained discovery from spaces/Unicode path'
} finally {
    $env:DOTNET_ROOT = $previousRoot
    $env:DOTNET_MULTILEVEL_LOOKUP = $previousLookup
}
