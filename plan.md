# Plan

The agreed roadmap for this fork: what is done, what is in progress, and the order of what
comes next. This file is kept current — an item is marked done in the same commit that
finishes it, and a change of order or scope is recorded here when it is agreed, not later.

*Last updated: 2026-10-28*

## Where we are

- **`develop`:** everything up to and including the code review before C1 (`e215c55`). B3 and
  the cleanup are merged locally and **not pushed**. `origin/develop` is at `5a81f60`, the last
  pushed state (up to the app list step). Pushing waits until the end, as agreed.
- **Branches:** every feature branch is merged into `develop`. Deleting them is part of D.
- **Next:** C1 — grid settings, part 1, on `feature/grid-settings-1` from `develop`.

## How an item is worked

One item at a time, each on its own branch off `develop`:

1. Tests first, and confirm they fail without the change.
2. The change itself.
3. Documentation per the table in `CLAUDE.md` — `docs/user-guide.md` **and**
   `docs/benutzerhandbuch.md`, `CHANGELOG.md`, and this file.
4. `dotnet format`, then commit.
5. Rebuild `publish/` and stop for a manual test before starting the next item.

Nothing is pushed without asking first.

## In progress

### C6 — Manager redesign

Agreed as a series of small, separately tested steps rather than one rewrite. The skeleton
comes first so that C1–C4 land in their final place instead of being laid out twice.

- [x] **Group list (sidebar)** — taskbar icon and app count per group, accent bar for the
  selection, **+ New group**, per-row context menu, F2/Del. `d7d1abd`, merged into `develop`.
- [x] **Group header** — name edited in place, **Pin to taskbar** as the one accent button,
  *⋯* menu for *Show shortcut…*, *Copy group id* and *Delete group…*, icon menu for
  *Change icon…* / *Use app icons*. `b62aa23`, merged into `develop`.
- [x] **Themed context menus** — no light icon column in dark mode. `88fceff`, merged into
  `develop`.
- [x] **App list** — *Apps* and *Appearance* tabs; a slim **Add apps…** row in place of the
  large drop box (the whole tab takes dropped files); a delete icon on the hovered or selected
  row instead of a *Remove* button on every row, `Del` to remove. *Appearance* holds only the
  group icon choice until C1 + C2 fill it. `90c3ee4`, merged into `develop`.
- [ ] **Settings and empty state** — settings apply as they are changed (no *Save*); with no
  groups, a three-step "get started" view. Ranked #2 below.
- [ ] **Visual polish** — let the Manager's Mica backdrop show through (it is applied but
  hidden under solid brushes), Fluent icons, consistent spacing; retake the screenshots.
  Ranked #6 below.

## Up next, in order

Ranked by importance on 2026-10-28. The C6 steps above that are still open are placed in this
list where they fall. The detailed specification of each item is in *Item specifications*
below.

