# Developer Guide

Building, testing and debugging TaskbarFolders, plus the conventions and invariants the build enforces. For how the pieces fit together, read [architecture.md](architecture.md) first.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). `global.json` pins 8.0.100 with `rollForward: latestMajor`, so a newer SDK is fine.
- Windows. The whole solution targets `net8.0-windows*`; it does not build or test on Linux or macOS.
- Any of Visual Studio 2022, JetBrains Rider, or VS Code with the C# Dev Kit.

Running the test suite needs the `Microsoft.WindowsDesktop.App 8.0.x` runtime. If your machine only has a newer major, prefix commands with `DOTNET_ROLL_FORWARD=LatestMajor` — the same applies to running the built executables directly.

## Getting started

```bash
git clone https://github.com/gianluca-schwekendiek/taskbar-grouping.git
cd taskbar-grouping
dotnet build TaskbarFolders.sln -c Release
```

The clone directory is `taskbar-grouping`; the product name is TaskbarFolders.

## Solution layout

| Project | Type | Target framework | Purpose |
|---|---|---|---|
| `TaskbarFolders.Shared` | Library | `net8.0-windows` | Models, JSON persistence, path provider, file logging |
| `TaskbarFolders.Core` | Library | `net8.0-windows` | Icon engine, Win32/COM interop, shortcut generation |
| `TaskbarFolders.Manager` | WPF exe | `net8.0-windows` | Group management UI |
| `TaskbarFolders.Launcher` | WPF exe | `net8.0-windows10.0.19041.0` | Popup and pin runner |
| `TaskbarFolders.Core.Tests` | Tests | `net8.0-windows` | |
| `TaskbarFolders.Manager.Tests` | Tests | `net8.0-windows` | |
| `TaskbarFolders.Launcher.Tests` | Tests | `net8.0-windows10.0.19041.0` | |

The Launcher's higher target framework is deliberate: it needs the Windows 10 1903+ SDK projections for `Windows.UI.Shell.TaskbarManager`. Keep this in mind whenever code derives paths from a build output directory — the two executables do **not** share a framework folder name.

Dependencies flow Manager/Launcher → Core → Shared. `TaskbarFolders.Shared` must stay free of Windows-only APIs.

## Building and publishing

```bash
dotnet build TaskbarFolders.sln -c Release
```

Publishing mirrors what the release workflow does. Both executables are published **separately, into sibling folders** — the installer script consumes exactly that layout, and the Manager's launcher probe depends on it:

```powershell
dotnet publish src/TaskbarFolders.Manager/TaskbarFolders.Manager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/Manager
```

```powershell
dotnet publish src/TaskbarFolders.Launcher/TaskbarFolders.Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/Launcher
```

Both projects also set `PublishReadyToRun`, which is why the published output is large. Do not publish both projects into one flat folder — the installer will not find them.

## Running

```bash
dotnet run --project src/TaskbarFolders.Manager
```

```bash
dotnet run --project src/TaskbarFolders.Launcher -- --group-id <id>
```

```bash
dotnet run --project src/TaskbarFolders.Launcher -- --pin-mode --group-id <id>
```

The launcher accepts `--group-id <value>` and the `--pin-mode` flag; unrecognised arguments are ignored. Without `--group-id` it falls back to the AppUserModelID Windows assigned the process, which is how a pinned tile starts it.

A dev run uses your real `%APPDATA%\TaskbarFolders`, so groups you create while debugging are the same ones the installed build sees.

## Testing

```bash
dotnet test TaskbarFolders.sln -c Release
```

```bash
dotnet test TaskbarFolders.sln -c Release --collect:"XPlat Code Coverage"
```

Roughly 330 test cases across the three projects, using xUnit with Moq and FluentAssertions. FluentAssertions is pinned below version 8 — that release changed to a commercial licence, and Dependabot is configured to ignore it.

Most tests run headless: no WPF `Application`, view models exercised directly. A handful do touch the real shell — icon extraction from `notepad.exe` and `cmd.exe`, and COM shortcut creation into a temp directory — so they need a real Windows install rather than a stripped container.

