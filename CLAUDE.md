# TaskbarFolders

Windows-only WPF/.NET 8 desktop app. iOS-style taskbar grouping for Windows 11.

## Commands

```bash
dotnet build TaskbarFolders.sln -c Release
dotnet test  TaskbarFolders.sln -c Release
dotnet format --verify-no-changes      # CI enforces — run before commit
```

**Local test requirement:** Windows only — Linux/macOS cannot run the tests. `Microsoft.WindowsDesktop.App 8.0.x x64` is what the tests target; if the machine only has a newer major (this one has 10.x), run `DOTNET_ROLL_FORWARD=LatestMajor dotnet test …` and everything passes. The same variable is needed to launch the built exes directly, or the host exits with 0x80008096. SDK is pinned in `global.json` (8.0.100, `rollForward=latestMajor`).

## Architecture

Four-project solution. Dependencies flow Manager/Launcher → Core → Shared.

| Project | Purpose | Constraint |
|---|---|---|
| `TaskbarFolders.Shared` | Models, JSON persistence, file logging | **No Windows-only APIs** — keep portable |
| `TaskbarFolders.Core` | Icon engine, Win32 P/Invoke, shortcut generation, group sync (`Core.Groups`) | `[SupportedOSPlatform("windows")]` |
| `TaskbarFolders.Manager` | WPF group-CRUD app | DI via `ManagerServiceCollectionExtensions` |
| `TaskbarFolders.Launcher` | Per-group popup (short-lived per click) | DI via `LauncherServiceCollectionExtensions` |

## Project Conventions

- **Strict MVVM** — no business logic in code-behind. Commands via `[RelayCommand]` (CommunityToolkit.Mvvm 8.3.2).
- **DI everywhere** — register in `*ServiceCollectionExtensions`, inject via constructor. `CompositionRootTests` validates the Manager graph at build.
- **File-scoped namespaces**, `_camelCase` private fields, PascalCase for `const` fields.
- **XML doc comments required** on public members. Enforced: `GenerateDocumentationFile` is on for `src` (off for `tests`), so `CS1591` is actually emitted, and `TreatWarningsAsErrors` makes it fail the build. Until that property was set the rule was documented but inert.
- **Async I/O** — all persistence and shell calls async, except `IIconCache.TryGet/Set` (sync on purpose; UI hot path).

## Non-Obvious Patterns

**Atomic writes** — used in `JsonGroupConfigStore`, `JsonAppSettingsStore`, `IcoFileWriter`, `FileSystemIconCache`:

```csharp
File.WriteAllBytes(target + ".tmp", bytes);
File.Move(target + ".tmp", target, overwrite: true);
```

New persistence code must follow this.

**HICON discipline** — every `SHGetFileInfo` / `IImageList.GetIcon` `HICON` must be released in `finally` via `DestroyIcon`; every COM RCW via `Marshal.FinalReleaseComObject`. Instantiate the RCW **before** `try` so a CLSID-not-registered failure doesn't leave the `finally` with a half-constructed object (see `ShellIconExtractor.ResolvePath`).

**Pin-to-taskbar = Strategy C** — per-group `.lnk` with a distinct AUMID stamped via `IPropertyStore` + `PKEY_AppUserModel_ID`. The `Launcher.exe` is shared; grouping is purely AUMID-driven. **Do not** introduce per-group `.exe` copies + `UpdateResource` (Strategy A) — ruled out in M5 spike because unsigned dynamically-modified PEs trigger Defender false positives.

**GroupId validation** — `AppDataPathProvider` enforces `^[A-Za-z0-9._-]{1,96}$`. Any path-derived storage code (groups, per-group shortcut dirs) must funnel through `GetGroupFile` / `GetGroupDirectory` so the validation isn't bypassed.