| # | Item | What it changes and why |
|---|---|---|
| 1 | **C1 — grid settings, part 1** | On *Appearance*: columns, icon size, show/hide names, applied to the popup. Columns are JSON-only today (upstream #29). |
| 2 | **C6 — settings and empty state** | Settings apply as they change (no *Save*); with no groups, a three-step "get started" view. |
| 3 | **D — release v0.5.0** | Version bump, installer and portable build, installer smoke test, CHANGELOG section, release notes; open an issue for `IsPinningAllowed = false`; delete merged branches. Puts the work so far in users' hands before the larger items. |
| 4 | **C2 — grid settings, part 2** | Live preview of the popup next to the settings, and tile spacing. The preview reuses the popup's own tile style rather than a copy. |
| 5 | **C4 — reorder apps by dragging** | Drag rows in the app list, and `Alt+↑` / `Alt+↓` from the keyboard. Order matters: the group icon is built from the first four apps, and today the only way to change it is to remove and re-add apps. |
| 6 | **C6 — visual polish** | Let the Manager's Mica backdrop show, Fluent icons, spacing; retake screenshots. |
| 7 | **E — add apps from a list of installed apps (research first)** | Instead of browsing for an `.exe`/`.lnk`, pick from what Windows lists under *Start → All apps*, which also covers Store/packaged apps (Calculator, Settings…) that cannot be added at all today. The research settles the source (the shell's AppsFolder vs. Start menu shortcuts), how a packaged app is stored and launched (`shell:AppsFolder\<AUMID>` rather than a path), and how its icon is read. |

## Item specifications

Each specification is written before any code, so that the item can start from this section
alone. The decisions under **Decided** were agreed on 2026-10-28. Every item follows *How an
item is worked* above.

### 1. C1 — grid settings, part 1 (`feature/grid-settings-1`)

**Goal:** the popup's grid is configurable from *Appearance*: columns, icon size, and names
shown or hidden. Closes upstream #29 for columns.

**Decided:** icon size is one of four presets, 32 / 48 (default) / 64 / 80 DIP. Hiding the names
makes the tiles narrower (compact, icon first).

**Changes**
- `GroupConfig` (`src/TaskbarFolders.Shared/Models/GroupConfig.cs`): `IconSize` (int, default 48;
  the setter snaps to the nearest of `IconSizes = { 32, 48, 64, 80 }`) and `ShowNames` (bool,
  default true). JSON `iconSize` / `showNames`. Old JSON without them gets the defaults. Both are
  non-nullable, because `WhenWritingNull` would drop a null value.
- `TileMetrics` (new, `src/TaskbarFolders.Launcher/Views/TileMetrics.cs`), a pure helper like
  `TileNavigator`: `Compute(iconSize, showNames, spacing = 0)` returns `Width`, `Height`,
  `LabelMaxWidth` and `ExtractPx`.
  - Names shown: `Width = max(96, icon + 32)`, `Height = icon + 48`. At 48 this is 96 × 96,
    the current look.
  - Names hidden: `Width = Height = icon + 24`.
  - `ExtractPx = icon * 2`, so the icon stays sharp at 150 % DPI.
  - `spacing` is in the signature already, so C2 does not rewrite the helper.
- `PopupViewModel` (`src/TaskbarFolders.Launcher/ViewModels/PopupViewModel.cs`): `IconSize`
  changes from a `const` (line 31) to an instance value. `IconSize`, `ShowNames`, `TileWidth`,
  `TileHeight` and `LabelMaxWidth` become observable. Both `LoadAsync` (about line 140) and
  `ReloadAsync` (about line 215) copy them from the config. `LoadIconForAsync` requests `ExtractPx`.
- `PopupWindow.xaml` and `.xaml.cs`: the hard-coded 96 × 96 tile, the 48 icon box, the
  `MaxWidth=80` label and `TilePx` (`.xaml.cs` line 37) come from `TileMetrics` through bindings.
  The name `TextBlock` collapses when `ShowNames` is false; the tooltip already shows the name.
  `Window.MaxWidth` goes from 640 to 720, so six columns of 112 px fit with the scrollbar.
  `ApplySizeAndPlacement` keeps its measuring logic and runs again when the column count or the
  size changes.
- Manager `GroupEditorViewModel` (`src/TaskbarFolders.Manager/ViewModels/GroupEditorViewModel.cs`):
  `Columns`, `IconSize`, `ShowNames`. They save the way `SaveOnEdit` does: save only, no sync,
  with `_pendingSaves` set so `IsSaving` stays true. A `_binding` flag stops `Bind` from saving.
  The column slider is debounced by 300 ms; the radio buttons and the checkbox save at once.
- `MainWindow.xaml` *Appearance* tab (about lines 341–365): a "Grid" section below "Group icon".
  A column `Slider` from 1 to 6 with snapping and a value label; icon size as four segmented
  `RadioButton`s; a "Show names" `CheckBox`.

**Tests first** (each must fail before the change)
- `PreferencesSerializationTests`: old JSON gives the defaults; `IconSize` snaps; the new fields
  round-trip.
- `TileMetricsTests` (new): the three formulas, and the invariant that 48 gives 96 × 96.
- `PopupViewModelTests`: the fields load, and update on reload.
- `PopupWindowSizingTests`: the constants now come from `TileMetrics`; new cases at 80 DIP, with
  names hidden, and at six columns; width stays at or below `MaxWidth`.
- `TestPopup.Show`: optional `iconSize` and `showNames` parameters.
- `GroupEditorViewModelTests`: edits save, sync is not called, `Bind` does not save.
- `ControlStyleTests`: the segmented button style.

**Docs**
- `docs/user-guide.md` (about line 162) and `docs/benutzerhandbuch.md` (about line 214): the
  "edit the JSON" sentence goes; the new controls are described.
- `docs/api-reference.md` (about line 34): the new `GroupConfig` fields.
- `docs/troubleshooting.md` (about line 132): the columns known limitation is removed.
- `README.md` (about line 138): "There is no UI for it yet" is removed.
- `CHANGELOG.md` under `[Unreleased]`: Added (columns, icon size, names).
- Screenshots: the popup and the Appearance tab, in both themes, with the recipe from `CLAUDE.md`.

**Verify:** all tests; `dotnet format --verify-no-changes`; a PrintWindow capture of the popup in
the four sizes, with and without names; `publish/` rebuilt, then a manual test by the user.

**Risks**
- At 150 % DPI a 64 px source is slightly upscaled; `ExtractPx = icon * 2` avoids it. The default
  moves the extraction from 64 to 96 px, so each icon misses the cache once. Harmless.
- The slider saves often; the debounce keeps that down.
- `PopupWindowSizingTests` constants are the most fragile part of the change.

### 2. C6 — settings apply instantly, empty state (`feature/settings-instant`)

**Goal:** no Save button. Each change takes effect at once. With no groups, the main window
guides the user.

**Decided:** the Explorer menu is refreshed on each change that affects it.

**Changes**
- `SettingsViewModel` (`src/TaskbarFolders.Manager/ViewModels/SettingsViewModel.cs`): remove
  `SaveCommand`, `HasUnsavedChanges` and the dirty tracking. Each property change calls
  `ApplyAsync(property)`. Applies run through a `SemaphoreSlim` so they never interleave: the
  store writes the whole document, so two overlapping writes would lose a setting. Order per
  apply: write the store, run the side effect, then raise `Saved`. `ExplorerContextMenuService`
  reads the setting from the store, so the order matters. `LoadAsync` applies nothing.
- Side effects:
  - Theme: `IThemeService.SetPreference`, immediate.
  - AutoStart: `Enable` / `Disable`. On failure, the property is reverted without applying again,
    and `IUserConfirmation.Notify` tells the user.
  - ShowInExplorerMenu: `Saved` rewrites the HKCU `shell\TaskbarFolders.Groups` keys.
- `SettingsWindow.xaml` (lines 34–42): the footer keeps only **Close** (`IsDefault` and
  `IsCancel`). The "Unsaved changes" text goes. The title-bar refresh in `.xaml.cs` is unchanged.
- Empty state:
  - `MainWindowViewModel.HasNoGroups`, raised from `Groups.CollectionChanged` and after
    `LoadGroupsAsync`.
  - `MainWindow.xaml`: a "Get started" panel in column 1, outside the
    `DataContext="{Binding Editor}"` grid. Three steps: 1 *Create a group* (the `+ New group`
    action), 2 *Add apps* (drop them or use *Add apps…*), 3 *Pin it to the taskbar*, once the
    group has apps. The existing "Select a group…" line shows only when groups exist.

**Tests first**
- `SettingsViewModelTests`: rewrite the Save-based tests (about lines 132, 142, 153, 172, 191
  and 204). New: each change applies once; `Saved` comes after the store write; a failed AutoStart
  reverts; two quick changes lose neither; `LoadAsync` applies nothing.
- `MainWindowViewModelTests`: `HasNoGroups` on an empty start, after the first group, and after
  the last group is deleted.
- `CompositionRootTests` is unchanged.

**Docs:** both user guides (no Save; the empty state), CHANGELOG under Changed, and screenshots of
the empty state and the settings window.

**Verify:** by hand. Change the theme; toggle AutoStart on and off and check `HKCU\…\Run`; toggle
the Explorer menu and check the registry keys; read `manager-*.log`.

**Risks:** the Explorer rewrite runs on each toggle. It is cheap, and the queue keeps the order.
`SettingsViewModel` is a singleton, so a reopened window shows the same live state.

### 3. D — release v0.5.0 (on `develop`, no branch)

**Goal:** put C1 and C6 in users' hands before the larger items. This is the first release since
0.4.10.

**Preconditions:** C1 and C6 are merged into `develop`, and `develop` is green locally (build,
tests, format).

**Changes**
1. Version to `0.5.0`: `Directory.Build.props` (line 16), `installer/setup.iss` (line 4),
   `src/TaskbarFolders.Manager/app.manifest` (line 3, `0.5.0.0`), and the `README.md` status banner
   (line 9).
2. `CHANGELOG.md`: rename `[Unreleased]` to `## [0.5.0] - <date>`. The heading must match exactly,
   because `release.yml` looks for `## [0.5.0]`. Add a new empty `[Unreleased]` above it. In the
   footer, `[Unreleased]` points at `compare/v0.5.0...HEAD`, and `[0.5.0]` at
   `compare/v0.4.10...v0.5.0`.
3. Size baseline after tagging: `docs/release-process.md` lines 73–75 and `CLAUDE.md` line 235.

**Gates** (`docs/release-process.md` lines 20–41)
- Build, test (`DOTNET_ROLL_FORWARD=LatestMajor` on this machine), `dotnet format --verify-no-changes`.
- Run the Manager. Run `Launcher.exe --pin-mode --group-id <id>`, then read `launcher-*.log` and
  `manager-*.log`.
- The docs match what ships.

**Ask before:** pushing `develop`, pushing the `v0.5.0` tag, fast-forwarding `main`, and deleting
any branch.

**Branch cleanup**
- Local, merged into `develop` (14): `chore/cleanup`, `feature/custom-group-icon`,
  `feature/drop-group-theme`, `feature/manager-app-list`, `feature/manager-header`,
  `feature/manager-sidebar`, `feature/per-app-settings`, `feature/placeholder-icon`,
  `feature/popup-edit-apps`, `feature/popup-header-close`, `feature/popup-keyboard`,
  `feature/rename-group-orphan-anchor`, `feature/send-to-group`, `fix/icon-extraction-race`.
- Remote `origin`, merged: `feature/manager-app-list`, `feature/rename-group-orphan-anchor`,
  `feature/send-to-group`.
- Not touched: `perf/launcher-startup` (not merged into `develop`), and the dependabot branch.
- Open an issue for `IsPinningAllowed = false`.

**After tagging:** the install smoke test in `docs/release-process.md` (lines 80–92), with the
user. No announcement before it passes.

### 4. C2 — grid settings, part 2 (`feature/grid-settings-2`)

**Goal:** a live preview of the popup beside the grid controls, and tile spacing.

**Decided:** the preview uses the popup's own tile style, not a copy.

**Spike first (about 30 minutes, read-only):** can the tile style live in a resource that both
projects load? `Core` must not depend on WPF, so a shared resource goes into a file linked into
the Launcher and the Manager (`<Page Include="…" Link="…">`). If that does not work, fall back to a
Manager preview drawn with `TileMetrics`, plus one test that compares the colours and sizes of the
two.

**Changes**
- `TileSpacing`: an int with the steps 0, 4, 8 and 12; default 0, the current look.
- `TileMetrics.Compute` takes the spacing. The `UniformGrid` has no gap, so each tile gets
  `Margin = spacing / 2` and each cell grows by `spacing`. The window's `MaxWidth` is recalculated:
  six columns of 112 + 12 px is about 768 px, so `MaxWidth` goes to 800.
- Preview in *Appearance*: a `Viewbox` beside the controls, showing up to 12 tiles of the group's
  real apps. It reads the view model, not the store, so it updates at once when `Columns`,
  `IconSize`, `ShowNames` or `TileSpacing` change, without waiting for the save.

**Tests first**
- `TileMetricsTests`: spacing cases.
- The popup sizing theory with spacing.
- The preview view model: tile count and column count.
- Old JSON loads with spacing 0.

**Docs:** both user guides, api-reference, CHANGELOG, this file, and screenshots.

### 5. C4 — reorder apps by dragging (`feature/reorder-apps`)

**Goal:** change the app order by dragging rows, and with `Alt+↑` / `Alt+↓`. The order matters,
because the group icon uses the first four apps.

**Decided:** both drag and keyboard.

**Changes**
- `GroupEditorViewModel.MoveAppAsync(int from, int to)`, with a `[RelayCommand]` wrapper. Take
  `var item = _boundItem` into a local first, as the other commands do. Move the entry in both
  `item.Config.Apps` and `Apps` (`ObservableCollection.Move`). An out-of-range index, equal indices,
  or no group is a no-op. Save with the `SaveOnEdit` pattern (no sync), **except** when
  `min(from, to) < CompositeIconGenerator.MaxTiles` (4): then use `SaveAndSyncAsync`, because the
  group icon changes.
- `ReorderMath` (new, `src/TaskbarFolders.Manager/Views/ReorderMath.cs`), pure: the pointer's Y and
  the row heights give the insert index, corrected for the row being removed.
- `MainWindow.xaml.cs`:
  - A drag starts only after `SystemParameters.MinimumVerticalDragDistance`, and never from a
    `TextBox` or `Button`.
  - A private data format, `"TaskbarFolders.AppRow"`, tells row drags from file drops.
  - When a drag starts, an open details row collapses.
  - After a drop, the click that follows is suppressed by a flag in `AppRow_MouseLeftButtonUp`.
  - `Editor_PreviewDragOver` returns `Move` for the row format and `Copy` for `FileDrop`.
  - `AppsTab_DragEnter` does not show "Drop to add" for row drags.
  - The insert position is a thin `AccentBrush` line between rows.
- `AppList_KeyDown`: `Alt+Up` and `Alt+Down` move the selected row. Keys from a `TextBox` are
  ignored, as the existing rule says.

**Tests first**
- `GroupEditorViewModelTests`: both collections end in the same order; the move saves; sync runs
  only when the first four change; an out-of-range move is a no-op; a rebind during the move keeps
  `_boundItem`.
- `ReorderMathTests`.
- A small STA test that `Alt+Down` moves a row.
- The drag gesture itself is checked by hand.

**Docs:** both user guides (drag, `Alt+↑` / `Alt+↓`), CHANGELOG, this file.

**Risks:** click versus drag; dropping onto an expanded row (the rule is the upper or lower half
of the row).

### 6. C6 — visual polish (`feature/manager-polish`)

**Goal:** the Mica backdrop shows through; icons and spacing are consistent; screenshots are
retaken.

**Changes**
- The backdrop is applied (`src/TaskbarFolders.Core/Interop/WindowBackdrop.cs`), but solid brushes
  in `Dark.xaml` and `Light.xaml` hide it. Window and sidebar backgrounds become translucent;
  content surfaces stay opaque. Contrast is measured in both themes.
- Segoe Fluent icons, used consistently.
- A spacing scale (4 / 8 / 12 / 16) as resources in `Controls.xaml`.
- All screenshots retaken, with the recipe in `CLAUDE.md`.

**Start with** a 20-minute read-only pass that lists which brushes are opaque.

**Tests:** theme dictionary tests, and a pixel test for the Mica area.

**Docs:** the user guides where the look is described, the screenshots, and CHANGELOG.

### 7. E — add apps from the installed apps (`feature/installed-apps`)

**Goal:** add apps from Windows' *All apps* list, including Store and packaged apps (Calculator,
Settings), which cannot be added today.

**Why:** browsing for an `.exe` is slow, and packaged apps have no `.exe` or `.lnk` path.
`AppEntry.TryCreateFromPath` accepts only those two kinds.

**Decided:** a written plan first, then the research, then the code. No prototype commit.

**Stage 0 — research note** (no commit; any temporary files are deleted afterwards). It answers:
1. Enumeration: bind `shell:AppsFolder` with `SHCreateItemFromParsingName`, then `BHID_EnumItems`
   into `IEnumShellItems`. How many items, and how long does it take? The target is under 1 s.
2. Identity: `PKEY_AppUserModel_ID` for each item. Are the Win32 items paths or `{GUID}\…`? Do the
   packaged ones look like `PackageFamily!App`?
3. Classification: does the item have a `.exe` or `.lnk` target (`PKEY_Link_TargetParsingPath`)?
   If so, Win32: store the normal path, and everything works as today. If not, packaged: store
   `shell:AppsFolder\<AUMID>`.
4. Launch: `Process.Start` with `UseShellExecute` on `shell:AppsFolder\…`, from the Launcher. The
   expectation is yes. The `WorkingDirectory` must be skipped for these (`ProcessLauncher.cs`,
   lines 29–59).
5. Icon: `IShellItemImageFactory.GetImage` (an HBITMAP, `SIIGBF_ICONONLY`). Are there alpha or plate
   problems? `SHGFI_USEFILEATTRIBUTES` cannot resolve these items.
6. Icon cache: `FileSystemIconCache.cs` line 145 uses 0 ticks when the file is missing. Packaged
   apps need the package version in the key, or no cache at all.

Output: the research note, an ADR draft (ADR-009), and this section updated with the findings. **The
decision goes to the user.**

**Stage 1 — model:** `AppEntry.TryCreateFromAumid(name, aumid)`, which sets
`Path = "shell:AppsFolder\<AUMID>"`. The JSON stays compatible, since it is a string.
`AppEntryViewModel.DefaultName` and `PathDisplay.ForDisplay` get a branch for `shell:` paths. The
duplicate check in `CommandLineAppAdder` already works on `Path`.

**Stage 2 — core:** COM interfaces `IEnumShellItems`, `IShellItem2` and `IShellItemImageFactory` in
`Core/Interop`. Declare every method in order, because `IContextMenu.cs` warns that COM vtables are
positional. `IInstalledAppsProvider` in Core, with asynchronous enumeration. `FinalReleaseComObject`
on every RCW; shell calls go through the existing shell lock. `ShellIconExtractor` gets an
`IShellItemImageFactory` branch for `shell:` paths.

**Stage 3 — UI:** a dialog with search, multi-select, lazily loaded icons and a loading state.
`GroupEditorViewModel.AddInstalledAppsAsync` follows the `AddAppsAsync` pattern and saves through
`SaveAndSyncAsync`.

**Tests**
- `AppEntryTests`, `ProcessLauncherTests`, `ShellIconExtractorTests` (using the real Calculator AUMID,
  skipped if it is absent).
- `GroupEditorViewModelTests` with a fake provider.
- The picker view model: filtering and multi-select.

**Uninstall:** nothing new is written outside the program folder, so `Uninstaller` does not change.
This is still verified.

**Risks:** AUMIDs stay stable across package updates, while paths do not, which is good.
Enumeration speed. Icon transparency. Win32 apps may appear twice (Start menu and All apps).

**Docs:** both user guides, architecture (the storage format), api-reference, troubleshooting (the
limits), CHANGELOG, the ADR, and this file.

## Done

| Item | Commit | Branch |
|---|---|---|
| C3 — per-app name, arguments and icon (upstream #29, #30); settings close on a click outside | `862bd57`, `b6470f5` | merged |
| B5 dropped — `GroupConfig.Theme` removed (upstream #30) | `ba95eb7` | merged |
| B3 — keyboard navigation in the popup (upstream #26) | `29f37e6` | merged |
| Code review before C1: one icon lookup (`AppIcons`), dead code and stale docs removed, shared test helpers; found and fixed the launcher's exit code 4 on closing an active popup | `cb2a895`, merged as `e215c55` | merged |
| Pushed `develop` and `feature/manager-app-list` to `origin`, up to the app list step | `1da190a`, `9f18df6`, `5a81f60` on `origin` | — |
| CI sizing test expects the window height Windows allows | `10e8698` | `develop` |
| Adding an app in the Manager no longer closes it | `3fa414a` | merged |
| The popup no longer accepts dropped files (the right-click menu covers it) | `c49e153` | merged |
| A1 — a second tile click closes the popup | `01d16e2` | merged |
| Complete uninstall, per-user setup (ADR-008) | `71fd68f` | merged |
| B1 — pin the popup open | `237ed01` | merged |
| B2 — scroll a group taller than the popup (upstream #32) | `9ca0245` | merged |
| B4 — placeholder for an app without an icon (upstream #27) | `5f77f59` | merged |
| C5 — an image as a group's icon | `419241f` | merged |
| Screenshots retaken | `066d96a` | merged |
| Shell icon extraction serialised (random blank popup tiles) | `66a99af` | merged |
| C6 — group list | `d7d1abd` | merged |
| C6 — group header, themed menus | `b62aa23`, `88fceff` | merged |
| C6 — app list: tabs, slim add row, hover delete | `90c3ee4` | merged |