Two classes go further and drive WPF itself, because what they cover *is* WPF behaviour rather than arithmetic. `ControlStyleTests` realises control templates so a malformed one fails the build instead of the Settings dialog. `PopupWindowSizingTests` constructs the real popup and shows it off-screen: an `ItemsControl` only builds its containers inside a live layout pass, and `UpdateLayout` on a window with no `PresentationSource` does nothing, so there is no headless way to measure the tile grid. They marshal their body onto a short-lived STA thread with `StaThread.Run`, since xUnit runs MTA.

Helpers every test project uses live once, in `tests/Shared/`, linked into each test project as source: `StaThread` (`Run`, `Pump`, `PumpUntil` — wait for a condition rather than a fixed time, which a loaded machine overruns), `RepoRoot.Path` for tests that read XAML or assets from the tree, and `TestImages.Solid` for a frozen bitmap. Popup window tests build their window with `TestPopup.Show` in the Launcher tests, which also sets `ShowActivated = false`.

Every test class that constructs a `PopupWindow` — the sizing, theme, animation, close and keyboard tests — belongs to the `PopupWindowTestGroup` xUnit collection, so they run one after another. On Windows 11 22H2+ the popup attaches a `WindowChrome`, whose worker registers a handler in a process-wide WPF dictionary that has no lock; two popups constructed at once on parallel STA threads corrupt it and fail a test at random.

If you add another of these, set `ShowActivated = false` before `Show()`. An activated popup receives a `Deactivated` from the window manager while the test tears it down, `PopupWindow.OnDeactivated` calls `Close()` on a window that is already closing, and the resulting exception escapes a `WndProc` and crashes the entire test host rather than failing one test.

Some tests are timing-sensitive (cache pruning, log rotation, debounced preview refresh) and have already been hardened once against slow CI runners. If you add one, poll with a generous deadline and exit early on success rather than sleeping a fixed interval.

Coverage is collected in CI, merged with ReportGenerator and **gated**: the job fails below 65 % line coverage, and also fails at exactly 0 %, since an empty report is indistinguishable from an untested code base. The current figure is 69.2 %.

Release builds use `DebugType=embedded` for this reason. With `none` — as it was until v0.4.10 — coverlet has nothing to instrument against and every report is silently empty. Embedding keeps collection working without emitting separate `.pdb` files that would then have to be kept out of the published artifacts.

## Code style and analyzers

```bash
dotnet format TaskbarFolders.sln --verify-no-changes
```

```bash
dotnet format TaskbarFolders.sln
```

CI runs the verify form as a hard gate. Run the fixing form **before staging each commit**, not between commits — formatting fixes that land in the working tree but never get staged are how a release CI run has failed before.