**Group icons are versioned; the pinned copy is updated, never recreated** — `icons/<id>-<8 hex>.ico`, a new name per image, because the shell caches a shortcut's icon by path (ADR-006). Never assume a fixed icon path: ask for the newest file `IsGroupIconFile` accepts. Sync may rewrite the icon location of `.lnk` files in `PinnedTaskbarDirectory` that carry a group AUMID — nothing else there, and it never creates or deletes files in that folder — except that the uninstaller unpins group tiles there through the shell's `taskbarunpin` verb, which removes the file itself (ADR-008). Delete old icon versions only after every shortcut points at the new one.

**Start Menu anchors are found by AUMID, never by file name** — the anchor's file name is the sanitised *display name*, its identity the id-derived AUMID, so a name-based lookup misses every anchor written under an earlier name (that is how renames left duplicate anchors behind, issue #28). Anything that finds, replaces or deletes anchors goes through `IShortcutReader.TryReadAumid`. A new anchor removes its same-AUMID predecessor only *after* it has been written — until then the old one is what keeps a pin alive. The orphan sweep takes known ids from `groups/*.json` file names, not from `LoadAllAsync`, which skips unparsable configs whose tiles may still be pinned. Files without a `TaskbarFolders.Group.` AUMID in that folder are not ours: leave them.

**JSON config** — `Id` in `GroupConfig` is always reconstructed from the file name on load (disk layout is the source of truth; JSON `id` field is ignored). See `JsonGroupConfigStore.LoadFromFileAsync`.

**Cross-thread BitmapSource** — `Freeze()` inside the producer lambda *before* `await` crosses back to the UI thread (see `PopupViewModel.LoadIconForAsync`). An unfrozen bitmap that crosses threads carries the wrong dispatcher affinity and the WPF Image binding throws.

**A missing icon stays `null` until it is drawn** — `IIconExtractor` returns `null` for an app without an icon, and callers keep that distinction: the fallback `PlaceholderIcon` is substituted only where an icon is shown or composed, and never stored in the icon cache (a cached placeholder would hide an icon that appears later). The popup draws it only on `PopupAppViewModel.IconMissing`, because a `null` icon there also means "still loading" (upstream #27). `ShellIconExtractor` serialises its shell calls behind a process-wide lock: called from many thread-pool threads at once, `SHGetFileInfo`/`SHGetImageList` returned null for 8 of 9 system executables, which is what blanked random popup tiles. Keep the lock; `ExtractIcon_FindsEveryIcon_WhenCalledFromManyThreadsAtOnce` fails without it.

**CancellationTokenSource fields → IDisposable** — Adding a CTS field triggers CA1001. Make the owner `sealed … IDisposable` and have `Dispose` call the cancel-and-dispose helper (`PopupViewModel`, `GroupEditorViewModel` follow this).

**Launcher failure paths must reach the file log** — `Trace`-only diagnostics are forbidden in the Launcher. Every early-exit (`Shutdown(n)`) and every catch that ends startup goes through `StartupFailureLogger` (DI-free, never throws, same line format/file as `FileLogger`). The v0.4.x popup regression exited with code 3 on every click for weeks while the log showed nothing — because the failure was `Trace`-only. Exit codes are documented in `App` remarks: 1 = no group id, 3 = startup threw, 4 = unhandled dispatcher exception.

**TaskbarManager takes no HWND** — `InitializeWithWindow.Initialize` applies to WinRT types that own a modal surface (`FileOpenPicker`, `FolderPicker`); `Windows.UI.Shell.TaskbarManager` comes from `GetDefault()` and does not implement `IInitializeWithWindow`. Calling it throws `InvalidCastException` from the CsWinRT marshaller. That one line made everything after it in `TaskbarPinRunner` unreachable from v0.4.0 to v0.4.8 — including the diagnostic meant to explain pin failures. `RequestPinCurrentAppAsync` parents its own dialog; what it needs is for the process to hold the **foreground**, which is a `Window.Activate()` concern, not an interop one. Related: never trust its return value — verify a pin landed by matching AUMIDs in the shell's pinned-items folder, because it has been observed reporting success while persisting nothing.

**A disabled control cannot explain itself** — WPF does not render a `ToolTip` on a disabled element unless `ToolTipService.ShowOnDisabled="True"` is set on it. v0.4.4 disabled `+ Add` until a group name was entered and attached a tooltip saying so; the tooltip was unreachable in exactly the state it described, and a fresh installer could not work out how to create a group at all (fixed in v0.4.6). Any control that is conditionally disabled must either set `ShowOnDisabled` or carry the explanation somewhere always visible — or better, not need disabling: `+ Add` and its name box were replaced by **+ New group**, which creates "New group" and raises `RenameRequested` so the view selects the name for typing. Related: `FocusManager.FocusedElement` on the `Window` (now the group list) is the declarative way to put the caret where input is expected on open — it sidesteps the `Loaded`-fires-inside-`Show()` trap below.

**Pin mode's exit code is its result, so pin mode shuts down explicitly** — `ShutdownMode.OnExplicitShutdown` in `RunPinModeAsync`. Under the default `OnLastWindowClose`, closing `PinHostWindow` started a shutdown with code 0 and the `Shutdown(exitCode)` after it was ignored; every pin outcome reached the Manager as "Pinned" until this was found. A log line saying "exit code 2" proves nothing about the process exit code — check `Process.ExitCode`.

**Template contracts in Manager styles** — a restyled `TabControl` must keep `PART_SelectedContentHost` as the name of its content presenter: `TabItemAutomationPeer` finds the selected tab's content through it, and without it UI Automation (and screen readers) saw nothing on any tab. Header font and colour belong on the header presenter inside the `TabItem` template, never on the `TabItem` (they are inherited by the tab's whole content), and the gap between tabs belongs inside the template too, since `TabPanel` arranges tabs without their `Margin` and clips the label instead. `ControlStyleTests` renders both.

