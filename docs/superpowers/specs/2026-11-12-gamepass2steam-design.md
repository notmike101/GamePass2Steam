# GamePass2Steam design

Build a Windows application that imports locally registered Xbox games into Steam and follows their real lifetime. The first acceptance case is State of Decay 2. No installation path, account or game identity belongs in product code.

## Approaches and decision

An Electron/Node UI would honor the language preference but still need native COM activation, package/process identity queries and a native launcher. A browser UI plus Node adds a local server and the same interop. Choose C#/.NET 10 WinForms: one language, built-in UI, direct Win32 interop, self-contained x64 distribution. No third-party product dependencies. This is the implementer's decision under the user's explicit delegation of routine choices. No JavaScript is used, so JS/JSDoc ESLint packages do not apply.

## Discovery and launch

Use current-user Get-AppxPackage and Get-StartApps via the Windows PowerShell included with Windows; serialize structured JSON. Parse manifests with DTDs disabled. Use MicrosoftGame.config to identify GDK games, real executables and non-development entrypoints. Also recognize Xbox Live extensions in older UWP manifests. Hide frameworks/resource packages and applications without visible entries. Offer all registered apps as a fallback because not all games carry reliable game metadata; allow browsing a folder and matching its manifest identity to current-user registration. A folder alone must not invent a license-bypassing direct-executable launch.

Store stable AUMID in Steam, resolve current metadata each launch, activate through documented IApplicationActivationManager. For GDK, track configured executable paths (including custom Content paths) beyond the bootstrap PID. For UWP, use package identity and application executable. Startup is bounded; session exit is driven by matching processes, with a short handoff debounce, not a fixed play duration. Fail clearly if installation is missing or untrackable. No changes to licensing, save files, game files or services.

## Steam and distribution

Steam shortcuts.vdf is undocumented binary KeyValues. Preserve ordered typed nodes, unknown supported scalar fields and nested objects. Reject unknown types, malformed roots, duplicate critical fields, excessive depth and truncated data. Tag managed shortcuts with stable identity; preserve user metadata and appid when updating. Never modify while Steam runs; check again immediately before replacement. Use exclusive application lock, same-directory temp, flush and atomic replacement with a unique backup; refuse concurrent changes. Restore uses the same safety path and backs up the current file.

Portable self-contained folder: extract to a permanent location and keep GamePass2Steam.exe at the same path during updates. Shortcut uses that exact executable plus stable AUMID, never the checkout. Moving it requires re-import to repair. No installer/admin access is necessary. GUI offers profile selection, discovery/filtering, add/update, remove-managed and backup restore. CLI enables reproducible integration tests.

## Scope and verification

Files: Core contains binary VDF, Steam mutation, manifest interpretation and session state logic; Windows app contains discovery, activation, process inspection, CLI and WinForms UI; Tests is an assert-based executable using isolated temporary profiles; scripts and Windows CI produce the release ZIP. Tests exercise byte preservation, malformed rejection, quoting, deduplication, metadata, backups/restore, running-state and concurrent-write refusal, startup/handoff/exit. Real packaged app must separately demonstrate Steam Play to a usable game screen and clean helper exit.

Artwork, external EA/Ubisoft launchers, protocol guessing, overlay injection and controller emulation are out of scope. Overlay/Input/Remote Play are separate unverified capabilities. Windows 10/11 x64 is the intended platform; record the actual test OS. No subagents. Work on feat/windows-app and push coherent Conventional Commits without merging.
