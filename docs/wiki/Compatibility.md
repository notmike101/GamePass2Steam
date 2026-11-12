# Compatibility

## What was verified

| Environment or capability | Evidence |
| --- | --- |
| Windows 11 x64, State of Decay 2 | Steam Play launched the installed game through its bootstrapper to the Juggernaut menu. |
| Steam presence | Library showed Running/Stop and Friends showed the game name while the game was open. Normal game exit cleared both after launcher shutdown. |
| Existing shortcuts | The preexisting unrelated shortcut remained unchanged; repeated import did not duplicate the managed entry. |
| Portable runtime | Extracted package discovery worked from a path with spaces and Unicode without external .NET resolution. |
| Core safety behavior | Automated tests cover VDF preservation, malformed input, ownership, backup/restore, concurrent writes and session transitions. |

These are launch, presence and shutdown results, not a full gameplay test. See the [verification record](https://github.com/notmike101/GamePass2Steam/blob/main/docs/VERIFICATION.md) for details.

## Discovery and launch support

- **GDK / packaged Win32:** uses Windows registration for activation and MicrosoftGame.config for the real executable. The game process may differ from its bootstrapper. Registered games on custom drives can be discovered without a fixed XboxGames-folder scan.
- **UWP:** uses registered application identity and manifest executable/package matching. A visible game window is required for tracking; minimized windows remain eligible. This path is implemented but has **not** been verified with a real UWP game.
- **Microsoft Store purchases:** may appear alongside subscription games. Local registration does not prove Game Pass subscription ownership.
- **Full registered-app list:** available when metadata is insufficient to label an app as a likely game. Review your selection; this list includes ordinary applications.

Dependencies, resource-only packages, entries without Start-menu registration or a trackable executable, and DLC without a visible application are excluded. A visible DLC launcher cannot always be distinguished from a game.

## Not supported or not verified

External-launcher-only EA/Ubisoft installations, loose unregistered Win32 executables, ARM64/x86 builds, protected/elevated processes that cannot be inspected, and undeclared differently named executable handoffs are unsupported. Use the official launcher for those cases.

Windows 10, additional game titles and real UWP gameplay have not been integration-tested. Steam Overlay, Steam Input and Remote Play are separate capabilities and are not promised or tested. GamePass2Steam does not inject an overlay, translate controller input, bypass DRM or install games.