**Implicit Button theme styles beat inherited text colour in the popup too** — the v0.4.7 Manager fix (`Themes/Controls.xaml`) never reached the Launcher, whose tiles are `Button`s styled by `AppTileButton`. That style must set `Foreground` itself; `PopupWindowThemeTests` checks the realised label colour in both themes. The header's `PopupIconButton` follows the same rule.

**Every popup dismissal is a `Close()`; `OnClosing` owns the fade** — do not add a separate close path. With animations on, `OnClosing` cancels the first request and plays `CloseAnimation`; `FinishClose` closes for real on `Completed` or after a 300 ms safety net. The outside-click hook (`OutsideClickWatcher`, `WH_MOUSE_LL`) is installed on `ContentRendered`, never earlier: a low-level hook on a thread that is not pumping messages stalls every mouse event on the desktop. Its delegate lives in a field so the GC cannot collect it under the native hook. The outside-click hook, `Deactivated` and the never-activated fallback all ask `DismissesOnOutsideInteraction`, which is false while a dialog the popup owns is open (`_dismissSuspended`, or it closes under its own picker) and while it is pinned (`IsPinned`, which also keeps a successful launch from closing it). Add new dismissal triggers there, not beside it. A pinned popup still closes on a press on its own tile: the hook raises `OutsidePress` first and closes when `App` marks `IsOnOpeningTile`.

**Command-line adds are serialised per group** — Explorer starts one launcher per selected file. `CommandLineAppAdder` loads, adds and saves inside `Local\TaskbarFolders.Group.<id>.Edit` (a named `Semaphore`: a `Mutex` must be released on the thread that took it, and the work awaits). `PopupPresence` is claimed *before* adding, so exactly one of the concurrent launchers shows the popup; the others signal it to reload. Only the add path defers to an existing popup — a plain tile click never does, because the previous popup may still be fading out.

