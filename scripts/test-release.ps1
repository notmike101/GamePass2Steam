$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/release.ps1" -Phase Prepare
function Check([bool]$ok, [string]$name) { if (!$ok) { throw $name }; Write-Output "PASS $name" }
$valid = '{"action":"closed","number":20,"pull_request":{"merged":true,"merge_commit_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head":{"ref":"dev","repo":{"full_name":"owner/repo"}},"base":{"ref":"main","repo":{"full_name":"owner/repo"}}}}'
Assert-ReleaseEvent ($valid | ConvertFrom-Json) 'pull_request' 'owner/repo'
Check $true 'merged same-repository dev-to-main PR is eligible'
foreach ($scenario in 'push','workflow_dispatch','unmerged','feature','fork','dev-target','opened','missing-sha') {
    $sample = $valid | ConvertFrom-Json
    $eventName = 'pull_request'
    switch ($scenario) {
        'push' { $eventName = 'push' }
        'workflow_dispatch' { $eventName = 'workflow_dispatch' }
        'unmerged' { $sample.pull_request.merged = $false }
        'feature' { $sample.pull_request.head.ref = 'feature/fix' }
        'fork' { $sample.pull_request.head.repo.full_name = 'fork/repo' }
        'dev-target' { $sample.pull_request.base.ref = 'dev' }
        'opened' { $sample.action = 'opened' }
        'missing-sha' { $sample.pull_request.merge_commit_sha = '' }
    }
    $rejected = $false
    try { Assert-ReleaseEvent $sample $eventName 'owner/repo' } catch { $rejected = $true }
    Check $rejected "release rejects $scenario"
}
Check ((Get-NextVersion '0.1.0' @('v0.1.0','main-build')) -eq '0.1.1') 'first promotion increments patch'
Check ((Get-NextVersion '0.1.0' @('v0.1.9','v0.1.10','v0.1.11-rc.1')) -eq '0.1.11') 'stale dev version advances beyond numeric stable tags'
Check ((Get-NextVersion '1.0.0' @('v0.1.5')) -eq '1.0.1') 'higher project version is respected'
$rejected = $false
try { Get-NextVersion 'invalid' @() | Out-Null } catch { $rejected = $true }
Check $rejected 'malformed version is rejected'
foreach ($file in 'release.ps1','package.ps1','test-package.ps1') {
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file), [ref]$null, [ref]$parseErrors) | Out-Null
    Check ($parseErrors.Count -eq 0) "$file parses"
}

# Exercise retry and stale-main handling with Git mocked; never create commits or contact GitHub.
$projectPath = Join-Path $PSScriptRoot '../src/GamePass2Steam/GamePass2Steam.csproj'
$originalProject = [IO.File]::ReadAllText($projectPath)
$originalLocation = Get-Location
$savedEnvironment = @{}
foreach ($name in 'GITHUB_EVENT_PATH','GITHUB_EVENT_NAME','GITHUB_REPOSITORY') { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('gp2s-release-event-' + [Guid]::NewGuid().ToString('N') + '.json')
[IO.File]::WriteAllText($fixture, $valid)
$global:gp2sTestReleaseRecorded = $false
$global:gp2sTestMainAtMerge = $true
$global:gp2sTestReleaseCommits = 0
function git {
    $command = $args -join ' '
    switch -Regex ($command) {
        '^fetch origin main --tags$' { return }
        '^tag --list dev-release-pr-20$' { if ($global:gp2sTestReleaseRecorded) { 'dev-release-pr-20' }; return }
        '^tag --list v\*$' { 'v0.1.0'; return }
        '^rev-parse origin/main$' { if ($global:gp2sTestMainAtMerge) { 'a' * 40 } else { 'b' * 40 }; return }
        '^rev-parse HEAD\^$' { 'a' * 40; return }
        '^commit ' { $global:gp2sTestReleaseCommits++; return }
        '^(checkout --detach |config user\.|add (?:-- )?|tag v|tag dev-release-pr-20)' { return }
        default { throw "Unexpected Git operation in release test: $command" }
    }
}
try {
    $env:GITHUB_EVENT_PATH = $fixture
    $env:GITHUB_EVENT_NAME = 'pull_request'
    $env:GITHUB_REPOSITORY = 'owner/repo'
    [IO.File]::WriteAllText($projectPath, ($originalProject -replace '<Version>[^<]+</Version>', '<Version>0.1.0</Version>'))
    & "$PSScriptRoot/release.ps1" -Phase Prepare
    Check (([xml](Get-Content $projectPath -Raw)).Project.PropertyGroup.Version -eq '0.1.1' -and $global:gp2sTestReleaseCommits -eq 1) 'prepare commits one patch bump'
    $global:gp2sTestReleaseRecorded = $true
    & "$PSScriptRoot/release.ps1" -Phase Prepare
    Check (([xml](Get-Content $projectPath -Raw)).Project.PropertyGroup.Version -eq '0.1.1' -and $global:gp2sTestReleaseCommits -eq 1) 'retry reuses recorded release without another bump'
    $global:gp2sTestReleaseRecorded = $false
    $global:gp2sTestMainAtMerge = $false
    $rejected = $false
    try { & "$PSScriptRoot/release.ps1" -Phase Prepare } catch { $rejected = $true }
    Check ($rejected -and $global:gp2sTestReleaseCommits -eq 1) 'advanced main fails before creating a release commit'
} finally {
    [IO.File]::WriteAllText($projectPath, $originalProject)
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Set-Location $originalLocation
    Remove-Item Function:/git
    Remove-Variable gp2sTestReleaseRecorded,gp2sTestMainAtMerge,gp2sTestReleaseCommits -Scope Global
    Remove-Item -LiteralPath $fixture
}

