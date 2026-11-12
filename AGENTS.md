# Contributor guide

These instructions apply to changes throughout this repository, whether made by a person or a coding assistant.

## Project structure

GamePass2Steam is a C# Windows Forms application that adds registered Xbox/Microsoft Store applications to Steam as non-Steam shortcuts. Its launcher activates the registered application and tracks the game session so Steam can track the launcher lifetime.

- `src/GamePass2Steam.Core/`: binary VDF serialization, shortcut updates and file storage, game metadata parsing, and session state. Keep platform-independent logic here.
- `src/GamePass2Steam/`: GUI and command-line entry points, Windows package discovery, application activation, and process/window inspection. `Discover.ps1` is embedded in the application.
- `tests/GamePass2Steam.Tests/`: a console-based test runner for the core library. Run it with `dotnet run`; it is not a `dotnet test` project.
- `scripts/package.ps1`: runs tests, publishes the Windows x64 app with its .NET runtime, and creates a ZIP and checksum under `dist/`.
- `.github/workflows/windows.yml`: build-only CI for branches and PRs; `.github/workflows/release.yml`: releases after a reviewed `dev` to `main` merge.

## Development commands

Use Windows x64 with the .NET 10 SDK for application development and integration testing. Run these commands from the repository root in PowerShell:

```powershell
# Build the application
dotnet build src/GamePass2Steam -c Release

# Run the automated checks
dotnet run --project tests/GamePass2Steam.Tests -c Release

# Start the development GUI
dotnet run --project src/GamePass2Steam

# Build the portable distribution, including tests
./scripts/package.ps1
```

`Directory.Build.props` enables nullable reference checking, implicit usings, deterministic builds, and warnings as errors. Follow the surrounding C# conventions and keep changes focused. Explain any new dependency and its packaging or licensing implications.

## Behavior to preserve

- Identify managed shortcuts by their `GamePass2Steam:` ownership tag, not their display name. Preserve unrelated shortcuts and existing user metadata; repeated imports should update the launch configuration without adding duplicates.
- Reject malformed or ambiguous shortcut data before writing. Preserve backup creation, atomic replacement, concurrent-change detection, and refusal to write while Steam is running. Restore operates on the entire shortcut snapshot.
- Discover and launch through the current Windows application registration. Do not hardcode game installation paths, package versions, or a particular Steam profile.
- Track the actual game rather than only its bootstrapper. Keep startup and failure handling bounded, and allow for process handoff before deciding a session ended.
- Do not force-close Steam, change game files or saves, bypass licensing, or require changes to WindowsApps permissions as part of importing or launching a game.

## Validating a change

Choose checks based on the affected behavior:

- **Core logic:** run the console test project and add focused regression cases for changed parsing, shortcut handling, metadata, or session behavior. Use fixtures and temporary directories, not a contributor's Steam configuration.
- **GUI:** build and exercise the affected controls, including error states and keyboard access.
- **Discovery or launcher:** test on Windows with a relevant installed game when available. Record the game and installation type, launch result, Steam's running status/presence, and behavior after normal game exit. If that environment is unavailable, state what remains unverified.
- **Packaging or CI:** run the packaging script and check the extracted app, bundled runtime/license files, and checksum. The workflow includes a discovery smoke test from a path containing spaces and Unicode.
- **Documentation only:** check accuracy against the owning code/configuration and verify links. A live game test is not needed.

For real Steam imports, use a packaged EXE in a stable folder: the shortcut stores the importer's executable path, so a development build can leave shortcuts pointing into build output. Back up any profile used for manual testing and close Steam before editing shortcuts. For scripted CLI checks, use `Start-Process -Wait -PassThru` and inspect the exit code and `--output` JSON; the application is a Windows GUI executable.

Successful discovery or activation does not establish gameplay, Steam Overlay, Steam Input, or Remote Play compatibility. Describe only the behavior actually checked. Review logs and screenshots for personal information before publishing them.

## Documentation and releases

Update `README.md` and relevant `docs/wiki/` pages when user-facing behavior changes. Keep measured integration results and limitations in `docs/VERIFICATION.md`; use `docs/RESEARCH.md` for design background. GUI screenshots belong in `docs/images/`. Generated builds and local test captures are ignored by Git and should not be committed.

The GitHub wiki is published separately from the application repository. Include wiki changes in `docs/wiki/` so they can be reviewed with the code; maintainers synchronize those files to the wiki repository. The application workflow does not publish wiki pages.

Create feature branches from `dev` and target feature PRs at `dev`. Keep each issue focused and document PR dependencies. Do not merge PRs without maintainer review. Promote reviewed work through a same-repository `dev` to `main` PR.

Only a merged `dev` to `main` PR triggers `.github/workflows/release.yml`. It automatically increments the patch version above the project version and existing stable version tags, packages and smoke-tests that exact merge plus its version-only commit, then atomically pushes the version commit and tags before publishing. Direct pushes and manual CI runs cannot publish. `main-build` is retired. Do not manually bump the patch version for ordinary feature PRs.

Finish one release promotion before merging another. The workflow fails safely if `main` advances before the version commit can be pushed; it never force-pushes or silently includes unrelated later changes. Re-running the original merged-PR workflow resumes its recorded version after a publishing failure instead of bumping again. Keep the `dev-release-pr-*` marker tags, which make retries idempotent. After release, merge `main` back into `dev` to carry the version commit forward; this does not publish. If branch protection is added, preserve an approved way for the release job to write its version commit and tags.

When changing release scripts or workflows, run `./scripts/test-release.ps1`, `./scripts/package.ps1`, and `./scripts/test-package.ps1`. The release checks exercise the eligibility policy and version calculation without publishing. Preserve bundled license notices and immutable published assets.

In the change description, explain the problem, the resulting behavior, and the checks performed. Call out compatibility or data-format changes and any validation that could not be completed.
