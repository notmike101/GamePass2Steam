# Research and integration decisions — 2026-11-12

## UWPHook

Reviewed upstream master `AppManager.cs`, `SteamManager.cs`, README and MIT license. [UWPHook](https://github.com/BrianLima/UWPHook) discovers registered apps through PowerShell, uses IApplicationActivationManager, exports non-Steam shortcuts and follows the activation PID. Its fallback searches process path/name and waits a configurable delay before another search. `SteamManager.WriteShortcuts` writes serialized bytes directly to the destination. README explicitly discusses overlay limitations. These are source observations, not claims that every current issue is reproduced here.

Concrete improvements implemented: GDK real executable metadata rather than treating GameLaunchHelper as the game; identity plus full-path matching rather than substring/process-name matching; multiple PID tracking; bounded startup and process-driven exit; preserving typed fields and user metadata; duplicate ownership marker; atomic replacement with backups and refusal while Steam runs; structured JSON discovery rather than delimiter-separated text; launch-time rediscovery instead of versioned executable arguments in shortcuts. No UWPHook implementation code is copied; Windows ABI declarations follow Microsoft documentation. Its MIT license is included as reference attribution.

## Documented Windows interfaces

- [ActivateApplication](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication) launches a registered app in the current session and returns an activation process ID. This may be a bootstrapper, so the ID is logged, not accepted as game-lifetime evidence.
- [GetPackageFamilyName](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagefamilyname) identifies process package family using query-limited access. It supports matching a versioned WindowsApps alias to the game on a custom drive, with the expected executable filename as an additional constraint.
- [GDK launching on PC](https://learn.microsoft.com/en-us/gaming/gdk/docs/tools/tools-pc/launching-on-pc) distinguishes registered shell activation, execution aliases and direct executable launch. Direct launch is viable for properly configured modern GDK titles, but this app uses registered activation to preserve the retail bootstrapper path, including older configurations.
- [MicrosoftGame.config executable metadata](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-executable) supplies executable identity, path and development-only flags. The installed first case declares a real shipping executable while AppxManifest declares GameLaunchHelper.exe.
- [Self-contained .NET distribution](https://learn.microsoft.com/en-us/dotnet/core/deploying/) bundles the runtime. Folder publication avoids single-file extraction overhead and native extraction policy issues. Current docs were also fetched through the Context7 CLI `/dotnet/docs`.

## Undocumented / observed boundaries

Steam binary KeyValues shortcuts.vdf, its appid convention and managed tags are compatibility conventions, not a supported Valve shortcut-management API. The parser retains raw supported scalar values and ordered nested fields; unsupported types are refused. Stable CRC32 IDs have the high bit set and collisions are explicitly refused. It preserves existing appids, names and user metadata on update. Windows SteamPath/ActiveProcess registry entries are observed client conventions, not Windows guarantees.

Get-StartApps plus current-user package registration covers installed visible package applications, including registered GDK titles on custom drives. It cannot determine subscription entitlement or discover every external publisher's launcher. Rather than speculate about GamingServices' internal registry format, the fallback matches a selected manifest to an already registered application. Nothing takes ownership of WindowsApps or reads credentials.

## Architecture tradeoff

C# is chosen over the preferred JS/Node/pnpm stack because the required UI, COM activation, process handles and Windows paths fit one framework and one distributable runtime. Electron plus a native helper would increase update and lifetime coordination without adding required functionality. The project therefore has no JavaScript files and does not need the requested JS/JSDoc ESLint configs. The machine's pnpm shim was broken during inspection; this was not the reason for choosing C# and no global pnpm settings were changed.
