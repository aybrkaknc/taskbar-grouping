# Plan

The agreed roadmap for this fork: what is done, what is in progress, and the order of what
comes next. This file is kept current — an item is marked done in the same commit that
finishes it, and a change of order or scope is recorded here when it is agreed, not later.

*Last updated: 2026-10-30*

## Where we are

- **`develop`:** v0.5.1 prepared — version bumped in the five places, CHANGELOG section dated
  2026-10-29, collecting the fixes from the v0.5.0 smoke test (below). Pushing `develop`, pushing
  the `v0.5.1` tag and fast-forwarding `main` each wait for approval; after the tag, the install
  smoke test of the published assets comes before any announcement.
- **`main`:** still at `18c929e`; to be fast-forwarded to the v0.5.1 commit once the tag is out.
- **Branch:** none open locally. `origin/perf/launcher-startup` and the dependabot branch are not
  merged and stay.
- **Still open:** the `IsPinningAllowed` issue (issues are off on the fork; turning them on is a
  repository setting, and the user said not now). `assets/screenshots/settings.png` still shows
  the Save button; retaking it needs 150 % scaling (C6 polish retakes all of them). Then C2.

## Found in the v0.5.0 smoke test (2026-10-28)

Noted during the installed-build smoke test. Each becomes its own commit, with a test first,
decided with the user one at a time.

- [x] **Deleting a group leaves its pinned tile on the taskbar.** Fixed on `develop` in
  `fix(manager): unpin a deleted group's taskbar tile`. Confirmed in the smoke test (group
  `190bb1b3…` deleted, its pinned copy left, the tile opening "(Unknown…) / No apps to launch").
  As decided with the user on 2026-10-29: one confirmation that says the tile goes too; unpin
  (by AUMID, through `IPinnedTileRemover` → `ITaskbarUnpinner`) before the files are removed; the
  delete does not depend on the unpin; two language-neutral methods — the `taskbarunpin` verb by
  name, then the menu item whose canonical verb it is (`GCS_VERBW`), never the caption, never
  deleting the file; after a successful unpin, ask every time whether to restart Explorer (No →
  "restart it by hand"); a refused tile → "unpin by hand"; tiles left by earlier deletions listed
  once, remembered in `settings.json` (`orphanedTilesNoticeShown`). All text in English, as the
  app. Measured first on this machine: the verb removed the test tile's pinned file without an
  Explorer restart. Not changed: the popup of a missing group still titles itself "(Unknown…)".
- [x] **Two Explorer menu refreshes race and log a warning.** Fixed on `develop` in
  `fix(manager): serialise Explorer menu refreshes`. Closing the delete confirmation re-activates
  the Manager, whose reload raises `GroupListChanged`, and `DeleteGroupAsync` raises it again; the
  handlers in `App.xaml.cs` ran two `RefreshAsync` calls at once, the second `DeleteSubKeyTree`
  found the tree half gone (`IOException`), and an older read could write last. As decided with
  the user on 2026-10-29: serialise the whole refresh — read and write — behind a `SemaphoreSlim`
  (the service is now `IDisposable`); the event is still raised twice, which is harmless once the
  refreshes queue. Tests: a second refresh waits for the first, the newest list wins, a failed
  refresh releases the gate.
