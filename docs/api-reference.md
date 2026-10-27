# API Reference

A map of the public surface, organised by project. It is a guide to what exists and what each abstraction is for — the XML documentation on the types themselves is the authority on individual parameters.

Every type below links to its source file.

## TaskbarFolders.Shared

Models, persistence and logging. No Windows-only APIs.

### Models

#### [`AppEntry`](../src/TaskbarFolders.Shared/Models/AppEntry.cs)

One application inside a group.

| Property | Type | Notes |
|---|---|---|
| `Name` | `string` | Required. Display name, filled from the file name without its extension |
| `Path` | `string` | Required. Full path to the `.exe` or `.lnk` |
| `IconPath` | `string?` | Declared for a future custom-icon feature; **not read by any current code** |
| `Arguments` | `string?` | Launch arguments; omitted from JSON when null, and not editable in the UI |

`static bool TryCreateFromPath(string? path, out AppEntry? entry)` is the one rule for turning a picked or dropped file into an entry: `.exe` and `.lnk` only, any case, named after the file. Everything that adds apps goes through it.

#### [`GroupConfig`](../src/TaskbarFolders.Shared/Models/GroupConfig.cs)

One group.

| Property | Type | Notes |
|---|---|---|
| `Id` | `string` | New groups get a GUID. **Reconstructed from the file name on load** — the `id` field in the JSON is ignored |
| `GroupName` | `string` | Display name |
| `Columns` | `int` | Popup grid columns, clamped to 1–6 on assignment, default 3. No UI |
| `Theme` | `ThemePreference` | Serialised but **not read**; both applications use the global setting |
| `Apps` | `List<AppEntry>` | Members of the group |

#### [`AppSettings`](../src/TaskbarFolders.Shared/Models/AppSettings.cs)

| Property | Type | Default |
|---|---|---|
| `AutoStart` | `bool` | `false` |
| `Theme` | `ThemePreference` | `System` |
| `EnableAnimations` | `bool` | `true` |
| `PopupPosition` | `PopupPositionPreference` | `Auto` |

On load the registry, not this file, is the source of truth for `AutoStart`.

#### [`Preferences`](../src/TaskbarFolders.Shared/Models/Preferences.cs)

`ThemePreference` — `System`, `Light`, `Dark`. `PopupPositionPreference` — `Auto`, `Above`, `Below`. Both serialise as camel-case strings.

### Configuration

#### [`IGroupConfigStore`](../src/TaskbarFolders.Shared/Configuration/IGroupConfigStore.cs)

```csharp
Task<IReadOnlyList<GroupConfig>> LoadAllAsync(CancellationToken cancellationToken = default);
Task<GroupConfig?> LoadAsync(string groupId, CancellationToken cancellationToken = default);
Task SaveAsync(GroupConfig config, CancellationToken cancellationToken = default);
Task DeleteAsync(string groupId, CancellationToken cancellationToken = default);
```

Implemented by `JsonGroupConfigStore`, one file per group, written atomically.

#### [`IAppSettingsStore`](../src/TaskbarFolders.Shared/Configuration/IAppSettingsStore.cs)

```csharp
Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
```

#### [`IAppDataPathProvider`](../src/TaskbarFolders.Shared/Configuration/IAppDataPathProvider.cs)

Every path the applications use, in one place.

```csharp
string AppDataRoot { get; }
string GroupsDirectory { get; }
string IconsDirectory { get; }
string LogsDirectory { get; }
string ShortcutsDirectory { get; }
string StartMenuDirectory { get; }
string PinnedTaskbarDirectory { get; }
string SettingsFile { get; }

string GetGroupFile(string groupId);
string GetGroupIconFile(string groupId);                  // legacy <id>.ico
string GetGroupIconFile(string groupId, string version); // <id>-<8 hex>.ico
bool IsGroupIconFile(string groupId, string path);
string GetGroupShortcutFile(string groupId);
string GetStartMenuShortcutFile(string sanitizedFileName);
```

`PinnedTaskbarDirectory` is the shell's `User Pinned\TaskBar` folder in production and a folder under the base directory in every test constructor. The `Get…` and `IsGroupIconFile` methods validate the group id against `^[A-Za-z0-9._-]{1,96}$` and throw otherwise. Route anything path-derived through them so the validation cannot be bypassed — it blocks traversal and keeps AppUserModelIDs inside their length limit.

