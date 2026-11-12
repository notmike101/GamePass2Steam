# Releases and updates

## Choose a download

- **[Latest versioned release](https://github.com/notmike101/GamePass2Steam/releases/latest):** a fixed `v<major>.<minor>.<patch>` release. Download `GamePass2Steam-<version>-win-x64.zip` and its `.sha256` file. Existing versioned assets are not replaced by the pipeline.
- **Source code archives:** intended for contributors; they are not the runnable Windows package.

Each application ZIP includes the Windows x64 executable, .NET runtime files, documentation and third-party license notices. No separate runtime installation is required. Builds are unsigned.

## Verify the ZIP

In PowerShell, run the following with the downloaded version's filename:

```powershell
Get-FileHash .\GamePass2Steam-0.1.0-win-x64.zip -Algorithm SHA256
Get-Content .\GamePass2Steam-0.1.0-win-x64.zip.sha256
```

The 64 hexadecimal characters must match (case does not matter). A mismatch means you should not use that download. A matching checksum checks integrity against the published file; it does not replace trusting the release source.

Exit the app and games launched through it, then extract the new release into the same permanent app folder. If changing its location, re-import shortcuts with Steam closed. See [Managing shortcuts](Managing-Shortcuts).

## How publishing works

The [Windows build action](https://github.com/notmike101/GamePass2Steam/actions/workflows/windows.yml) tests PRs and pushes to `dev` and `main`, creates a self-contained package, and checks packaged discovery in a path containing spaces and Unicode. Manual runs are build-only.

Feature PRs target `dev`. A maintainer reviews and merges a PR from this repository's `dev` into `main` when the integrated work is ready. Only that merged-PR event starts the [release action](https://github.com/notmike101/GamePass2Steam/actions/workflows/release.yml). Closing an unmerged PR, merging a different source branch, merging into `dev`, pushing directly to `main`, or manually running CI cannot publish a release.

The release automatically increments the patch version above the current project version and stable version tags, then builds, tests, and smoke-tests the package. Once those checks pass, it records the bumped version in a tagged release commit and publishes a fixed `v<major>.<minor>.<patch>` release containing the ZIP and SHA-256 file. The first promotion after `v0.1.0` normally produces `v0.1.1`. Existing published assets are not replaced. The historical `main-build` prerelease is no longer updated; use the latest versioned release.

Maintainers should finish one promotion before merging the next. The release commit lives on its tags, so `main` and `dev` retain their development baseline version; the next release increments above existing stable tags. Publishing needs no protected-branch bypass. If `main` advances during a release, the workflow fails instead of overwriting commits or releasing an unintended snapshot. Reconcile the branch before retrying. Failed publication after the version commit can be retried using **Re-run all jobs** on the original merged-PR run: its `dev-release-pr-<number>` marker reuses the same version, resumes an incomplete draft, and leaves an already published release unchanged. Keep these marker tags. No personal credential or extra repository secret is required; the release job uses GitHub's scoped `GITHUB_TOKEN` with contents-write permission.

CI does not own a licensed game or verify Steam presence; real integration results are recorded separately in the [verification document](https://github.com/notmike101/GamePass2Steam/blob/main/docs/VERIFICATION.md).