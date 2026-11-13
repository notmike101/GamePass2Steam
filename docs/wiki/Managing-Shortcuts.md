# Managing shortcuts and backups

## Re-import, update and remove

Exit Steam before each change. **Add / update selected** updates existing managed shortcuts without creating duplicates. Existing names, appids, artwork references and other user metadata are preserved. Keep the `GamePass2Steam:` tag; it identifies entries owned by the app. An unrelated shortcut with the same display name is preserved.

**Remove managed selected** removes only selected managed Steam shortcuts, including entries for games that have since been uninstalled. It does not uninstall games or delete saves.

To update the portable app, exit it and games launched through it, then extract the new release into the same folder. If you move the folder, close Steam and re-import from the new location so shortcuts point to the correct EXE. Game updates are resolved from the registered application identity at launch.

To stop using GamePass2Steam, remove its managed shortcuts with Steam closed, exit the app and delete its extracted application folder. Keep any shortcut backups you may want to restore. Local logs are under `%LOCALAPPDATA%\GamePass2Steam\logs`.

## Automatic backups

Shortcut changes affect `<Steam>\userdata\<profile>\config\shortcuts.vdf`. Before changing an existing file the app creates a sibling backup named `shortcuts.vdf.gp2s-<UTC timestamp>-<unique>.bak`. An unchanged import does not create another backup; a first-ever shortcut file has no previous file to back up. Backups are not automatically pruned. Optional [artwork](Artwork) is stored separately in `config/grid`; shortcut snapshots do not include image files.

Writes use a flushed temporary file and atomic replacement. Malformed input and unsupported binary types are refused. Do not run another shortcut editor or restart Steam during a save: Steam offers no transactional shortcut API, so a final race with another writer cannot be eliminated completely.

## Restore a snapshot

1. Exit Steam using **Steam > Exit**.
2. Select the correct Steam profile in GamePass2Steam.
3. Choose **Restore backup** and select the desired `.bak` file.
4. Reopen Steam and inspect your non-Steam library.

Restore replaces the **entire shortcut snapshot**, including other non-Steam shortcuts as they existed in that backup. It first backs up the current file. It does not affect installed games or saves.

If a malformed current file prevents the app from restoring, close Steam, manually copy the current `shortcuts.vdf` somewhere safe, and copy the chosen backup over `shortcuts.vdf`. Retain the `.bak` source until you have checked the result.

Application identities are compared case-insensitively. A registration casing change updates the same managed shortcut and preserves its stored AppID and user metadata. Ambiguous duplicate ownership is still refused.

The name filter updates after a 200 ms typing pause and reads the current shortcuts file on each update. Imports and restores refresh immediately.

Shortcut field names, including the root `shortcuts` name, are accepted case-insensitively and retain their original spelling when saved.
