# Architecture

How TaskbarFolders is put together, and why. For the reasoning behind individual decisions see the [ADRs](adr/README.md).

## System overview

Two executables and two libraries. Dependencies flow one way: Manager and Launcher both depend on Core and Shared; Core depends on Shared; Shared depends on nothing of ours.

```mermaid
graph TB
    subgraph Windows
        Taskbar[Taskbar tile]
        StartMenu[Start menu index]
    end

    subgraph Applications
        Manager[Manager<br/>WPF - group management]
        Launcher[Launcher<br/>WPF - one popup per click]
    end

    subgraph Libraries
        Core[Core<br/>icons, interop, shortcuts]
        Shared[Shared<br/>models, persistence, logging]
    end

    subgraph "%APPDATA%/TaskbarFolders"
        Groups[groups/*.json]
        Icons[icons/*.ico + cache]
        Shortcuts[shortcuts/*.lnk]
        Settings[settings.json]
        Logs[logs/*.log]
    end

    Manager --> Core --> Shared
    Launcher --> Core
    Manager -->|writes| Groups
    Manager -->|generates| Icons
    Manager -->|generates| Shortcuts
    Manager -->|anchors| StartMenu
    Launcher -->|reads, edits own group| Groups
    Launcher -->|regenerates| Icons
    Shortcuts --> Taskbar
    Taskbar -->|click| Launcher
```

## Components

| Project | Role | Constraint |
|---|---|---|
| `TaskbarFolders.Shared` | Models, JSON persistence with atomic writes, path provider, file logging | No Windows-only APIs — kept portable |
| `TaskbarFolders.Core` | Icon extraction and composition, Win32/COM interop, shortcut generation, shell change notification, window backdrop, group sync (`GroupSyncService`) | `[SupportedOSPlatform("windows")]` |
| `TaskbarFolders.Manager` | WPF application for group CRUD, settings, pinning | DI graph in `ManagerServiceCollectionExtensions` |
| `TaskbarFolders.Launcher` | Short-lived popup process, one per tile click; also the pin runner | DI graph in `LauncherServiceCollectionExtensions` |

