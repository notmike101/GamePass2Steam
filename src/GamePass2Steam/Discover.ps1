$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$start = @{}
Get-StartApps | ForEach-Object { $start[$_.AppID] = $_.Name }
$items = [System.Collections.Generic.List[object]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
foreach ($package in Get-AppxPackage) {
    if ($package.IsFramework -or $package.IsResourcePackage -or !$package.InstallLocation) { continue }
    $prefix = $package.PackageFamilyName + '!'
    $ids = @($start.Keys | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    if (!$ids.Count) { continue }
    try {
        $manifest = [IO.File]::ReadAllText((Join-Path $package.InstallLocation 'AppxManifest.xml'))
        $configPath = Join-Path $package.InstallLocation 'MicrosoftGame.config'
        $config = if ([IO.File]::Exists($configPath)) { [IO.File]::ReadAllText($configPath) } else { $null }
        foreach ($id in $ids) {
            $name = $start[$id]
            if ([string]::IsNullOrWhiteSpace($name)) { $name = $id; $warnings.Add($id + ': missing display name; using application identity.') }
            $items.Add(@{ Id=$id; Name=$name; Family=$package.PackageFamilyName; InstallPath=$package.InstallLocation; Manifest=$manifest; Config=$config })
        }
    } catch { $warnings.Add($package.Name + ': ' + $_.Exception.Message) }
}
@{ Items=@($items.ToArray()); Warnings=@($warnings.ToArray()) } | ConvertTo-Json -Depth 5 -Compress