**A second tile click closes the popup, through a marker, not through presence** — the press on the tile reaches the outside-click hook (button *down*) well before the shell starts the next launcher (button *up* plus process start), so the old popup is gone by the time the new one could look for it. `OutsidePress` fires synchronously in the hook; if the press is within `TileToggle.ToleranceDip` of the click that opened the popup, `App` creates `Local\TaskbarFolders.Group.<id>.Dismissed` and `OnExit` keeps the windowless process alive until the next launcher consumes it or `TileToggle.Window` (1.5 s) passes. Only a plain click (no files) consumes it. Do not move the marker to `Closed`/`OnExit`: the fade delays those past the new launcher's check.

**Explorer's menu is a registry copy of the group list** (ADR-007) — `ExplorerContextMenuService` deletes and rewrites `HKCU\Software\Classes\{exefile,lnkfile}\shell\TaskbarFolders.Groups` as a whole. Anything that changes which groups exist or their names must end in `MainWindowViewModel.GroupListChanged`; settings changes in `SettingsViewModel.Saved`. Never write per-group icons there: icon paths are versioned (ADR-006) and go stale. Registry access goes through `IRegistryWriter`; tests use a recording fake and must never touch the real HKCU.

**Two processes write group configs** (ADR-005) — the Manager and the popup, which edits only its own group. Whoever saves a group runs `IGroupSyncService.SyncAsync` afterwards (it lives in `Core.Groups` for that reason), and whoever caches groups re-reads them before saving (the Manager does on window activation). That re-read stands down while `GroupEditorViewModel.IsSaving` — closing a file picker activates the window mid-save, and reloading then replaced the edited group and crashed the add. Editor commands that await take `_boundItem` into a local first; a reload can rebind the editor under them. The same goes for the header name box, which commits on focus loss: a click on another group moves the focus and the selection while the rename is still saving, so `RenameGroupAsync` writes `EditedGroupName` back only if the renamed group is still selected. Turning a picked file into an app always goes through `AppEntry.TryCreateFromPath`.

**The popup's window mode is fixed before the HWND exists** — DWM never paints a system backdrop behind a layered (`AllowsTransparency=True`) window, so on Windows 11 22H2+ the constructor switches the popup to a non-layered window with a full-client `WindowChrome` glass frame and DWM acrylic; elsewhere it stays layered with the `PopupFallbackBackground` panel. Both modes are live code. See ADR-004.

**Window accepts only identity RenderTransforms** — `Window.CoerceRenderTransform` throws `InvalidOperationException` for any non-identity transform, including one declared in XAML (kills `InitializeComponent`). Scale/animate a child element (`ChromeRoot` in `PopupWindow`) instead. Related: a `Window` raises `Loaded` synchronously *inside* `Show()` — subscribe before `Show()` or the handler never runs.

**Never write a property that carries a `OneWay` binding** — a local value *replaces* the `BindingExpression`, and it does not come back. `ClearValue` removes the local value only, so the property falls to its next source — for `UIElement.Visibility` that is the DP default, `Visible`. PR #31 forced `ErrorStrip.Visibility = Visible` to measure the popup's launch-failure strip and cleared it in a `finally`; from the first failed launch onward the binding was gone, and the next click — which clears `LastError` — left an empty red bar stealing 36 DIP from the fixed 96 px tiles, reproducing the very bug being fixed. If code must read a bound property's current value, call `BindingExpression.UpdateTarget()`; if it must override one temporarily, drive the property from a `Style` setter or trigger so a local value is legally reversible. Related: `Measure` short-circuits when the element is measure-valid and the constraint is unchanged, so a repeat sizing pass needs `InvalidateMeasure()` first or it reads the previous `DesiredSize`.