The build is strict by configuration: `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and `AnalysisLevel=latest-recommended`. A style violation is a build error, not a warning.

Conventions:

- File-scoped namespaces.
- `_camelCase` for private fields; `PascalCase` for constants — a dedicated naming rule keeps both valid side by side.
- XML documentation on every public member. `CS1591` is an error — `Directory.Build.props` generates a documentation file for the `src` projects, which is what makes the compiler emit the diagnostic at all, and `TreatWarningsAsErrors` does the rest. The test projects switch the documentation file back off: xUnit's classes and `[Fact]` methods are public by design, and commenting each one would add noise rather than information.
- Strict MVVM. No business logic in code-behind; commands via `[RelayCommand]`.
- Constructor injection everywhere; register services in the project's `*ServiceCollectionExtensions`. `CompositionRootTests` validates that the graph resolves.
- Async I/O throughout, with one deliberate exception: `IIconCache.TryGet`/`Set` are synchronous because they sit on the popup's hot path.
- One spacing scale in the Manager's markup: every literal `Margin` and `Padding` in `Views/*.xaml` and `Themes/Controls.xaml` is one of 0, 4, 8, 12, 16, 20, 24, 28 or 32 DIP, or 1 and 2 for hairlines. `SpacingScaleTests` fails on anything else; bound values are not checked.

Six analyzer rules are suppressed, each with its rationale next to the suppression in `.editorconfig`: `CA1716` globally (namespace name versus a VB.NET keyword), `CA1848`/`CA1873` globally (no `LoggerMessage` source generators; cold-path logging only — opt back in locally for a hot path), and `CA1707`/`CA1859`/`CA1861` under `tests/**` only. If you add a suppression, add the reason beside it.

## Invariants worth knowing before you touch things

These are the ones that have actually broken releases.

- **HICON discipline.** Every `HICON` from `SHGetFileInfo` or `IImageList.GetIcon` gets `DestroyIcon` in a `finally`, and every COM wrapper `Marshal.FinalReleaseComObject`. Construct the wrapper *before* the `try`, so a failure to create it cannot leave the `finally` with a half-built object.
- **Atomic writes.** Persistence writes `target.tmp` and then moves it over the target. Any new persistence code does the same.
- **Freeze bitmaps in the producer.** Call `Freeze()` before the `await` that returns to the UI thread. An unfrozen `BitmapSource` crossing threads carries the wrong dispatcher affinity and the binding throws.
- **A `Window` accepts only an identity `RenderTransform`.** Declaring a scale transform on a `Window`, even in XAML, throws during `InitializeComponent`. Animate a child element instead.
- **`Window.Loaded` fires inside `Show()`.** Subscribe before calling `Show()`, or the handler never runs.
- **Device pixels at the Win32 boundary.** Taskbar and monitor rectangles, the cursor position and the anchor are all device pixels; conversion to DIPs happens exactly once during placement. Do not pre-convert.
- **Adding a `CancellationTokenSource` field triggers CA1001.** Make the owner `sealed … IDisposable` and cancel-and-dispose in `Dispose`.
- **Group ids are validated** against `^[A-Za-z0-9._-]{1,96}$`. Route anything path-derived through the path provider so the validation cannot be bypassed.
- **A group's id comes from its file name.** The `id` field inside the JSON is ignored on load; disk layout is the source of truth.
- **Launcher failures must reach the file log.** `Trace`-only diagnostics are forbidden in the Launcher. Every early exit and every catch that ends startup goes through the startup failure logger. A regression once exited with code 3 on every click for weeks while the log stayed empty.

## Debugging

Logs are in `%APPDATA%\TaskbarFolders\logs\`, one file per day per process kind, at `Information` level — `LogDebug` calls do not reach disk with the default configuration.

Launcher exit codes are the fastest triage signal:

| Mode | Code | Meaning |
|---|---|---|
| Popup | 1 | No group id resolvable |
| Popup | 3 | Startup threw |
| Popup | 4 | Unhandled dispatcher exception |
| Pin | 0 / 1 / 2 / 3 | Pinned / declined / unavailable / failed |

The popup can be smoke-tested without clicking a tile: start the launcher with `--group-id <id>` as a detached process and check whether it is still alive after a moment. A process that exited immediately with code 3 failed during startup, and the reason will be in `launcher-*.log`.

Common starting points:

- **Nothing generated after adding an app** — the launcher binary was not found. The resolver logs every path it probed at error level.
- **Popup in the wrong place** — placement is DPI-sensitive; check that no value is converted twice.
- **Build fails on a warning** — that is `TreatWarningsAsErrors`; fix it rather than suppressing it.

More symptom-driven detail in [troubleshooting.md](troubleshooting.md).

## Adding a feature

1. Branch from `develop`: `git checkout -b feature/my-feature develop`.
2. Implement it, following MVVM and constructor injection.
3. Add tests. If it touches the DI graph, extend `CompositionRootTests`.
4. Run `dotnet format`, then `dotnet build -c Release`, then `dotnet test -c Release`.
5. Update the documentation this change affects, and add a `CHANGELOG.md` entry under `[Unreleased]`.
6. Open a pull request against `develop`.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for commit conventions and [release-process.md](release-process.md) for what happens at release time.
