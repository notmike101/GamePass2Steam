$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('gp2s-names-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $directory 'AppxManifest.xml'), '<Package />')
    function Get-AppxPackage { [pscustomobject]@{ PackageFamilyName = 'Example_abcd'; InstallLocation = $directory; Name = 'Example' } }
    function Get-StartApps {
        [pscustomobject]@{ AppID = 'Example_abcd!Null'; Name = $null }
        [pscustomobject]@{ AppID = 'Example_abcd!Blank'; Name = ' ' }
        [pscustomobject]@{ AppID = 'Example_abcd!Named'; Name = 'Normal name' }
    }
    $result = (& "$PSScriptRoot/../src/GamePass2Steam/Discover.ps1") | ConvertFrom-Json
    if ($result.Items.Count -ne 3 -or $result.Warnings.Count -ne 2) { throw 'Wrong discovery or warning count' }
    foreach ($item in $result.Items) {
        $expected = if ($item.Id -eq 'Example_abcd!Named') { 'Normal name' } else { $item.Id }
        if ($item.Name -cne $expected) { throw 'Display name was not preserved or normalized' }
    }
    'PASS discovery normalizes null and blank names and preserves named apps'
} finally { [IO.Directory]::Delete($directory, $true) }