**Size the popup by measuring, not by counting rows** — the error strip docks `Bottom` in a fill-last `DockPanel`, so any height derived from tile rows alone gets eaten from the tiles. `PopupWindow.ApplySizeAndPlacement` measures `ChromeRoot` and clamps its `DesiredSize`. Also note `FrameworkElement.DesiredSize` **includes** `Margin` — adding it again double-counts, which is how the same PR ended up 8 DIP too tall. Past `MaxHeight` the grid scrolls in `TileScroller`; whether its bar shows is read from the laid-out `ComputedVerticalScrollBarVisibility`, not inferred from `DesiredSize` — on the first pass (in `SourceInitialized`) the tiles are not yet in it, and the window came out 17 px too narrow. The window is widened by the bar so the 96 px tiles are not squeezed.

**WPF-driven tests are allowed but must say why** — the suite is otherwise headless; `ControlStyleTests` and `PopupWindowSizingTests` marshal onto an STA thread deliberately, because the behaviour under test is WPF's own. A test that shows a window **must** set `ShowActivated = false`: otherwise `Deactivated` arrives during teardown, `OnDeactivated` calls `Close()` on a closing window, and the exception escapes a `WndProc` and kills the whole test host instead of failing one test. A test class that constructs a `PopupWindow` **must** carry `[Collection(PopupWindowTestGroup.Name)]`: the popup's `WindowChrome` writes to a process-wide WPF dictionary without locking, and two popups built in parallel on separate STA threads corrupt it — a random failure in whichever window test runs first.

**DPI unit contract (fixed in v0.4.3)** — everything crossing the Win32 boundary (taskbar/monitor rects, `GetCursorPos`, `ICursorAnchor`) is **device pixels**; `TaskbarPositionHelper.CalculatePlacement` converts to WPF DIPs exactly once using the target monitor's effective DPI (`GetDpiForMonitor`). Do not pre-convert values before seeding the anchor and do not feed DIP values into `CalculatePlacement`'s Win32 parameters — a double conversion reintroduces the pre-v0.4.3 placement drift at ≠100% scaling.

## Analyzer Suppressions (rationale)

Project-wide `EnforceCodeStyleInBuild=true` + `TreatWarningsAsErrors=true`. Suppressions in `.editorconfig`:

