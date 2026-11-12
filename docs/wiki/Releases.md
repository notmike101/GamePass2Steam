# Releases and updates

## Choose a download

- **[Latest versioned release](https://github.com/notmike101/GamePass2Steam/releases/latest):** a fixed `v<major>.<minor>.<patch>` release. Download `GamePass2Steam-<version>-win-x64.zip` and its `.sha256` file. Existing versioned assets are not replaced by the pipeline.
- **[Latest main build](https://github.com/notmike101/GamePass2Steam/releases/tag/main-build):** a rolling prerelease after successful main-branch checks. Its assets and tag change; the release notes identify the source commit. Use a versioned release for a fixed download.
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

The [Windows build and publish action](https://github.com/notmike101/GamePass2Steam/actions/workflows/windows.yml) runs core tests, creates a self-contained package, and checks packaged discovery in a path containing spaces and Unicode. Only successful main-branch builds publish; pull requests cannot publish releases.

Every successful main build refreshes `main-build`. If the project version has no versioned release yet, the workflow also publishes that version. Maintainers increase `<Version>` in `src/GamePass2Steam/GamePass2Steam.csproj` for the next versioned release. Manual workflow runs publish only when run on main. CI does not own a licensed game or verify Steam presence; real integration results are recorded separately in the [verification document](https://github.com/notmike101/GamePass2Steam/blob/main/docs/VERIFICATION.md).