### Logging

`FileLoggerProvider` writes one file per day per process kind (`manager-<date>.log`, `launcher-<date>.log`), at `Information` and above, and prunes files older than 14 days on a background task. Line format is `<ISO-8601 UTC> [<Level>] <Category>: <message>`, with the exception on the following line.

## TaskbarFolders.Core

Icon engine, interop and shortcut generation. Windows-only.

### Icons

#### [`IIconExtractor`](../src/TaskbarFolders.Core/Icons/IIconExtractor.cs)

```csharp
BitmapSource? ExtractIcon(string filePath, int size = 256);
```

Implemented by `ShellIconExtractor` via `SHGetFileInfo` and `IImageList`. Shortcuts are resolved to their target first. Returns `null` rather than throwing when extraction fails.

#### [`ICompositeIconGenerator`](../src/TaskbarFolders.Core/Icons/ICompositeIconGenerator.cs)

```csharp
BitmapSource GenerateComposite(IReadOnlyList<BitmapSource> icons, int outputSize = 256);
```

Implemented by `CompositeIconGenerator`. Uses at most the first four icons: one centred, two side by side, three in an iOS-style arrangement, four as a 2×2 grid.

#### [`PlaceholderIcon`](../src/TaskbarFolders.Core/Icons/PlaceholderIcon.cs)

```csharp
public const double DesignSize = 24;
public const double StrokeThickness = 1.5;
public static readonly Color NeutralColor;
public static Geometry Shape { get; }
public static BitmapSource Render(int size, Color color);
```

The icon shown for an app that has none: a window outline in a `DesignSize` square, frozen. XAML strokes `Shape` with a theme brush; `Render` draws it into a frozen bitmap for the composite, in `NeutralColor` there because the taskbar follows the system theme, not the app's.

#### [`IIcoFileWriter`](../src/TaskbarFolders.Core/Icons/IIcoFileWriter.cs)

```csharp
Task WriteAsync(BitmapSource source, string targetPath, CancellationToken cancellationToken = default);
```

Writes a multi-resolution PNG-in-ICO container at 16, 32, 48 and 256 px, atomically.

#### [`IIconCache`](../src/TaskbarFolders.Core/Icons/IIconCache.cs)

```csharp
bool TryGet(string sourcePath, int size, [NotNullWhen(true)] out BitmapSource? icon);
void Set(string sourcePath, int size, BitmapSource icon);
void StartBackgroundPrune() { }
```

`TryGet`/`Set` are synchronous **on purpose** — they sit on the popup's hot path. `FileSystemIconCache` keys entries by source path, last-write time and size, and prunes after 30 days.

### Shortcuts

#### [`IShortcutGenerator`](../src/TaskbarFolders.Core/Shortcuts/IShortcutGenerator.cs)

```csharp
string BuildAumid(string groupId);
void Generate(GroupShortcutRequest request);
bool TryUpdateIcon(string shortcutPath, string iconPath);
```

Writes the `.lnk` through `IShellLinkW`/`IPersistFile` and stamps `PKEY_AppUserModel_ID` through `IPropertyStore`. `TryUpdateIcon` loads an existing shortcut, changes only its icon location and saves it in place — used on the taskbar's pinned copies; `false`, never an exception, when the file is missing or cannot be rewritten.

#### [`GroupShortcutRequest`](../src/TaskbarFolders.Core/Shortcuts/GroupShortcutRequest.cs)

```csharp
public sealed record GroupShortcutRequest(
    string GroupId,
    string DisplayName,
    string TargetExePath,
    string IconPath,
    string ShortcutPath);
```

#### [`GroupAumid`](../src/TaskbarFolders.Core/Shortcuts/GroupAumid.cs)

```csharp
public const string Prefix = "TaskbarFolders.Group.";
public static string For(string groupId);
public static bool TryExtractGroupId(string? aumid, out string groupId);
```

`TryExtractGroupId` is how the launcher recovers its group when Windows starts it without arguments.

#### [`IShortcutReader`](../src/TaskbarFolders.Core/Shortcuts/IShortcutReader.cs)

```csharp
string? TryReadAumid(string shortcutPath);
```

