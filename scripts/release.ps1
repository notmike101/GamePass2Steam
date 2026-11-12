param([Parameter(Mandatory)][ValidateSet('Prepare', 'Publish')][string]$Phase)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Assert-ReleaseEvent($event, [string]$eventName, [string]$repository) {
    $pr = $event.pull_request
    if ($eventName -ne 'pull_request' -or $event.action -ne 'closed' -or $pr.merged -ne $true -or
        $pr.head.ref -ne 'dev' -or $pr.base.ref -ne 'main' -or
        $pr.head.repo.full_name -ne $repository -or $pr.base.repo.full_name -ne $repository -or
        $pr.merge_commit_sha -notmatch '^[0-9a-f]{40}$' -or $event.number -notmatch '^[1-9][0-9]*$') {
        throw 'Only a merged dev-to-main PR from this repository may release.'
    }
}
function Get-NextVersion([string]$current, [string[]]$tags) {
    if ($current -notmatch '^\d+\.\d+\.\d+$') { throw 'Project Version must be major.minor.patch' }
    $highest = [version]$current
    foreach ($tag in $tags) {
        if ($tag -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -gt $highest) { $highest = [version]$Matches[1] }
    }
    return '{0}.{1}.{2}' -f $highest.Major, $highest.Minor, ($highest.Build + 1)
}
# Dot-sourcing exposes the policy/version functions to the offline checks without side effects.
if ($MyInvocation.InvocationName -eq '.') { return }

$event = Get-Content $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
Assert-ReleaseEvent $event $env:GITHUB_EVENT_NAME $env:GITHUB_REPOSITORY
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$project = 'src/GamePass2Steam/GamePass2Steam.csproj'
$merge = $event.pull_request.merge_commit_sha
$marker = "dev-release-pr-$($event.number)"

if ($Phase -eq 'Prepare') {
    git fetch origin main --tags
    if (git tag --list $marker) {
        git checkout --detach "refs/tags/$marker"
        if ((git rev-parse 'HEAD^') -ne $merge) { throw 'Release marker does not belong to this merge' }
    } else {
        # Never publish a later unrelated main commit or force-push over concurrent changes.
        if ((git rev-parse origin/main) -ne $merge) { throw 'Main advanced beyond this merge. Finish one release promotion at a time; reconcile main before retrying.' }
        git checkout --detach $merge
        $xml = Get-Content $project -Raw
        $current = ([xml]$xml).Project.PropertyGroup.Version
        $version = Get-NextVersion $current @(git tag --list 'v*')
        $updated = $xml.Replace("<Version>$current</Version>", "<Version>$version</Version>")
        if ($updated -eq $xml) { throw 'Could not replace project version' }
        [IO.File]::WriteAllText((Join-Path $repo $project), $updated)
        git config user.name 'github-actions[bot]'
        git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
        git add -- $project
        git commit -m "chore: release v$version (dev PR #$($event.number))"
        git tag "v$version"
        git tag $marker
    }
    return
}

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version
$tag = "v$version"
$commit = git rev-parse HEAD
if ((git rev-parse 'HEAD^') -ne $merge -or (git rev-parse "refs/tags/$marker") -ne $commit -or (git rev-parse "refs/tags/$tag") -ne $commit) { throw 'Release commit/tag mismatch' }
$archive = "dist/GamePass2Steam-$version-win-x64.zip"
$expected = (Get-Content "$archive.sha256").Split(' ')[0]
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Artifact checksum mismatch' }
$remoteMarker = git ls-remote origin "refs/tags/$marker"
if (!$remoteMarker) {
    # Release versions live on tags so publishing never bypasses branch review rules.
    git push --atomic origin "refs/tags/$tag" "refs/tags/$marker"
} elseif (($remoteMarker -split '\s+')[0] -ne $commit) { throw 'Remote release marker changed' }

$releases = gh release list --limit 1000 --json tagName,isDraft | ConvertFrom-Json
$existing = $releases | Where-Object tagName -eq $tag
if ($existing -and !$existing.isDraft) { Write-Output "$tag is already published; leaving its assets unchanged."; return }
if (!$existing) {
    $notes = "Windows x64 portable release from dev-to-main PR #$($event.number), merge $merge. Extract the entire ZIP into a permanent folder and run GamePass2Steam.exe. No separate .NET installation is needed. The app is unsigned. Installation and troubleshooting: https://github.com/$env:GITHUB_REPOSITORY/wiki"
    gh release create $tag --verify-tag --draft --title "GamePass2Steam $version" --notes $notes --generate-notes
}
gh release upload $tag $archive "$archive.sha256" --clobber
$assets = gh release view $tag --json assets --jq '.assets[].name'
if ($assets -notcontains (Split-Path $archive -Leaf) -or $assets -notcontains ((Split-Path $archive -Leaf) + '.sha256')) { throw 'Release assets are incomplete' }
gh release edit $tag --draft=false --latest
