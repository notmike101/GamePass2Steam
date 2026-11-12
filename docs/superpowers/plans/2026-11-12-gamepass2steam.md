# GamePass2Steam Implementation Plan

> Execute inline with superpowers:executing-plans. User prohibits subagents and explicitly requests autonomous implementation after assessment.

**Goal:** Discover, import and launch registered Xbox games from Steam safely, and ship a self-contained Windows build.
**Architecture:** Pure C# core plus Windows UI/activation host. Stable registered identity connects discovery, Steam shortcut and launch-time rediscovery.
**Tech stack:** .NET 10, WinForms, Windows PowerShell, Win32 COM.
**Spec:** ../specs/2026-11-12-gamepass2steam-design.md

## Constraints and review focus

No game/account/path hardcoding; no game file modifications; no forced Steam shutdown; no unbacked configuration replacement; no broad compatibility claims from mocks. Review malformed binary data, concurrent writes, Unicode/quoting, bootstrap handoff and relocated/upgraded installations.

## Tasks

- [ ] 1. Core safety. Create src/GamePass2Steam.Core/{BinaryVdf,SteamShortcuts,Game,SessionTracker}.cs and tests/GamePass2Steam.Tests. Tests first: roundtrip independent binary fixture, unknown fields, malformed inputs, repeat import, metadata preservation, removal, backup/restore, Steam-running and conflict refusal, manifest traversal rejection, session failure/handoff. Run assert executable, implement, rerun, commit/push.
- [ ] 2. Windows integration. Create src/GamePass2Steam/{Discovery,Native,Launcher,Program}.cs and embedded discovery script. Consume Game records and session state. Discover installed game, validate observed GDK target and AUMID; CLI discovery and launch logs. Commit/push after build/tests.
- [ ] 3. UI and packaging. Create MainForm.cs, scripts/package.ps1, CI, README, notices. Consume discovery and Steam APIs, select profile, add/update/remove/restore. Self-contained portable ZIP, smoke-test outside checkout in path with spaces/Unicode. Commit/push.
- [ ] 4. Real acceptance and review. Gracefully exit Steam, preserve baseline, import twice, confirm no duplicate or unrelated change, reopen Steam, select Play, inspect actual game screen, quit normally and verify helper exit. Record actual results and limitations in docs/VERIFICATION.md. Review whole diff directly (no subagent), fix findings with regression checks, rebuild, commit/push.