The read counterpart to `IShortcutGenerator`. Returns `null` — never throws — when the shortcut carries no AppUserModelID, does not exist, or cannot be read. Used to verify that a pin actually landed: Windows copies the shortcut it resolved into its own pinned-items folder under a name of its choosing, so the AUMID is the only stable identity across the programmatic and manual pin routes.

#### [`ITaskbarUnpinner`](../src/TaskbarFolders.Core/Shortcuts/ITaskbarUnpinner.cs)

`bool TryUnpin(string pinnedShortcutPath)` invokes the shell's `taskbarunpin` verb — by canonical name, never the localized caption — on a shortcut in the taskbar's pinned-items folder, through `IShellItem` → `IContextMenu`. The shell then removes the tile and the file. Works from a normal user process; there is no public API for this. Default `ShellTaskbarUnpinner`; only the uninstaller calls it.

#### [`IShellChangeNotifier`](../src/TaskbarFolders.Core/Shortcuts/IShellChangeNotifier.cs)

```csharp
void NotifyCreate(string path);
```

Also `void NotifyUpdate(string path)` (`SHCNE_UPDATEITEM`, flushed), sent after a pinned copy's icon changes. Wraps `SHChangeNotify` so the shell notices a newly written Start menu shortcut. Without it a programmatic pin can silently fail to persist. The interface exists so call sites stay testable without executing native code.

### Groups

#### [`IGroupSyncService`](../src/TaskbarFolders.Core/Groups/IGroupSyncService.cs)

```csharp
Task SyncAsync(GroupConfig config, CancellationToken cancellationToken = default);
void RemoveArtifacts(string groupId, string displayName);
bool EnsureStartMenuShortcut(GroupConfig config);
int RemoveOrphanedStartMenuAnchors();
```

Regenerates icon, shortcut and Start menu anchor for a group. `SyncAsync` returns early for a group with no apps. Lives in Core so that both executables that change a group can run it.

Anchors are matched by the AUMID read back through `IShortcutReader`, not by file name: writing an anchor removes any other anchor carrying the same group's AUMID, and `RemoveArtifacts` removes all of them. `RemoveOrphanedStartMenuAnchors` deletes anchors whose AUMID names a group with no `groups\<id>.json`, and leaves files without a TaskbarFolders AUMID alone.

#### [`ILauncherPathResolver`](../src/TaskbarFolders.Core/Groups/ILauncherPathResolver.cs)

```csharp
string? TryResolve();
```

The launcher executable a group's shortcuts target, or `null` when it cannot be found — sync then logs and skips the shortcut. Each executable supplies its own implementation (see the Manager section).

### Interop

`WindowBackdrop.TryApply(IntPtr hwnd, WindowBackdropKind kind)` requests a DWM system backdrop: `Mica` on the Manager window, `Acrylic` on the popup. A Manager failure is ignored and the window keeps solid brushes; a popup failure switches it to its fallback panel.

`WindowBackdrop.IsSupported` is true from Windows 11 22H2 (build 22621), the first build that honours the backdrop attribute. The popup reads it before its HWND exists, because the backdrop only paints behind a non-layered window. `WindowBackdrop.TrySetRoundedCorners(IntPtr hwnd)` sets `DWMWA_WINDOW_CORNER_PREFERENCE` to round. All three members return `false` rather than throw on a zero handle or an older Windows.

## TaskbarFolders.Manager

#### [`ExplorerContextMenuService`](../src/TaskbarFolders.Manager/Services/ExplorerContextMenuService.cs)

```csharp
Task RefreshAsync(CancellationToken cancellationToken = default);
void Write(IReadOnlyList<GroupConfig> groups, string? launcherPath);
```

Keeps Explorer's *Taskbar Groups* menu in step with the groups and the `AppSettings.ShowInExplorerMenu` setting. `RefreshAsync` reads both stores and the launcher path, logs and swallows registry failures. `Write` deletes the submenu for both file classes and writes it again, or leaves it deleted for no groups or no launcher. Registry access goes through `IRegistryWriter` (`DeleteTree`, `SetString`, `GetValueNames`, `DeleteValue` on HKCU; `CurrentUserRegistryWriter` in production). `MainWindowViewModel.GroupListChanged` and `SettingsViewModel.Saved` trigger it.

