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
| `Name` | `string` | Required. Display name; the Manager fills it from the file name without its extension |
| `Path` | `string` | Required. Full path to the `.exe` or `.lnk` |
| `IconPath` | `string?` | Declared for a future custom-icon feature; **not read by any current code** |
| `Arguments` | `string?` | Launch arguments; omitted from JSON when null, and not editable in the UI |

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
string SettingsFile { get; }

string GetGroupFile(string groupId);
string GetGroupIconFile(string groupId);
string GetGroupShortcutFile(string groupId);
string GetStartMenuShortcutFile(string sanitizedFileName);
```

The four `Get…` methods validate the group id against `^[A-Za-z0-9._-]{1,96}$` and throw otherwise. Route anything path-derived through them so the validation cannot be bypassed — it blocks traversal and keeps AppUserModelIDs inside their length limit.

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
```

Writes the `.lnk` through `IShellLinkW`/`IPersistFile` and stamps `PKEY_AppUserModel_ID` through `IPropertyStore`.

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

#### [`IShellChangeNotifier`](../src/TaskbarFolders.Core/Shortcuts/IShellChangeNotifier.cs)

```csharp
void NotifyCreate(string path);
```

Wraps `SHChangeNotify` so the shell notices a newly written Start menu shortcut. Without it a programmatic pin can silently fail to persist. The interface exists so call sites stay testable without executing native code.

### Interop

`WindowBackdrop.TryApply(IntPtr hwnd, WindowBackdropKind kind)` requests a DWM system backdrop: `Mica` on the Manager window, `Acrylic` on the popup. A Manager failure is ignored and the window keeps solid brushes; a popup failure switches it to its fallback panel.

`WindowBackdrop.IsSupported` is true from Windows 11 22H2 (build 22621), the first build that honours the backdrop attribute. The popup reads it before its HWND exists, because the backdrop only paints behind a non-layered window. `WindowBackdrop.TrySetRoundedCorners(IntPtr hwnd)` sets `DWMWA_WINDOW_CORNER_PREFERENCE` to round. All three members return `false` rather than throw on a zero handle or an older Windows.

## TaskbarFolders.Manager

#### [`IGroupSyncService`](../src/TaskbarFolders.Manager/Services/IGroupSyncService.cs)

```csharp
Task SyncAsync(GroupConfig config, CancellationToken cancellationToken = default);
void RemoveArtifacts(string groupId, string displayName);
bool EnsureStartMenuShortcut(GroupConfig config);
int RemoveOrphanedStartMenuAnchors();
```

Regenerates icon, shortcut and Start menu anchor for a group. `SyncAsync` returns early for a group with no apps.

Anchors are matched by the AUMID read back through `IShortcutReader`, not by file name: writing an anchor removes any other anchor carrying the same group's AUMID, and `RemoveArtifacts` removes all of them. `RemoveOrphanedStartMenuAnchors` deletes anchors whose AUMID names a group with no `groups\<id>.json`, and leaves files without a TaskbarFolders AUMID alone.

#### [`ILauncherPathResolver`](../src/TaskbarFolders.Manager/Services/ILauncherPathResolver.cs)

```csharp
string? TryResolve();
```

Probes side-by-side, sibling-folder and development layouts in that order, logging every probed path when none match.

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
| [`IProcessRunner`](../src/TaskbarFolders.Manager/Services/IProcessRunner.cs) | `RunAndWaitAsync(ProcessStartInfo, TimeSpan, …)` returning the exit code |
| [`IUserConfirmation`](../src/TaskbarFolders.Manager/Services/IUserConfirmation.cs) | `Confirm()` and `Notify()`, so view models stay free of `MessageBox` |

## TaskbarFolders.Launcher

| Type | Purpose |
|---|---|
| [`LauncherOptions`](../src/TaskbarFolders.Launcher/Configuration/LauncherOptions.cs) | `sealed record LauncherOptions(string GroupId)` |
| `CommandLineParser` | Parses `--group-id <value>` and the `--pin-mode` flag; unknown arguments are ignored |
| [`ICursorAnchor`](../src/TaskbarFolders.Launcher/Services/ICursorAnchor.cs) | `Seed(Point)` / `Position` — the click position in **device pixels** |
| [`ITaskbarPositionHelper`](../src/TaskbarFolders.Launcher/Services/ITaskbarPositionHelper.cs) | `ComputePlacement(Size popupSize, PopupPositionPreference)` returning a `PopupPlacement(double Left, double Top)` in DIPs |
| [`TaskbarEdge`](../src/TaskbarFolders.Launcher/Services/TaskbarEdge.cs) | `Left`, `Top`, `Right`, `Bottom` |
| [`IProcessLauncher`](../src/TaskbarFolders.Launcher/Services/IProcessLauncher.cs) | `bool Launch(string path, string? arguments)` |
| `TaskbarPinRunner` | Drives the WinRT pin request in pin mode and maps the outcome to an exit code |
| `StartupFailureLogger` | Dependency-free, never-throwing logger for failure paths that occur before or instead of the DI graph |

`ICursorAnchor` carries device pixels and `ComputePlacement` returns DIPs. That single conversion is the contract — see [ADR-003](adr/003-dpi-unit-contract.md).