- `CA1716` global — `TaskbarFolders.Shared` namespace clashes with a VB.NET reserved word (irrelevant for Windows-only C# app).
- `CA1848` / `CA1873` global — `LoggerMessage` source generators not used; cold-path logging only. Hot paths can opt in.
- `CA1707` / `CA1859` / `CA1861` `tests/**` only — xUnit naming convention + perf rules don't apply to test code.
- `IDE1006` is **not** suppressed — a separate `required_modifiers=const` naming rule allows `PascalCase` const fields alongside `_camelCase` private fields, so both satisfy the rule rather than one being exempted from it.

If you suppress an analyzer, add the rationale in `.editorconfig` next to the suppression.

## Definition of Done

Nothing is finished until every line below holds. Each one is here because it was violated
in this repository and cost a release, a broken feature, or a false claim — the reasons are
kept so the rule stays arguable rather than ritual.

- **Documentation moves with the code, in the same commit.** See the table below. User-visible
  changes need **both** `docs/user-guide.md` and `docs/benutzerhandbuch.md`.
- **Screenshots are part of the interface.** If the UI changes visibly, retake them. Recipe, so
  they stay comparable: display scaling at 150 %, crop to `DWMWA_EXTENDED_FRAME_BOUNDS` with a
  2 px inset (the shadow region otherwise lets the desktop bleed into the edges), same group
  selected in both themes. Files live in `assets/screenshots/`.
- **Evaluate the format gate, do not announce it.** `dotnet format --verify-no-changes` prints
  errors and still exits 0 in a pipeline — read the output. New `.cs` files need **CRLF and a
  UTF-8 BOM** or the gate fails on `ENDOFLINE`/`CHARSET`.
- **Look at visual changes.** A contrast or layout claim rests on having seen the result:
  screenshot the running window, or render the control offscreen with `RenderTargetBitmap` on an
  STA thread. Dark mode sat at 1.2:1 through several releases because nobody looked.
- **Run interop and WinRT paths, do not just compile them.** The suite is headless and cannot
  reach WinRT. Pin-to-taskbar was broken from v0.4.0 to v0.4.8 behind a bad cast that no test
  could see; `Launcher.exe --pin-mode --group-id <id>` plus the log would have caught it in
  one run.
- **Verify the verifier.** A gate that never fires is not a gate. When adding one, feed it an
  input that must fail and confirm it does.
- **Coverage is gated, not aspired to.** CI fails below 65 % line coverage and at exactly 0 %.
  Release uses `DebugType=embedded`; with `none` coverlet has nothing to instrument and every
  report is silently empty.
- **Release notes come from `CHANGELOG.md`.** `generate_release_notes` produces a bare compare
  link here, because releases are cut from direct pushes.
- **Repo presentation is part of the product.** Description and topics on the repository,
  author in `Directory.Build.props`, `LICENSE` and `installer/setup.iss` — all kept accurate.
- **No inbox address in a committed file.** Security reports go through GitHub's private
  vulnerability reporting, conduct reports through the profile and GitHub's abuse form. The
  repository's `user.email` is the GitHub noreply address, set repo-locally, so commits do
  not leak one either. A published address collects spam, not reports.
- **Known limitations become issues.** A limitation recorded only in prose is invisible; open
  one with the file references and acceptance criteria.
- **Branch protection**: `main` and `develop` reject force-pushes and deletion; `develop`
  requires the `build` check on pull requests. Direct pushes to `develop` stay allowed —
  solo-maintained, no reviewer to wait for.
- **No AI/agent mentions** in any human-facing committed file. Tooling files like this one are
  exempt. Grep the diff before committing.

## Repo Policy

- **Documentation moves with the code.** Every change, implementation or update also brings the documentation it affects up to date, **in the same commit** — not in a follow-up, not "later". A behaviour change whose documentation still describes the old behaviour is not finished. This is not bureaucracy: the guides once sat three minor versions behind and actively misdescribed the pinning flow, which is how a user ends up following instructions for a design the project had abandoned. Which files a change touches:

  | Changed | Also update |
  |---|---|
  | User-visible behaviour, a button, a dialog, a setting | `docs/user-guide.md` **and** `docs/benutzerhandbuch.md` — the pair is only useful if both move together |
  | Component boundaries, data flow, storage layout, startup order | `docs/architecture.md` |
  | Public types or their signatures | `docs/api-reference.md` |
  | Build, test, debug, conventions, analyzer rules | `docs/developer-guide.md` |
  | A new failure mode, exit code, or known limitation | `docs/troubleshooting.md` |
  | Anything about versioning, tagging or the pipeline | `docs/release-process.md` |
  | A decision that constrains future work | a new ADR under `docs/adr/`, added to its index |
  | A new convention or invariant | this file |
  | A roadmap item finished, started, reordered, split, added or dropped | `plan.md` |
  | Anything at all | a `CHANGELOG.md` entry under `[Unreleased]` |

- **The roadmap lives in `plan.md` and is kept live.** It is the single record of what is done, what is in progress and the agreed order of what comes next — not session memory, not chat. Update it the moment the state changes: an item is ticked off in the same commit that finishes it (with its commit hash and branch), and a change of order, scope or a split into steps is written down as soon as it is agreed, even if no code changes with it. Bump its *Last updated* date with every edit. Read it at the start of a session before picking up work; if it disagrees with the git history, fix the file first. A plan that lags behind the work is worse than none, because the next session will follow it.

- **No AI/agent mentions** in code, comments, commits, docs, or any human-facing committed file. Tooling files like this one are exempt.
- **Conventional Commits** — `<type>(<scope>): <desc>`. Types per `CONTRIBUTING.md`.
- **Branching** — `develop` is integration, `main` is releases, feature branches off `develop`.

## Where things live at runtime

- Group configs: `%APPDATA%/TaskbarFolders/groups/<id>.json`
- Per-group composite icon: `%APPDATA%/TaskbarFolders/icons/<id>.ico`
- Per-group shortcut (Strategy C `.lnk`): `%APPDATA%/TaskbarFolders/shortcuts/<id>.lnk`
- Settings: `%APPDATA%/TaskbarFolders/settings.json`
- Icon cache: `%APPDATA%/TaskbarFolders/icons/cache/<sha256>.png`
- Chosen group icon (replaces the composite while present): `%APPDATA%/TaskbarFolders/icons/custom/<id>.png`
- Logs: `%APPDATA%/TaskbarFolders/logs/{manager,launcher}-yyyy-MM-dd.log`

**Uninstall leaves nothing behind, and there is exactly one uninstall procedure** (ADR-008) — `Uninstaller`, run as `TaskbarFolders.Manager.exe --uninstall`, by Inno's `[UninstallRun]` (with `--no-confirm`) and by the portable `Uninstall.cmd`; *Settings → Uninstall TaskbarFolders…* only starts one of those. **Anything new the app writes outside its program folder — a registry value, a file, a shell registration — must be removed there in the same change**, with a test in `UninstallerTests`. It deletes the user's data too, last, and logs nothing after that (`FileLogger` drops lines whose folder is gone rather than re-create it). What the running Manager cannot delete — its last log lines, `%TEMP%\.net\TaskbarFolders.*` — is the callers' backstop: `[UninstallDelete]` and the end of `Uninstall.cmd`. That script deletes files only on exit code 0 or 1; a crashed or killed Manager returns something else, and `if errorlevel 2` alone would have read a negative crash code as success. Setup is per user (`PrivilegesRequired=lowest`) because Inno cannot run anything as the original user while uninstalling: an elevated cleanup could not unpin the user's tiles and might clean another account's `HKCU`.

## Installed layout (read before touching `LauncherPathResolver`)

The Inno Setup installer (per user, `{app}` = `%LOCALAPPDATA%\Programs\TaskbarFolders`) and the portable ZIP both ship Manager and Launcher in **sibling folders**, not one directory; next to them sits `unins000.exe` or `Uninstall.cmd`, which `InstallLocation.FindUninstaller` looks for:

```
{app}\Manager\TaskbarFolders.Manager.exe
{app}\Launcher\TaskbarFolders.Launcher.exe
```

`LauncherPathResolver` probes three layouts (`side-by-side` → `sibling folder` → `dev sln walk-up`). Any new packaging must match one of those, or the resolver gains a fourth probe. The v0.2.0 release shipped without the sibling probe and the `Show shortcut...` button was silently broken for every install — regression test in `LauncherPathResolverTests.TryResolveFrom_FindsLauncherInSiblingFolder_MatchingInstallerLayout` guards against repeating it.

## Workflow for non-trivial work

This project is solo-maintained; senior-dev workflow expectations apply.

**1. Analyse before changing anything.** Read the files end-to-end (XAML binding → VM command → service → P/Invoke), check the runtime log under `%APPDATA%/TaskbarFolders/logs/`, and form a falsifiable root-cause hypothesis. Quote line numbers when you state the cause.

**2. Orchestrate subagents when the task warrants it.** Rule of thumb: trivial edits go direct; multi-file fixes get a `Plan` agent first; bug investigations across more than two files get an `Explore` agent in parallel; anything user-blocking gets a `general-purpose` agent for independent code review **before push**. Brief each agent self-contained (file paths, line numbers, constraints from this CLAUDE.md) — they cannot see the conversation.

**3. Bug-fixing waves.** Group changes into reviewable commits: one commit per behavioural concern (e.g. resolver fix, UX fix, UX defence-in-depth), each with its own tests. Run `dotnet build -c Release` after every wave, and `DOTNET_ROLL_FORWARD=LatestMajor dotnet test -c Release` — tests do run locally, see above. Address code-review findings as a polish commit on top — never amend a pushed commit, never amend across reviewable boundaries.

**4. Complete testing — including the runtime layout that ships.** Build + unit tests in CI is necessary but not sufficient. The v0.2.0 "Show shortcut" bug existed *only* in the installed and portable layouts; the dev `dotnet run` and the CI test runner both used a layout where the launcher happened to be findable. Every release-eligible change must be verified in all three runtimes it touches:

  - **Unit tests** — `dotnet test -c Release` (CI) — must be green before push, no exceptions. Pushing red and "fixing forward" is forbidden because it muddies bisects.
  - **Format gate** — `dotnet format --verify-no-changes` (CI) — fix locally before push.
  - **Dev run** — `dotnet run --project src/TaskbarFolders.Manager` — for any change that touches MVVM, DI, XAML, or services. Click the affected button.
  - **Installer/portable smoke** — after tagging, before announcing: actually install `TaskbarFolders-Setup.exe` (or unzip the portable) and walk the user-visible happy path end-to-end (create group → drop apps → click the affected feature). Two minutes of clicking catches what 200 unit tests do not. **This step would have caught the v0.2.0 bug before users did.**
  - **Log inspection** — open `%APPDATA%/TaskbarFolders/logs/manager-*.log` after the smoke test. A warning or error line with no user-visible counterpart is a UX bug.

  If you cannot run the installer smoke (e.g. no Windows machine to hand), say so explicitly in the PR/commit message rather than imply the feature is verified. Future-you reading the log will know what was actually tested.

**5. Commits.** Conventional Commits (`fix(manager): …`, `feat(core): …`). Body explains the *why* and the *blast radius*, not the *what* — assume the reader has the diff. No AI/agent mentions, and no `Co-Authored-By` trailer naming a tool — human co-authors are fine and should be credited. Run `dotnet format` **before staging each commit** — *not* between commits. v0.2.1 CI failed because format auto-fixed BOMs on files that were committed in Wave 1, but the fix landed in the working tree only and was never staged. Format → `git status` → stage everything modified → commit.

**6. Pushing & releasing.** Bug fixes land on `develop` directly (no PR — solo-maintained). Patch releases tag `v0.x.y` from `develop`; `release.yml` publishes the assets. Bump `Directory.Build.props:Version`, `installer/setup.iss:MyAppVersion`, `src/TaskbarFolders.Manager/app.manifest:assemblyIdentity/@version` (four-part, e.g. `0.4.5.0`), the README status banner, and add a `CHANGELOG.md` section in the same commit as the tag-eligible state. Full checklist in `docs/release-process.md`. If a fix is user-blocking on a shipped version, cut a patch release the same day. **Wait for the installer-smoke pass before announcing the release as available** — the tag and the assets exist before they're verified.

**7. Documentation upkeep.** Before calling any change done, walk the table in Repo Policy and update every row the change touched, in the same commit — including `plan.md` when the change finishes or advances a roadmap item. When a fix introduces a new convention or invariant, surface it here too — the next session reads this file before doing anything else.

## Release

Pushing a `v*` tag triggers `release.yml`: builds, publishes self-contained, builds the Inno Setup installer (ISCC is pre-installed on `windows-latest`), uploads `TaskbarFolders-portable.zip` + `TaskbarFolders-Setup.exe`. The release job needs `permissions: contents: write` (already set).

**Asset size baseline (v0.4.10, measured from the published release):** portable ZIP ≈ 167 MB (166,691,561 B), Setup.exe ≈ 112 MB (112,208,154 B) — about +145 KB each versus v0.4.9, which is the cost of `DebugType=embedded`. A collapse to a fraction of this means a publish step produced a framework-dependent build. `PublishReadyToRun=true` (Launcher + Manager) roughly doubled the size vs v0.2.1 because the entire .NET runtime is AOT-compiled into the publish. Plan estimates that assume "~10 MB R2R cost" are wildly low — count on +50-70 MB per binary.