#### [`IUninstaller`](../src/TaskbarFolders.Manager/Services/IUninstaller.cs)

`Task<UninstallReport> RunAsync(CancellationToken)` removes every trace outside the program folder, in this order: ends the other TaskbarFolders processes of the session (`IInstanceCloser`), unpins tiles whose pinned shortcut carries a group AUMID (`ITaskbarUnpinner`), turns the menu and autostart settings off, removes the Run entry, the Explorer menu and each group's artifacts (`IGroupSyncService.RemoveArtifacts`), deletes the `WindowsRecordKeys` values for which `IsOurRecord(name, installRoot)` holds, deletes `%APPDATA%\TaskbarFolders`, then sweeps orphaned Start menu anchors and the empty Start menu folder. Every step runs even if an earlier one failed. `UninstallReport(bool Complete, int TilesLeftPinned)`. Default implementation `Uninstaller`.

`static bool IsOurRecord(string valueName, string installRoot)` — true for a value name that starts with `installRoot` plus a separator, or with `GroupAumid.Prefix`.

[`UninstallArguments`](../src/TaskbarFolders.Manager/Services/UninstallArguments.cs) is the command line around it: `TaskbarFolders.Manager.exe --uninstall [--no-confirm]` runs it with no main window (`App`), asking first unless `--no-confirm`, and reporting always without it, only problems with it. Exit codes `ExitComplete` 0, `ExitIncomplete` 1, `ExitCancelled` 2.

[`InstallLocation`](../src/TaskbarFolders.Manager/Services/InstallLocation.cs)`(string Root)` is the folder holding `Manager` and `Launcher` (`OfRunningManager()`); `FindUninstaller()` returns `unins000.exe` or `Uninstall.cmd` in it, or `null` for a development build. `SettingsViewModel.UninstallCommand` starts it through `IProcessRunner.TryStart` and raises `UninstallStarted`, on which `App` exits.

[`IInstanceCloser`](../src/TaskbarFolders.Manager/Services/IInstanceCloser.cs) — `int CloseOthers()` ends every Manager but this process and every Launcher in the current session, returning how many would not end. Default `ProcessInstanceCloser`.

#### [`LauncherPathResolver`](../src/TaskbarFolders.Manager/Services/LauncherPathResolver.cs)

The Manager's `ILauncherPathResolver` (see Core). Probes side-by-side, sibling-folder and development layouts in that order, logging every probed path when none match.

#### [`IPinToTaskbarService`](../src/TaskbarFolders.Manager/Services/IPinToTaskbarService.cs)

```csharp
Task<PinResult> PinAsync(string groupId, CancellationToken cancellationToken = default);
```

`PinResult` is `Success`, `UserDenied`, `Unsupported`, `Error` or `NotVerified`. Implemented by running the launcher in pin mode and mapping its exit code.

`NotVerified` means Windows reported a successful pin but no pinned shortcut carrying the group's AppUserModelID was found afterwards — reported separately because claiming success with no tile behind it is indistinguishable from a broken application.

#### Supporting services

| Interface | Purpose |
|---|---|
| [`IAutoStartService`](../src/TaskbarFolders.Manager/Services/IAutoStartService.cs) | `IsEnabled`, `Enable()`, `Disable()` over the per-user Run key |
| [`ISystemThemeProbe`](../src/TaskbarFolders.Manager/Services/ISystemThemeProbe.cs) | `IsLightMode` from the Windows personalisation setting |
| [`IThemeService`](../src/TaskbarFolders.Manager/Services/IThemeService.cs) | `Preference`, `EffectiveTheme`, `SetPreference()`; subscribes to system theme changes only while the preference is `System` |
| [`IProcessRunner`](../src/TaskbarFolders.Manager/Services/IProcessRunner.cs) | `RunAndWaitAsync(ProcessStartInfo, TimeSpan, …)` returning the exit code; `TryStart(ProcessStartInfo)` without waiting |
| [`IUserConfirmation`](../src/TaskbarFolders.Manager/Services/IUserConfirmation.cs) | `Confirm()` and `Notify()`, so view models stay free of `MessageBox` |

## TaskbarFolders.Launcher