The Launcher targets `net8.0-windows10.0.19041.0` rather than `net8.0-windows`; it needs the Windows 10 1903+ SDK projections to reach `Windows.UI.Shell.TaskbarManager`. Everything else targets `net8.0-windows`. This asymmetry is load-bearing — see [Locating the launcher](#locating-the-launcher).

## Grouping: one launcher, many identities

There is exactly **one** `TaskbarFolders.Launcher.exe`. Groups are not separate binaries; they are separate *shell identities* pointing at that one binary.

Each group gets a `.lnk` in `%APPDATA%\TaskbarFolders\shortcuts\` that:

- targets the shared launcher executable,
- passes `--group-id "<id>"` as its arguments,
- takes its icon from the group's generated `.ico`,
- and carries `PKEY_AppUserModel_ID` = `TaskbarFolders.Group.<id>`, written through `IPropertyStore`.

Windows groups taskbar buttons by AppUserModelID, so distinct AUMIDs give each group its own tile even though every tile starts the same executable. The launcher recovers the group id from `--group-id`, falling back to `GetCurrentProcessExplicitAppUserModelID` and stripping the prefix when Windows starts it without arguments.

The rejected alternative — a copy of the launcher per group with the icon written into the PE via `UpdateResource` — is recorded in [ADR-002](adr/002-per-group-lnk-aumid.md). Unsigned, dynamically modified executables trip Defender heuristics.

### The Start menu anchor

A second copy of each shortcut is written to `%APPDATA%\Microsoft\Windows\Start Menu\Programs\TaskbarFolders\`, followed by `SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSH, …)`.

This is not decoration. `TaskbarManager.RequestPinCurrentAppAsync` trusts the calling AUMID and can report success while persisting nothing if the shell's AppsFolder index has not yet seen a matching shortcut. The anchor plus the change notification is what makes a programmatic pin stick. `GroupSyncService` also re-checks every group's anchor on Manager startup, so configurations written by older versions heal themselves.

The anchor's file name follows the group's display name, but its identity is the AUMID, which follows the id. Anchors are therefore located by reading their AUMID back, never by name: writing a group's anchor removes any other anchor with the same AUMID (the one left by a rename), deleting a group removes every anchor with its AUMID, and on startup anchors whose AUMID names no `groups\<id>.json` are deleted. The known ids come from the group file names rather than the loaded configs, so a group whose JSON fails to parse keeps its anchor and its pin. Duplicate anchors for one AUMID are a plausible cause of a pin that reports success but leaves no tile.

## Data flow

### Creating and syncing a group

1. The Manager creates a `GroupConfig` with a GUID id and writes `groups/<id>.json`. Writes go to a `.tmp` file and are then moved over the target, so a crash mid-write cannot truncate a config.
2. Adding apps triggers a sync. Sync **returns early for an empty group** — no apps means no icon and no shortcut, and therefore nothing to pin.
3. Icons for the first four apps are extracted at 128 px through the icon cache. `.lnk` inputs are resolved to their targets first.
4. Those icons are composed into one image: one centred, two side by side, three in an iOS-style arrangement, four as a 2×2 grid.
5. The composite is written as a multi-resolution PNG-in-ICO container (16/32/48/256 px) to `icons/<id>-<v>.ico`, again atomically. `<v>` is eight hex digits of a hash over the image: Windows caches a shortcut's icon by path, so a changed image needs a new file name or the pinned tile keeps the old one ([ADR-006](adr/006-versioned-group-icons.md)).
6. The `.lnk` is generated with the AUMID and icon reference described above, and the Start menu anchor is written and announced.
7. The taskbar's pinned copies of the group (the `.lnk` files in `User Pinned\TaskBar` carrying its AUMID) get their icon location — only that — pointed at the new file, with `SHCNE_UPDATEITEM`. Then the group's older icon files are deleted.

If every icon extraction fails, sync stops before writing a shortcut rather than producing a group tile with no icon.

The popup is the second writer ([ADR-005](adr/005-popup-edits-its-group.md)): adding apps with its **+** or removing them in its edit mode saves the group through the same store and runs the same GroupSyncService, which lives in Core for that reason. The launcher resolves the shortcut target as its own executable (SelfLauncherPathResolver). While the file picker it opens is up, the popup's dismissal triggers — outside click, focus loss, the never-activated fallback — are suspended, or it would close under its own dialog.

Files can also reach a group on the launcher's command line — `--group-id <id> <file>…`, which is what Explorer's *Taskbar Groups* menu runs and what a file dropped on the group's taskbar tile turns into. `CommandLineAppAdder` adds them inside a per-group, machine-wide lock (a named semaphore, since the work awaits), skipping files already in the group, then saves and syncs. Explorer starts one launcher per selected file, so a multi-selection is many launchers at once: each claims the group's `PopupPresence` event before adding anything, the one that created it shows the popup, and the others add their file, signal the event and exit; the open popup reloads its tiles on that signal.

The Manager keeps every group in memory, so it re-reads the store whenever its window is activated and rebuilds the list — reselecting the previous group by id — only if some group differs from the copy in memory. Without that, a change made outside the Manager while it was open would be overwritten by its next save of the same group.

### Pinning

Two paths, both offered in the UI.

**One-click.** The Manager spawns the launcher as `TaskbarFolders.Launcher.exe --pin-mode --group-id <id>` and waits up to two minutes for it to exit. In pin mode the launcher stamps its own AUMID, shows a 1×1 transparent host window so the WinRT dialog has a foreground parent, waits briefly for the shell index to settle, and calls `RequestPinCurrentAppAsync`. Windows displays its own consent dialog — that step cannot be automated. The child's exit code carries the outcome back:

| Exit code | Meaning |
|---|---|
| 0 | Pinned |
| 1 | The user declined |
| 2 | Pinning unavailable — `TaskbarManager` absent, or blocked by edition or policy |
| 3 | Unexpected failure |
| 5 | Reported as pinned, but no pinned shortcut carrying the group's AUMID was found |

On codes 2, 3 and 5 the Manager opens the shortcut folder so the user can pin manually.

Because the exit code *is* the result, pin mode runs with `ShutdownMode.OnExplicitShutdown`. Under WPF's default, closing the host window — the last window — starts a shutdown with code 0, and a `Shutdown(exitCode)` issued afterwards is ignored. Until that was fixed the Manager received 0 for every outcome and reported "Pinned" even when pinning was unavailable, declined or unverified.

**Manual.** **Show shortcut…** opens Explorer with the group's `.lnk` selected, for a right-click → *Pin to taskbar*.

### Clicking a pinned group

The taskbar starts the launcher. Startup order matters and is deliberate:

1. Set per-monitor-V2 DPI awareness.
2. Capture the cursor position — after the DPI call, so the coordinates are genuinely physical. If the call fails, fall back to the centre of the primary screen.
3. Stamp the AUMID unless one was inherited.
4. Load settings, build the DI graph, seed the cursor anchor, apply the theme.
5. Load the group, show the popup, activate it, then start icon extraction in the background.

Names appear immediately; icons stream in afterwards on the thread pool. Each `BitmapSource` is frozen inside the producer before the continuation crosses back to the UI thread — an unfrozen bitmap carries the wrong dispatcher affinity and the WPF binding throws.

The popup closes on a click outside it, Escape, its ✕ button, focus loss, a successful launch, or via a three-second fallback if Windows never granted it activation. A *failed* launch keeps it open and shows an inline error strip.

All of these end in `Window.Close()`, and `OnClosing` is the single place that applies the fade-out: with animations on it cancels the first request, plays `CloseAnimation` on `ChromeRoot` (about 120 ms), and closes for real on `Completed` or after a 300 ms safety net. Clicks outside are detected by `OutsideClickWatcher`, a process-local `WH_MOUSE_LL` hook installed on `ContentRendered` and removed when the popup closes, because `Deactivated` only fires for a popup that was activated in the first place.

The popup has two window modes, chosen in its constructor before the HWND exists. On Windows 11 22H2+ it is a non-layered window whose `WindowChrome` glass frame covers the client area, with DWM acrylic behind it, tinted by the theme, and rounded corners. Elsewhere it stays a layered (`AllowsTransparency`) window and paints a translucent fallback panel, because DWM never draws a backdrop behind a layered window. See [ADR-004](adr/004-popup-acrylic-backdrop.md).

Popup width comes from the column count, but height is *measured*, not calculated: the strip docks to the bottom of a fill-last `DockPanel`, so at a height derived from tile rows alone it takes its space out of the fixed 96 px tiles and clips the last row. Sizing therefore measures `ChromeRoot` and clamps its `DesiredSize` between `MinHeight` and `MaxHeight`, which accounts for the strip without any code knowing it exists. Every time `LastError` appears or clears, the window is resized and placement recomputed so the popup still clears the taskbar.

The strip's `Visibility` is a plain `OneWay` binding and must stay one. Writing that property from code — to force the strip visible for a measure pass, say — replaces the binding with a local value and discards the expression permanently; `ClearValue` then removes only the local value, and `Visibility` falls back to its default, `Visible`. The strip survives as an empty red bar and clips the tiles again on the next click, because `LaunchApp` clears `LastError` before every attempt. Where the binding's timing matters, `BindingExpression.UpdateTarget()` pushes the current source value through without taking the property over.

### Launcher exit codes

Popup-mode startup uses its own codes, all written to `logs/launcher-*.log` before shutdown:

| Exit code | Meaning |
|---|---|
| 1 | No group id — neither `--group-id` nor a usable AUMID |
| 3 | Startup threw |
| 4 | Unhandled dispatcher exception after startup |

Every failure path goes through a dependency-free startup logger rather than `Trace`. This is a direct response to the v0.4.x regression in which every tile click exited with code 3 for weeks while the log stayed empty.

## Popup placement and the DPI contract

Everything crossing the Win32 boundary — taskbar and monitor rectangles, the cursor position, the anchor — is in **device pixels**. Placement converts to WPF device-independent pixels exactly once, using the effective DPI of the monitor under the cursor.

Do not pre-convert values before seeding the anchor, and do not feed DIP values into the Win32 parameters. A second conversion reintroduces the placement drift that made the popup land far from the tile at 125 % and 150 % scaling. See [ADR-003](adr/003-dpi-unit-contract.md).

Placement itself: find the taskbar edge and rectangle, find the monitor under the cursor, centre the popup on the click position along the taskbar axis, offset it 8 DIP from the taskbar edge, then clamp it inside the work area.

## Storage layout

| Path under `%APPDATA%\TaskbarFolders\` | Contents |
|---|---|
| `groups\<id>.json` | Group configuration. The **file name is authoritative** for the id; a mismatched `id` field inside the JSON is ignored on load. |
| `icons\<id>.ico` | Generated composite icon. |
| `icons\cache\<sha256>.png` | Extracted source icons, keyed by source path, last-write time and size. Pruned after 30 days. |
| `shortcuts\<id>.lnk` | The pinnable shortcut. |
| `settings.json` | Global settings. |
| `logs\manager-<date>.log`, `logs\launcher-<date>.log` | One file per day per process kind, retained 14 days. |

Outside that root, one Start menu anchor per group lives in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\TaskbarFolders\`.

Group ids are validated against `^[A-Za-z0-9._-]{1,96}$` before any path is built. Everything path-derived funnels through the path provider so the validation cannot be bypassed — it blocks traversal and keeps the AUMID inside its 128-character limit.

Both the log prune and the icon-cache prune are deferred to a background task after the first frame, so neither delays startup.

## Locating the launcher

The Manager has to find the launcher binary, and the two are not deployed in one folder. Three layouts are probed in order:

1. **Side by side** — `{baseDir}\TaskbarFolders.Launcher.exe`.
2. **Sibling folder** — `{baseDir}\..\Launcher\TaskbarFolders.Launcher.exe`. This is the shipped layout: both the installer and the portable ZIP place `Manager\` and `Launcher\` next to each other under the install root.
3. **Development tree** — climb to `TaskbarFolders.sln`, then probe each target-framework folder under `src\TaskbarFolders.Launcher\bin\<Configuration>\`.

Probe 3 enumerates the framework folders instead of deriving one, because the launcher's target framework differs from the Manager's. Probe 2 exists because v0.2.0 shipped without it and **Show shortcut…** was silently broken on every real install; a regression test pins that layout explicitly.

When no probe matches, the resolver logs every path it tried at error level, and sync aborts before generating anything.

## Design decisions

| Decision | Rationale |
|---|---|
| WPF over WinUI 3 | Mature Win32 interop, reliable transparent windows, no MSIX requirement — [ADR-001](adr/001-wpf-over-winui.md) |
| One shared launcher with per-group AUMIDs | Avoids shipping unsigned, dynamically modified executables — [ADR-002](adr/002-per-group-lnk-aumid.md) |
| Device pixels at the Win32 boundary, one conversion | Prevents double conversion and placement drift at non-100 % scaling — [ADR-003](adr/003-dpi-unit-contract.md) |
| JSON files over a database | Human-readable, inspectable, no external dependency, trivially backed up |
| A separate short-lived launcher process | The taskbar starts a process per tile; a long-running host would have to broker clicks and would keep memory resident for a popup shown for seconds |
| Atomic write-then-move everywhere | A crash or power loss during a write cannot leave a truncated config, icon or shortcut behind |

## Icon pipeline

```mermaid
graph LR
    A[".exe / .lnk / .ico"] -->|SHGetFileInfo, IImageList| B[Source icon]
    B -->|cache by path + write time + size| C[icons/cache/&lt;hash&gt;.png]
    C -->|arrange 1, 2, 3 or 4 up| D[Composite 256 px]
    D -->|encode PNG-in-ICO| E["icons/&lt;id&gt;.ico<br/>16 / 32 / 48 / 256"]
    E -->|SetIconLocation| F["shortcuts/&lt;id&gt;.lnk"]
```

The icon is referenced by the shortcut. It is never written into an executable.

Every `HICON` obtained from `SHGetFileInfo` or `IImageList.GetIcon` is released with `DestroyIcon` in a `finally`, and every COM runtime-callable wrapper through `Marshal.FinalReleaseComObject`. Wrappers are constructed *before* the `try` so a failure to create one cannot leave the `finally` handling a half-constructed object.