- [x] **The Explorer menu's "Taskbar Groups" entry shows a generic icon, not ours.** Fixed in
  `9c9dbfe` (`fix(launcher): give the launcher its own icon`): the Launcher csproj now sets
  `ApplicationIcon` to `launcher.ico`; the built exe's icon matches that file. No test added, as
  decided — a one-line project setting, verified by measurement. Still to check after install:
  the menu shows the new icon. Seen in the
  v0.5.0 smoke test (screenshot). `ExplorerContextMenuService.Write` sets `Icon` to
  `<launcher>.exe,0` on each submenu root (`ExplorerContextMenuService.cs:133`). Cause found on
  2026-10-29: `TaskbarFolders.Launcher.csproj` has no `<ApplicationIcon>`, while the Manager's has
  `..\..\assets\icons\manager.ico`. So the Launcher exe carries .NET's default app icon, and
  `assets\icons\launcher.ico` (which exists) is used by nothing. Measured: the Launcher exe's icon
  differs from the Manager's, and from Windows' default icon, so it is some other icon — most
  likely the .NET default. Proposed: add `<ApplicationIcon>` to the Launcher csproj, pointing at
  `launcher.ico`, plus a test that each exe project declares an icon file that exists. Decision
  pending: `launcher.ico` (the Launcher's own) or `manager.ico` (the one the menu is part of).
- [ ] **After Enter the new name is saved, but nothing says so.** Seen in the smoke test. The
  name changes in the sidebar, but the header box keeps its focus border and the user cannot tell
  it was saved. Enter runs `CommitRenameCommand` (`MainWindow.xaml.cs:111-123`), and Escape moves
  focus to the list (`GroupList.Focus()`); Enter does not. Proposed: Enter commits and then moves
  focus to the list, as Escape already does, so the box visibly lets go. Decisions pending: see the
  question list in the chat on 2026-10-29.
- [ ] **The Pin to taskbar button does not pin on this machine.** Seen in the smoke test: exit
  code 2, `TaskbarManager.IsPinningAllowed=false`, Explorer fallback. **Cause found 2026-10-29:**
  Windows notifications were off (`HKCU\…\PushNotifications\ToastEnabled = 0`); Windows shows the
  pin request as a notification, and Microsoft's "Pin your app to the taskbar" lists notifications
  among the conditions for `IsPinningAllowed`. With them on, the button pinned the group (exit 0,
  verified by AUMID). No group policy, and this build needs no Limited Access Feature token.
  Decided with the user: be ready for every case without ever pinning silently.
  - [x] **A pinned group can be pinned again, blanking its tile's icon.** Pressing the button on a
    pinned group made Windows redraw the tile blank (fixed by an Explorer restart). Now the button
    reads *Pinned* and is off while a tile with the group's AUMID exists
    (`IPinnedTileRemover.IsPinned`), re-checked on selection, after a pin and on window activation.
  - [x] **Explain why pinning is unavailable and offer what works** — `PinHelper.Diagnose` reads
    `NoPinningToTaskbar`, the LAF seed and `ToastEnabled` (`PinBlockerRules.Classify`, policy and key
    before notifications). Notifications off → ask: Yes opens `ms-settings:notifications`, No pins by
    hand; key required / policy → say so, manual way; unknown → generic message, manual way. Explorer
    opens with the shortcut selected through `IProcessRunner`. Only the notifications case can be
    tried on this machine; key and policy are unit-tested only.
- [ ] **Pin to taskbar is refused right after a group is created, then allowed seconds later.**
  Seen on 2026-10-29 with the locally built per-user setup, notifications **on**
  (`ToastEnabled=1`): the first press, 2 s after the group's shortcut was written, got
  `TaskbarManager.IsPinningAllowed=false` (exit 2, blocker *Unknown*, the manual way offered); the
  second press 23 s later was allowed and Windows showed its request. Cause not proven; the
  working guess is that the shell has not yet taken up the new Start menu shortcut/AUMID.
  Decided with the user: `TaskbarPinRunner` re-asks `IsPinningAllowed` up to 6 times, 1 s apart
  (`PinPermissionWait`, unit-tested with a fake delay) before it gives up — never around Windows'
  consent. The cost is about 5 s before a machine that really cannot pin gets its explanation. The
  check used to sit before the 300 ms settle meant for this very race. Committed in `b76cc41`.
  Tried on the installed copy (2026-10-29): pinned at the first press, exit 0, verified by AUMID —
  but the press came 3.5 s after the shortcut was written and `IsPinningAllowed` was true at the
  first check (0.23 s from start to the next log line), so the retry itself was **not**
  exercised. Whether it fixes the 2 s case is still unproven.
  Tried again on the published v0.5.1 setup after a clean install (07:04): the press came 1.9 s
  after the shortcut was written, `IsPinningAllowed` was true at the first check (0.22 s to the
  next log line), Windows showed its request, and the user declined it (exit 1, `UserDenied`).
  The v0.5.0 refusal did **not** recur at 2 s, so the retry still has not run. The same first
  press on v0.5.0 (07:01, same machine) was refused. Kept open until it shows on its own: look
  for "after 6 checks" in `launcher-*.log`.
- [x] **Some apps' icons come out tiny, in the preview, the group icon and the popup.** Fixed on
  `develop` in `fix(core): cut a small icon out of the shell's large canvas`. **Measured cause:** for a
  program with no large icon, the shell's 256 px image list returns a 256 × 256 canvas with the
  small icon (48 px, `45 × 48` for `CardLock.exe`, `javaw.exe`) drawn at (0,0); `IconCanvas.TrimToIcon`
  cuts it back to the icon's native size, and the icon cache key got a version (`v2`) so earlier
  entries are not served. Verified on this machine: the two shortcuts now extract at 48 × 48, the
  two good ones at 256 × 256 as before. **Second finding, fixed in `fix(core): show a shortcut's own icon`:**
  `UniFi.lnk`'s own icon location (`unifi-network.ico`, a full 256 px icon) was ignored, because the
  extractor resolved a shortcut to its target (`javaw.exe`) and showed Java's icon, where Explorer
  shows UniFi's. Decided with the user: use the shortcut's icon (an `.ico`, or an index into an
  exe/DLL) and fall back to the target's. Cache key now `v3`. Measured: `UniFi.lnk` extracts at
  256 × 256 with UniFi's own icon. Seen on
  2026-10-29 in the development build while trying the C2 preview, and confirmed by the user to look
  the same in the Apps tab and the popup, so it is not the preview. The group *Test 1*: `AnyDesk.exe`
  and `LDPlayer 14.lnk` (no icon location) are full size; `UniFi.lnk` (icon location
  `…\unifi-network.ico`, target `javaw.exe`) and `CardLock.exe - Kısayol.lnk` (target
  `CardLock.exe`, no icon location) draw as a small glyph in the middle of the tile. Not measured
  yet: what the extractor returns for them (size of the bitmap, how much of it is drawn), and
  whether they have only small frames. Suspected: `ShellIconExtractor` takes the big shell image
  and a source with only small frames comes back as a small icon on a large transparent canvas.
  Next: extract both at 96 and 256 px in a throwaway test and measure the non-transparent box.
- [ ] **A popup sizing test failed once and passed on rerun.**
  `PopupWindowSizingTests.AddingATileToTheSameRow_ShowsItsLabelInsideTheWindow` failed in one full
  run on 2026-10-29 with no Launcher change, then passed three times alone and in a full rerun.
  Watch for it; if it fails again, find what the layout pass races with. Checked 2026-10-29: six
  more runs of the Launcher suite on this machine were all green, and CI on `develop` passed for
  `546df2f`, `21cefb2` and `c64f590`. (An earlier note here said CI had not run since September:
  wrong — `gh run list --workflow CI` returned stale rows; the runs API shows them.)
- [x] **Explorer menu warning recurs on group creation/deletion** — the same race as above, fixed
  with it.
- [x] **Deleting a group logs a warning when `icons\custom\` does not exist.** Fixed on `develop`
  (`CustomGroupIconStore.Delete` now treats a missing folder like a missing file; tests: no warning
  logged, an existing image is still removed). Seen on
  2026-10-29 with fresh data after the uninstall: `CustomGroupIconStore.Delete`
  (`CustomGroupIconStore.cs:155`) calls `File.Delete` on `icons\custom\<id>.png`; a missing file is
  fine, but a missing folder throws `DirectoryNotFoundException`, logged as a warning with no
  counterpart in the UI. Nothing is wrong on disk. Fix: treat a missing folder like a missing file.
- [x] **Uninstalling leaves two Compatibility Assistant values behind.** Fixed on `develop` in
  `40531e1` (`fix(manager): remove the records Windows writes as the uninstall exits`); verified in
  real installed and portable runs (below). Found in the uninstall
  test on 2026-10-29: everything else was gone (program folder, *Installed apps* entry, Run
  value, both menu keys, `%APPDATA%\TaskbarFolders`, Start menu folder, `%TEMP%\.net`), but
  `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store`
  still held `…\Programs\TaskbarFolders\Manager\TaskbarFolders.Manager.exe` and
  `…\Programs\TaskbarFolders\unins000.exe`. `Uninstaller.RemoveWindowsRecords` covers that key,
  so the likely cause — not yet verified — is that Windows writes these values when the
  processes exit, after the cleanup ran. The user's goal: **an uninstall leaves nothing at all.**
  First measure when the values appear (log the key right before and after the cleanup), then
  decide the fix with the user. The two values are still in the registry; deleting them was not
  approved. No group tile was pinned during this test, so the uninstaller's unpin was not
  exercised either.
  - **Read-only measurement (2026-10-29):** the values are `SACP` records of the Program
    Compatibility Assistant (60 bytes for installed paths, 108 for build/publish paths). The Store
    also holds records for short-lived `bin\Debug|Release` Launchers, `publish\` exes and the
    downloaded `TaskbarFolders-Setup.exe`, but none for the long-running development Manager. So
    Windows seems to record short-lived processes **as they exit**: `--uninstall` and `unins000.exe`
    are short-lived, and their records land after the cleanup. A process cannot delete its own exit
    record. Still a hypothesis: the moment of writing has not been timed.
  - **Decided with the user (2026-10-29):** a detached helper that waits for both processes to exit,
    then deletes the values; `cmd` + `reg delete` (not PowerShell, which trips antivirus more);
    exact value names (`<root>\Manager\TaskbarFolders.Manager.exe`,
    `<root>\Launcher\TaskbarFolders.Launcher.exe`, `<root>\unins000.exe`) in all six
    `WindowsRecordKeys` keys; a ~2 minute timeout and silent failure; the portable package gets the
    same deletion in `installer\Uninstall.cmd`, which already runs after the Manager exits.
    Order: tests (see them fail), ADR-008 amendment, CLAUDE.md paragraph, architecture,
    troubleshooting, CHANGELOG, this file. Verify with a locally built installer (Inno Setup 6 is
    installed here), a watcher script that timestamps when the values appear, and a real
    install/uninstall with the user. The two leftover values are not deleted without approval.
  - **Measured in a real install/uninstall (2026-10-29), locally built setup:** the cleanup removed
    every record; `unins000.exe` exited at 08:43:41.1, Windows wrote its Store value at 44.6
    (3.4 s later), and the helper deleted it at 45.5. The Launcher's value was rewritten 2–3 s after
    each exit; the `--uninstall` Manager wrote none this time. Afterwards nothing of that copy was
    left: folders, `%TEMP%\.net`, Start menu, menu keys, Run value, *Installed apps* entry, the
    pinned tile, the helper script itself. Hypothesis confirmed. A single pass 3 s after the exit
    beat the record by under a second, so, as decided with the user, the sweep now runs twice —
    3 s and 5 s after the exit (the user found 5 and 20 s too long for the portable script's open
    window). The second real run, per-user install, confirmed it: `unins000.exe` exited at
    08:52:47.8, Windows wrote its record at 50.8 (3.0 s) and the helper deleted it at 53.96; the
    two older values from the first per-user install went with it.
  - **The first setup run was started as administrator** on purpose, so Inno ran in administrative
    install mode and installed to `C:\Program Files\TaskbarFolders` despite
    `PrivilegesRequired=lowest`; the uninstall was still complete. Not a defect, but ADR-008 assumes
    a per-user install.
  - **The portable run found two defects (2026-10-29), both fixed here:** MuiCache keeps a path
    with `.FriendlyAppName` / `.ApplicationCompany` appended and the sweep deleted only the bare
    path; and `Uninstall.cmd`'s `reg delete` lines ran before Windows had written the record, while
    holding a console open. Decided with the user: **`Uninstall.cmd` is removed** (option A). A
    portable copy uninstalls through *Settings → Uninstall TaskbarFolders…* (the Manager with
    `--uninstall`), and the same helper that sweeps an installed copy also deletes the `Manager` and
    `Launcher` folders, the unpacked `%TEMP%\.net` runtime, the data folder and — non-recursively —
    the extracted folder. A portable copy is told apart by `InstallLocation.IsPortable` (both
    folders with their exes, no `unins000.exe`); a development build gets no helper. Tests:
    `CompatibilityRecordSweepTests`, `InstallLocationTests`, `UninstallerTests`,
    `SettingsViewModelTests`; 286 / 258 / 213 green, format clean.
  - **Registry cleanup done with the user's approval:** the four `portable-test` MuiCache values from
    this and the previous session, by exact name; nothing else was touched. A broken `$names` array
    in my first attempt deleted nothing.
  - **Still to verify:** the new portable path in a real run (Settings → Uninstall in a copy
    extracted from the rebuilt `publish\`), then the installed path once more with the final code.
    The commit waits for that.

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
- [x] **Settings and empty state** — settings apply as they are changed (no *Save*); with no
  groups, a three-step "get started" view. `a6cb480`, merged into `develop`.
- [ ] **Visual polish** — let the Manager's Mica backdrop show through (it is applied but
  hidden under solid brushes), Fluent icons, consistent spacing; retake the screenshots.
  Ranked #6 below. *In progress on `feature/manager-polish`: Mica shows app-wide, the popup's
  backdrop and tint are choices, the preview shows them, spacing is on one scale, icons are
  Fluent throughout; next the screenshots.*

## Up next, in order

Ranked by importance on 2026-10-28. The C6 steps above that are still open are placed in this
list where they fall. The detailed specification of each item is in *Item specifications*
below.

| # | Item | What it changes and why |
|---|---|---|
| 1 | **C1 — grid settings, part 1** — *done, merged* | On *Appearance*: columns, icon size, show/hide names, applied to the popup. Columns were JSON-only (upstream #29). |
| 2 | **C6 — settings and empty state** — *done, merged* | Settings apply as they change (no *Save*); with no groups, a three-step "get started" view. |
| 3 | **D — release v0.5.0** — *version bumped; gates and pushes pending* | Version bump, installer and portable build, installer smoke test, CHANGELOG section, release notes; open an issue for `IsPinningAllowed = false`; delete merged branches. Puts the work so far in users' hands before the larger items. |
| 4 | **C2 — grid settings, part 2** — *done on `feature/grid-settings-2`, not yet merged* | Live preview of the popup next to the settings, and tile spacing. The preview reuses the popup's own tile style rather than a copy. |
| 5 | **C4 — reorder apps by dragging, in the popup's edit mode** — *done, merged into `develop`* | Changed 2026-10-30: not in the Manager's app list but on the popup's tiles. With edit mode on, a tile can be dragged to a new place (the others shift out of the way) and the tiles wiggle, as icons do on a phone. Order matters: the group icon is built from the first four apps, and today the only way to change it is to remove and re-add apps. |
| 6 | **C6 — visual polish** | Let the Manager's Mica backdrop show, Fluent icons, spacing; retake screenshots. |
| 7 | **E — add apps from a list of installed apps (research first)** | Instead of browsing for an `.exe`/`.lnk`, pick from what Windows lists under *Start → All apps*, which also covers Store/packaged apps (Calculator, Settings…) that cannot be added at all today. The research settles the source (the shell's AppsFolder vs. Start menu shortcuts), how a packaged app is stored and launched (`shell:AppsFolder\<AUMID>` rather than a path), and how its icon is read. |

## Item specifications

Each specification is written before any code, so that the item can start from this section
alone. The decisions under **Decided** were agreed on 2026-10-28. Every item follows *How an
item is worked* above.

### 1. C1 — grid settings, part 1 — done

Built as specified, with these differences:

- **Columns are a limit per row, not a fixed width.** A group with fewer apps than its column
  count gets only as many columns as it has apps (`PopupViewModel.EffectiveColumns`), so the
  window is no wider than the apps need. The Manager's control is labelled *Max per row*.
- **Columns use the same segmented control as the icon size, not a slider.** The Manager has no
  themed `Slider`, and a choice of six saves on each click with no debounce needed.
- **`TileMetrics` has no `spacing` parameter yet.** C2 adds it; an optional parameter needs no
  rewrite of the callers.
- **No screenshot changed:** the popup's default look is the same 96 × 96 tile, and the
  *Appearance* tab is not in the screenshots. C6 polish retakes them all.

### 2. C6 — settings apply instantly, empty state — done

Built as specified. Differences and details worth knowing:

- **An auto-start entry that cannot be written is tried before the store is**, and the checkbox is
  put back to `IAutoStartService.IsEnabled`; the other settings are stored first, then applied.
- **`WhenIdleAsync`** was added so tests (and anything that must wait for a pending change) can
  wait for the queue; it takes the same gate the applies do.
- **The get-started view waits for the groups to be read** (`HasNoGroups` is false until the store
  has answered), so it does not flash up on every launch.
- **Close** is both the default and the cancel button of the dialog.

The specification as it was written:

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

**Decided with the user on 2026-10-29** (after the spike): one shared tile resource, spacing steps
0 / 4 / 8 / 12, and a preview of the group's real apps. The spike found that Core already uses WPF
(`UseWPF`, the icon composer), so the shared tile style can live in Core as a resource dictionary
merged by both apps — no linked file needed. In three steps, each its own commit and manual test:

- [x] **Step 1 — spacing in the popup.** `GroupConfig.TileSpacing` (snapped to `TileSpacings`, 0 by
  default and for older files); `TileMetrics` moved to Shared and given `Spacing`, `CellWidth`,
  `CellHeight`, `HalfSpacing`; the popup's tile root takes half a gap of margin and the window is
  sized from `CellWidth`; `MaxWidth` 720 → 800. Settable only in the JSON until step 3.
- [x] **Step 2 — shared tile resource.** `AppTileButton`, `AppTileFocusVisual` and the tile content
  (`PopupTileContent`) moved to `Core/Themes/PopupTile.xaml`; the popup merges it and binds the
  button's content to the shared template. `TileHoverBackground`, `TileFocusBorder` and
  `LabelShadow` were added to both Manager themes (Launcher values). Verified: six offscreen
  renders (two themes × names/no names/spacing) byte-identical before and after.
- [x] **Step 3 — Manager.** Spacing control and the live preview on *Appearance*; user guides,
  CHANGELOG. Layout decided with the user while trying it: settings in a left column (scrolls only
  when needed), the preview in a fixed place on the right; the window opens at 1180 × 720 (min
  900 × 520) and grows — never shrinks — when the preview column would show it below 0.75 scale
  (`AppearanceLayout`). Tried by the user in the development build. **Not done:** no screenshot of
  *Appearance* yet — none existed, the recipe needs 150 % scaling, and C6 polish retakes them all.

The original specification follows.

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

### 5. C4 — reorder apps by dragging, in the popup's edit mode (`feature/reorder-apps`)

**Goal:** in the popup, turning edit mode on (the pencil) lets the user drag a tile to another place
in the grid, and the tiles wiggle while it is on. The order matters, because the group icon uses the
first four apps.

**Scope history.** 2026-10-28: drag rows in the Manager's app list, plus `Alt+↑` / `Alt+↓`.
2026-10-29: keyboard dropped, Manager drag kept. **2026-10-30: Manager drag dropped too.** The
reordering lives in the popup, where edit mode already exists (a *Remove* badge on every tile, launching
disabled, `PopupViewModel.IsEditing`), and the popup's tiles get an iPhone-style wiggle while editing.
The Manager's app list is not touched; the specification that stood here for it was removed.

**Read first** (`PopupViewModel`, `PopupWindow.xaml`): edit mode is `IsEditing`, toggled by
`ToggleEditCommand`; `RemoveAppAsync` removes from `_config.Apps` and `Apps` and calls `PersistAsync`
(`_store.SaveAsync` + `_syncService.SyncAsync`); `LaunchApp` returns at once while `IsEditing`; the tile
root is a `Grid` inside a `UniformGrid` of `EffectiveColumns`; `RemoveBadge` is its own overlay button.

**Decided with the user on 2026-10-30:**
- The dragged tile follows the pointer and the other tiles shift out of its way as it moves, as on
  a phone; no insertion line.
- The wiggle follows the existing *Settings → Enable animations* switch: off there, the tiles stay
  still (the remove badges and the pencil's highlight still show edit mode). No new setting.
- **Esc** during a drag, or dropping the tile outside the grid, puts every tile back.
- In a group tall enough to scroll, the grid scrolls by itself while the tile is held near its top
  or bottom edge.

**Steps** (each its own commit, tested by hand in between):
1. [x] **Order in the view model.** `PopupViewModel.BeginReorder` (refused outside edit mode),
   `MoveApp(from, to)` (moves the tile in `Apps` only), `CommitReorderAsync` (writes the order to
   `_config.Apps` and saves; re-syncs only when the first four — `CompositeIconGenerator.MaxTiles`
   — changed; a tile dropped where it started writes nothing) and `CancelReorder` (restores);
   leaving edit mode mid-drag cancels. `PersistAsync(sync)` gained the switch. 11 tests.
2. [x] **The drag.** `TileReorderDrag` (pointer capture on a tile, a threshold before it starts, never
   from the remove badge, only while `IsEditing`) with the pure `TileGridHit` (pointer → cell, edge
   scrolling); the dragged tile is drawn above the rest, 8 % larger; the others slide to their new
   cells and the dropped tile lands in its own (FLIP: from where each was drawn to its new slot).
   **Added with the user while trying it:** in edit mode the header's add, pin and close buttons fade
   out and the pencil slides to the header's right end; both back when editing ends. All of it
   without animation when *Enable animations* is off. Tested by hand by the user.
   **Then, also asked for by the user:** a framed *Max per row* number in the close button's place
   while editing (pencil beside it, at the pin's place): one more per click, back to 1 after 6, saved
   at once; it pops on each click and the tiles glide into their new rows
   (`PopupViewModel.CycleColumnsCommand`, `PopupWindow.GlideTilesFrom`). Tried by the user.
3. [x] **The wiggle.** `PopupWindow.Wiggle`: each tile's root (tile and badge) rotates ±1.5° about its
   centre while `IsEditing`, neighbours in opposite directions at 120–148 ms, re-applied to tiles
   rebuilt while editing; none when animations are off. `PopupWiggleTests`. Tried by the user.
4. [x] **Docs** went with each step (both guides, CHANGELOG, CLAUDE.md, this file), as the repo
   policy asks; no separate step was needed.

**Tests first:** view-model tests for the order (moved, bounds, commit saves, sync only for the first
four, cancel restores, nothing happens outside edit mode), `TileGridHitTests`, an STA test that the
tiles wiggle in edit mode and are still outside it. The drag gesture itself is checked by hand.

**Risks:** the popup closes itself (outside click, deactivation) — a drag must never be mistaken for
either; a tile click versus a drag; the popup and the Manager both write the group (ADR-005); the
scroll of a tall group during a drag.

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

**Progress**
1. [x] **Mica shows, app-wide.** The pass found two covers, not one: the opaque `WindowBackground`
   and `SidebarBackground`, and WPF clearing the HWND to black. `MicaSurfaces.TryApply` extends
   the frame (`WindowBackdrop.TryExtendFrameIntoClientArea`) and clears both; the sidebar sits on
   the material, the content pane on Fluent's layer fill (`ContentBackdropTint`), rounded where
   they meet. The Settings dialog wears it too. The window background stays transparent rather
   than translucent (a tint on it would also lie under the sidebar). Tested by hand by the user
   in both themes; `BackdropTintTests` measures contrast, `MicaSurfacesTests` the surfaces.
   The pixel test planned for the Mica area was dropped: PrintWindow draws Mica as its plain
   colour, so a headless test cannot see the material.
2. [x] **The popup's backdrop, chosen** (asked for by the user on 2026-10-30): *Mica* (default, the
   Manager's look), *Mica Alt*, *Acrylic* (today's), or *Off* (an opaque theme colour). A default
   for all popups in Settings (`AppSettings.PopupBackdrop`), and a per-group override on
   *Appearance* (`GroupConfig.Backdrop`, `null` = Settings); `PopupBackdropChoice` resolves it.
   ADR-004 amended. Looking at Settings found the themed `ComboBox` showing a record's
   `ToString()` — fixed in the same commit, with a test. Tried by the user.
3. [x] **Colour tints for the popup** (same request): *None*, the Windows accent colour (its `Dark2` /
   `Light2` shades) and *Midnight*, *Forest*, *Sunset*, laid over the material, with text contrast
   tested per theme; a default in Settings, a per-group override on *Appearance*. `PopupTints` in
   Core. **With it, asked for by the user:** the preview shows the whole popup — its header, and
   its backdrop and tint (Off and tints exactly, the materials approximately), following Settings
   and the theme. Tried by the user.
4. [x] **The spacing scale.** 42 margins and paddings moved onto 4 DIP steps (1 and 2 kept for
   hairlines); `SpacingScaleTests` gates the Manager's markup, checked to fail on a 14. Not built
   as `Controls.xaml` resources as first planned — decided with the user on 2026-10-30: a
   resource per `Thickness` is verbose and stops nothing, the test stops every stray value.
   Tried by the user.
5. [x] **Fluent icons.** The pass found them already used throughout; only *+ New group* (sidebar
   and the empty state) still wrote a text "+". It is now the Fluent + glyph beside a label, and
   `ButtonNameTests` fails on a Manager button whose label starts with a typed symbol. Tried by
   the user (the empty-state button by code only: showing it needs a profile without groups).
6. [ ] **Screenshots**, retaken with the recipe in `CLAUDE.md` (the user switches the display to
   150 % for them).

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
| C6 — settings apply as they change; get-started view when there are no groups | `a6cb480`, merged as `8bec1ae` | merged |
| C1 — grid settings, part 1: most columns per row, icon size and app names per group on *Appearance* (upstream #29) | `b517f26`, `19587c7`, merged as `c3e5cdc` | merged |
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