| Type | Purpose |
|---|---|
| [`LauncherOptions`](../src/TaskbarFolders.Launcher/Configuration/LauncherOptions.cs) | `sealed record LauncherOptions(string GroupId)` |
| `CommandLineParser` | Parses `--group-id <value>` and the `--pin-mode` flag; `ParseFilesToAdd` returns the positional arguments (files to add); unknown `--` flags are ignored |
| [`CommandLineAppAdder`](../src/TaskbarFolders.Launcher/Services/CommandLineAppAdder.cs) | `Task<int> AddAsync(IReadOnlyList<string>)`: adds the accepted, not-yet-present files to the group under a per-group named semaphore, then saves and syncs |
| [`PopupPresence`](../src/TaskbarFolders.Launcher/Services/PopupPresence.cs) | Per-group named event: `Claim(id)` with `IsFirst`, `SignalReload()`, `ListenForReload(Action)` — one popup per group when several launchers add files at once |
| [`ICursorAnchor`](../src/TaskbarFolders.Launcher/Services/ICursorAnchor.cs) | `Seed(Point)` / `Position` — the click position in **device pixels** |
| [`ITaskbarPositionHelper`](../src/TaskbarFolders.Launcher/Services/ITaskbarPositionHelper.cs) | `ComputePlacement(Size popupSize, PopupPositionPreference)` returning a `PopupPlacement(double Left, double Top)` in DIPs |
| [`TaskbarEdge`](../src/TaskbarFolders.Launcher/Services/TaskbarEdge.cs) | `Left`, `Top`, `Right`, `Bottom` |
| [`IProcessLauncher`](../src/TaskbarFolders.Launcher/Services/IProcessLauncher.cs) | `bool Launch(string path, string? arguments)` |
| `TaskbarPinRunner` | Drives the WinRT pin request in pin mode and maps the outcome to an exit code |
| `StartupFailureLogger` | Dependency-free, never-throwing logger for failure paths that occur before or instead of the DI graph |
| [`SelfLauncherPathResolver`](../src/TaskbarFolders.Launcher/Services/SelfLauncherPathResolver.cs) | The launcher's `ILauncherPathResolver`: `Environment.ProcessPath`, since the launcher is the shortcut target |
| [`OutsideClickWatcher`](../src/TaskbarFolders.Launcher/Services/OutsideClickWatcher.cs) | `IDisposable` low-level mouse hook calling back `Action<int, int>` with the position of any button press outside a window; `IsInstalled`; pure `static bool IsOutside(x, y, left, top, right, bottom)` in **device pixels**, right/bottom exclusive |
| [`TileToggle`](../src/TaskbarFolders.Launcher/Services/TileToggle.cs) | Per-group `Local\TaskbarFolders.Group.<id>.Dismissed` marker: `MarkDismissed(id)` by a popup closed from its own tile, `TryConsume(id)` by the next plain launcher (which then shows nothing), `WaitForNextLaunch(timeout)`; pure `IsOnOpeningTile(anchor, x, y, dpiScale)` within `ToleranceDip` (40); `Window` 1.5 s. `PopupWindow.OutsidePress` (`OutsidePressEventArgs`: device-pixel `Position`, settable `IsOnOpeningTile`) feeds it |
| `PopupViewModel` | Besides loading the group and `LaunchAppCommand`: `CloseCommand` raises `CloseRequested`, mirroring `LaunchSucceeded`; the window closes on either. `Task<int> AddAppsAsync(IEnumerable<string>)` adds what `AppEntry.TryCreateFromPath` accepts, saves and runs `IGroupSyncService.SyncAsync`; a failure lands in `LastError`. `CanEditGroup` is false for a group that could not be loaded. Edit mode: `IsEditing`, `ToggleEditCommand` (stays off with nothing to edit), `RemoveAppCommand(PopupAppViewModel)` removes the entry, saves and syncs, and leaves edit mode when the last app goes; `LaunchAppCommand` does nothing while editing. `IsPinned` / `TogglePinCommand`: pinned for this popup only — the window then stays open after a launch and ignores outside clicks, focus loss and the activation fallback (`PopupWindow.DismissesOnOutsideInteraction`). `ReloadAsync()` rebuilds the tiles from the stored group after another launcher changed it |

`ICursorAnchor` carries device pixels and `ComputePlacement` returns DIPs. That single conversion is the contract — see [ADR-003](adr/003-dpi-unit-contract.md).
