# Verification

## Environment and scope

Verified on 2026-11-12, Windows 11 Pro x64, build 10.0.26300. State of Decay 2 package version 2.741.435.0, installed through Xbox in a custom folder. No game-specific identity or installation path is embedded in product code. The self-contained build was launched from a permanent folder outside the source checkout. Development discovery was also exercised. Only State of Decay 2 has received a real licensed-game acceptance test.

## Real Steam acceptance — passed

1. Discovered State of Decay 2 as GDK / packaged Win32. Windows manifest points to GameLaunchHelper; GDK config identifies the shipping executable.
2. Requested normal Steam shutdown, waited for Steam to exit, and imported into the previously active local profile. A timestamped backup was created beside shortcuts.vdf.
3. Repeated the import: no file change and no new backup. Binary comparison established exactly one new managed entry and preservation of the one pre-existing shortcut byte for byte.
4. Reopened Steam. User completed Steam sign-in manually. Selected the new entry and clicked Steam's Play button. The actual shipping game executable started after the bootstrapper exited.
5. User confirmed the first launch worked. On a second observed run, entered the **Juggernaut Edition main menu** with Campaign, Heartland and Daybreak options; no saved campaign was opened or modified.
6. Steam's library explicitly showed **State of Decay 2 — Running** and a **Stop** button. The Friends List self-presence header also displayed **State of Decay 2** in green. Library tracking was confirmed again at the main menu, not only during bootstrap.
7. Used the game's Quit menu and confirmed Yes. Game process disappeared at 13:53:34 CST; launcher logged completion at 13:53:37 CST. Both processes were absent on inspection. Steam's own gameprocess_log recorded removal from its running list at 13:53:39 CST; the library returned to **Play** and Friends List self-presence returned to **Online**.

This verifies local Steam tracking and local Friends List presence. Visibility from another person's Steam account was not tested. No claim is made about Steam Overlay, Steam Input or Remote Play.

Local screenshots of main menu, Steam Running and Steam after exit are retained under ignored `.local/acceptance/`; excluded from repository/release because desktop captures may contain account information. Launcher logs reside under `%LOCALAPPDATA%\GamePass2Steam\logs`.

## Optional GUI Steam restart — passed

Tested PR #7 implementation `d2338c6` on 2026-11-12 with a freshly packaged self-contained build in a stable preview installation folder. The existing State of Decay 2 shortcut and the active Steam profile were used; the shortcut file was copied before testing.

1. With Steam running, selected the existing managed game, clicked **Add / update selected**, and chose **Yes** in the per-operation close/reopen prompt. The GUI displayed its waiting-for-Steam state.
2. A read-only process/file monitor sampled at approximately 100 ms. It observed the original Steam process disappear at 16:03:14.142 CST, the shortcut file change while no Steam process was present at 16:03:14.365, and a new Steam process appear at 16:03:14.493. These are observation times, not exact process-event timestamps.
3. The importer returned to its ready state with controls enabled and a timestamped backup path. The actual before/after files retained the same shortcut count, stored AppID, and all managed metadata except the launch configuration. The unrelated shortcut was preserved byte for byte. The updated executable target exists in the stable preview folder.
4. Steam reopened to its normal **Who's playing?** account chooser. The user selected their account; the library then opened and the managed game displayed **Play**. Reopening Steam does not bypass its account selection or authentication requirements.

This verifies the real successful GUI close/update/reopen path. No force termination was used on Steam. Cancellation, timeout, write failure, and restart failure remain covered by controlled tests rather than deliberately disrupting the live client. This run did not launch the game or exercise artwork downloads. Local process/hash evidence and before/after files are retained under ignored `.local/acceptance/restart-live/`.

## Original release automated checks

`dotnet run --project tests/GamePass2Steam.Tests -c Release`: **12 checks passed**. Covers independent wire-fixture roundtrip, unknown metadata preservation, malformed/truncated/trailing/unsupported binary input, repeat import, user metadata preservation, removal, unsafe identities/quoting, ambiguous tags/indexes, UWP visibility policy, real temp-file backups/restore, no-op imports, running-Steam refusal, concurrent-change refusal, GDK metadata/path containment/DTD rejection, and startup/handoff/exit state.

`dotnet build src/GamePass2Steam -c Release`: **zero warnings and errors**. Tests use temporary profiles. A separate read-only integration comparison validated the actual before/after Steam files.

Windows CI runs 43293713210 and 43294589316 passed; the latter validates implementation commit `181908f`, including all 12 safety checks, self-contained packaging and packaged discovery. The final ZIP was independently extracted into `Game Pass Café` and discovered 42 registered applications, including the first game, with DOTNET_ROOT pointing to a nonexistent directory. A valid but unregistered identity produced an actionable error and the error helper exited automatically without dismissal.

The final release binaries (informational version `0.1.0+181908f`) were installed at the stable Steam target and launched through Play again. They reached the main menu and exited through Quit; the game disappeared at 13:57:35 CST and the helper finished at 13:57:38 CST. No game or launcher processes remained. Bundled licenses and third-party notices are present in `licenses/`.

## Limits

No real UWP game, second GDK title, external publisher launcher, protected/elevated game, Windows 10 machine, ARM64 machine or clean machine without an SDK was tested. Runtime files are bundled, and CI sets DOTNET_ROOT to a nonexistent directory for its packaged smoke test. No extended gameplay, save/load, game update/relocation, controller, overlay or streaming test was performed. The two-minute menu session establishes the first acceptance case, not broad catalog compatibility.

## Audit polling regression (2026-11-13)

The launcher now enumerates candidates by distinct declared executable basenames and reuses one 32K path buffer per poll. Windows native regression checks verified case-insensitive matching of the test executable, duplicate-name suppression, rejection of the same filename in a wrong directory, detection of a real child process, and disappearance after its normal exit. All seven UI/native checks passed. Full game activation, UWP windows, Steam presence, and allocation throughput have not been measured for this change; prior game acceptance results do not validate it.
