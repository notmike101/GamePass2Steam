$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('gp2s-target-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $directory 'AppxManifest.xml'), '<Package />')
    function Get-AppxPackage {
        [pscustomobject]@{ PackageFamilyName = 'Example_abcd'; InstallLocation = $directory; Name = 'Example' }
        [pscustomobject]@{ PackageFamilyName = 'Other_abcd'; InstallLocation = "$directory/missing"; Name = 'Other' }
    }
    function Get-StartApps {
        [pscustomobject]@{ AppID = 'Example_abcd!One'; Name = 'One' }
        [pscustomobject]@{ AppID = 'Example_abcd!Two'; Name = 'Two' }
        [pscustomobject]@{ AppID = 'Other_abcd!App'; Name = 'Other' }
    }
    $script = "$PSScriptRoot/../src/GamePass2Steam/Discover.ps1"
    $full = (& $script -ApplicationId '') | ConvertFrom-Json
    $target = (& $script -ApplicationId 'EXAMPLE_abcd!ONE') | ConvertFrom-Json
    $missing = (& $script -ApplicationId 'Missing_abcd!App') | ConvertFrom-Json
    if ($full.Items.Count -ne 2 -or $full.Warnings.Count -ne 1) { throw 'Full discovery changed' }
    if ($target.Items.Count -ne 1 -or $target.Items[0].Id -cne 'Example_abcd!One' -or $target.Warnings.Count -ne 0) { throw 'Target discovery read unrelated registrations' }
    if ($missing.Items.Count -ne 0 -or $missing.Warnings.Count -ne 0) { throw 'Missing target must be empty' }
    'PASS targeted discovery filters before reading manifests and preserves full discovery'
} finally { [IO.Directory]::Delete($directory, $true) }
