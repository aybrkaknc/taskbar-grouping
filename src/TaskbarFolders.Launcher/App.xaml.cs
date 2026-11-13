using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Hosting;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Hosting;
using TaskbarFolders.Shared.Logging;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher;

/// <summary>
/// Application entry point for the TaskbarFolders Launcher.
/// </summary>
/// <remarks>
/// Two modes:
/// <list type="bullet">
///   <item><b>Popup mode</b> (default) — user clicked a pinned tile; show the apps grid.</item>
///   <item><b>Pin mode</b> (<c>--pin-mode</c>) — Manager asked us to pin the group to the
///   taskbar via <see cref="Windows.UI.Shell.TaskbarManager"/>. Shows the system permission
///   dialog and exits with a status code the Manager interprets.</item>
/// </list>
/// Exit codes (popup/startup paths): <c>1</c> = no group id resolvable, <c>3</c> = startup
/// threw, <c>4</c> = unhandled dispatcher exception after startup. Pin mode reports its own
/// runner outcome codes via <see cref="TaskbarPinRunner"/> (the Manager maps those; only a
/// pin-mode process that dies before the runner reuses 1/3/4 with the meanings above).
/// Every failure path writes to the launcher file log via
/// <see cref="StartupFailureLogger"/> — Trace-only diagnostics are invisible in published
/// builds and are forbidden here.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "An Application is not disposed; everything it owns is released in OnExit.")]
public partial class App : Application
{
    private ServiceProvider? _services;
    private PopupPresence? _presence;
    private TileToggle? _tileToggle;

    // Set only while this process is the resident launcher.
    private HostElection? _election;
    private PopupHost? _host;
    private HostPipeServer? _pipe;
    private TrayIcon? _tray;
    private IdleTrimmer? _trimmer;

    // Runs before Main creates the App, so the warm-up starts as early as it can.
    static App() => StartRenderWarmup();

    /// <summary>
    /// Gets the application service provider once <see cref="OnStartup"/> has completed.
    /// </summary>
    public IServiceProvider? Services => _services;

    /// <inheritdoc/>
    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Last-chance handlers so no failure can end the process without a log line.
        // AppDomain.UnhandledException covers non-dispatcher threads (process dies after
        // the handler); DispatcherUnhandledException covers UI-thread throws outside the
        // OnStartup try below (e.g. deferred Storyboard.Begin callbacks).
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            StartupFailureLogger.Log(
                "Unhandled exception; process is terminating.",
                args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (_host is not null)
            {
                // The resident launcher has to outlive one popup's failure: the next click would
                // otherwise find no host. If it is beyond saving, the next click starts a new one.
                StartupFailureLogger.Log("Unhandled dispatcher exception; the resident launcher keeps running.", args.Exception);
                return;
            }

            StartupFailureLogger.Log(
                "Unhandled dispatcher exception; shutting down with exit code 4.",
                args.Exception);
            Shutdown(4);
        };

        var (groupId, fromAumid) = ResolveGroupId(e.Args);
        if (groupId is null)
        {
            StartupFailureLogger.Log(
                $"Launcher started without {CommandLineParser.GroupIdArg} argument and no AUMID fallback available; shutting down with exit code 1.");
            Shutdown(1);
            return;
        }

        // Any unhandled exception inside the async branches would otherwise crash the
        // process via async-void with an unobservable exit code; the Manager would map
        // the resulting random exit code to PinResult.Error. Wrap so we always shutdown
        // with a documented exit code.
        try
        {
            if (CommandLineParser.HasPinMode(e.Args))
            {
                await RunPinModeAsync(groupId, fromAumid).ConfigureAwait(true);
                return;
            }

            await RunPopupModeAsync(e, groupId, fromAumid).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StartupFailureLogger.Log("Launcher OnStartup threw; shutting down with exit code 3.", ex);
            Shutdown(3);
        }
    }

    /// <summary>
    /// Resolves the target group id from either the explicit <c>--group-id</c> argument
    /// (Manager-spawned + .lnk-pinned paths) or the AUMID Windows already assigned to the
    /// process (TaskbarManager-pinned tile paths where the original command line is not
    /// preserved). Returns a tuple of the id and whether it was recovered from the AUMID;
    /// callers use the latter to decide whether to re-stamp the AUMID (re-stamping an
    /// inherited AUMID can cause identity drift if Windows normalised the string).
    /// </summary>
    private static (string? GroupId, bool FromAumid) ResolveGroupId(string[] args)
    {
        var fromArgs = CommandLineParser.TryParseGroupId(args);
        if (fromArgs is not null)
        {
            return (fromArgs, false);
        }

        var assignedAumid = Interop.NativeMethods.TryGetCurrentProcessAumid();
        if (assignedAumid is not null && GroupAumid.TryExtractGroupId(assignedAumid, out var fromAumid))
        {
            return (fromAumid, true);
        }

        return (null, false);
    }

    /// <summary>
    /// Pin-mode entry point. Stamps AUMID, builds a minimal DI scope, shows the off-screen
    /// host window so the WinRT pin dialog has a foreground parent, awaits the pin runner,
    /// shuts down with the runner's exit code so the Manager can react to the outcome.
    /// </summary>
    private async Task RunPinModeAsync(string groupId, bool aumidAlreadyInherited)
    {
        // The exit code IS this mode's result, so no window may end the process. Under the
        // default OnLastWindowClose, closing the host window below started a shutdown with
        // code 0, and the Shutdown(exitCode) that followed was ignored: the Manager read
        // every outcome — unavailable, declined, failed, not verified — as "Pinned".
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // AUMID must match GroupAumid.For(groupId) before the WinRT call so
        // RequestPinCurrentAppAsync pins the right identity. Skip the stamp when Windows
        // already gave us the AUMID via process activation — re-stamping the inherited
        // string could drift the identity if Windows normalised the case or trimmed
        // whitespace differently from our For() formatter.
        var aumid = GroupAumid.For(groupId);
        if (!aumidAlreadyInherited)
        {
            var stampHr = Interop.NativeMethods.SetCurrentProcessExplicitAppUserModelID(aumid);
            if (stampHr != 0)
            {
                StartupFailureLogger.Log(
                    $"SetCurrentProcessExplicitAppUserModelID('{aumid}') failed with HRESULT 0x{stampHr:X8}; " +
                    "the pin would target the wrong identity.");
            }
        }

        var paths = new AppDataPathProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddTaskbarFoldersFileLogging(paths.LogsDirectory, "launcher"));
        services.AddTaskbarFoldersLauncher(new LauncherOptions(groupId), paths);
        // PopupWindow is registered in the same graph but never resolved in pin-mode;
        // AppSettings is required by the DI graph regardless, so register a default.
        services.AddSingleton(new AppSettings());

        _services = services.BuildServiceProvider();

        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Launcher pin-mode starting for group {GroupId}.", groupId);

        var host = _services.GetRequiredService<Views.PinHostWindow>();
        MainWindow = host;
        host.Show();
        host.Activate();

        var runner = _services.GetRequiredService<TaskbarPinRunner>();
        var exitCode = await runner.RunAsync(host, aumid).ConfigureAwait(true);

        logger.LogInformation("Pin runner exit code {ExitCode} for group {GroupId}.", exitCode, groupId);

        host.Close();
        Shutdown(exitCode);
    }

    /// <summary>
    /// Popup-mode entry point. Existing v0.3 startup path, factored out to keep
    /// <see cref="OnStartup"/> a thin dispatcher.
    /// </summary>
    private async Task RunPopupModeAsync(StartupEventArgs e, string groupId, bool aumidAlreadyInherited)
    {
        // Per-checkpoint Stopwatch timestamps so the launcher-*.log shows where the user's
        // perceived popup-open time goes. Two lines come out of it: "Startup timing" from
        // Loaded and "First render" from ContentRendered. GetTimestamp() costs ~10 ns per
        // call; negligible against the 100s of ms we are profiling.
        var tStart = Stopwatch.GetTimestamp();

        // Per-monitor DPI awareness must be set BEFORE the cursor capture below, not just
        // before HWND creation: the launcher ships without an app manifest, so the process
        // starts DPI-unaware and GetCursorPos would return virtualized 96-DPI coordinates
        // instead of the device pixels the ICursorAnchor contract (and the px→DIP
        // conversion in TaskbarPositionHelper) requires. The call costs microseconds, so
        // capturing the cursor immediately afterwards keeps the click location fresh.
        try
        {
            Interop.NativeMethods.SetProcessDpiAwarenessContext(
                Interop.NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        }
        catch (System.EntryPointNotFoundException)
        {
            // Pre-1703 Windows — leave DPI awareness at the manifest default.
        }

        // Capture the cursor position before anything that can take 100+ ms. This is
        // the click location — by the time WPF has bootstrapped (~300–500 ms) the cursor has
        // typically drifted, which is why the v0.2 helper's late GetCursorPos call produced
        // visually-random popup placement.
        //
        // GetCursorPos can fail on restricted desktops / session 0. Out param is default-
        // initialised to (0, 0) on failure, which would silently anchor the popup at the
        // top-left of the primary monitor. Detect the failure and fall back to the screen
        // centre so the user sees the popup near the middle instead of jammed in a corner.
        // The anchor contract is device pixels (ICursorAnchor); SystemParameters reports
        // DIPs, so the fallback multiplies by the system DPI scale to stay in-contract.
        System.Windows.Point anchor;
        if (Interop.NativeMethods.GetCursorPos(out var clickPoint))
        {
            anchor = new System.Windows.Point(clickPoint.X, clickPoint.Y);
        }
        else
        {
            var systemScale = Interop.NativeMethods.GetDpiForSystem() / 96.0;
            anchor = new System.Windows.Point(
                System.Windows.SystemParameters.PrimaryScreenWidth / 2 * systemScale,
                System.Windows.SystemParameters.PrimaryScreenHeight / 2 * systemScale);
        }

        // Stamp the process AUMID BEFORE any window is created so Windows matches the
        // popup to the pinned tile (which carries the same AUMID via PKEY_AppUserModel_ID).
        // Skip when Windows already gave us the AUMID via process activation — see
        // RunPinModeAsync for the identity-drift rationale.
        if (!aumidAlreadyInherited)
        {
            _ = Interop.NativeMethods.SetCurrentProcessExplicitAppUserModelID(GroupAumid.For(groupId));
        }
        var tAumid = Stopwatch.GetTimestamp();

        var paths = new AppDataPathProvider();

        // Load settings BEFORE the DI container is built so the resolved AppSettings instance
        // can be registered as a singleton (v0.3 single-load pattern).
        var settings = await new JsonAppSettingsStore(paths).LoadAsync().ConfigureAwait(true);
        var tSettings = Stopwatch.GetTimestamp();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddTaskbarFoldersFileLogging(paths.LogsDirectory, "launcher"));
        services.AddTaskbarFoldersLauncher(new LauncherOptions(groupId), paths);
        services.AddSingleton(settings);
#if DEBUG
        _services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
#else
        _services = services.BuildServiceProvider();
#endif
        var tDi = Stopwatch.GetTimestamp();

        // Seed the cursor anchor BEFORE PopupViewModel/PopupWindow are resolved so any
        // placement lookup during the first paint sees a populated value.
        _services.GetRequiredService<ICursorAnchor>().Seed(anchor);

        var logger = _services.GetRequiredService<ILogger<App>>();
        var perf = _services.GetRequiredService<PerfRecorder>();
        logger.LogInformation("Launcher starting for group {GroupId}.", groupId);

        // Files after the group id come from Explorer's "Taskbar Groups" menu.
        var filesToAdd = CommandLineParser.ParseFilesToAdd(e.Args);

        // The first launcher to start becomes the resident one and serves every later click; the
        // rest were already handed over in Program.Main. A launcher that finds a host it could not
        // reach (hung, or still starting) shows its popup itself, as a launcher that is not resident.
        if (settings.LauncherResident)
        {
            _election = new HostElection();
            if (_election.TryBecomeHost())
            {
                await RunResidentAsync(e, groupId, anchor, filesToAdd, logger, perf, tStart).ConfigureAwait(true);
                return;
            }

            _election.Dispose();
            _election = null;
            logger.LogWarning("A resident launcher exists but did not take this click; showing the popup from this process.");
        }

        // This click on the tile is the one that just closed the group's popup: a toggle.
        if (filesToAdd.Count == 0 && TileToggle.TryConsume(groupId))
        {
            logger.LogInformation("Popup for {GroupId} was closed by this tile click; not reopening.", groupId);
            Shutdown(0);
            return;
        }

        // Claimed before anything is added, so of several launchers started together for one
        // group (a multi-selection sent from Explorer) exactly one shows the popup.
        _presence = PopupPresence.Claim(groupId);

        // Add the files first, so the popup opens already showing them.
        if (filesToAdd.Count > 0)
        {
            await _services.GetRequiredService<CommandLineAppAdder>().AddAsync(filesToAdd).ConfigureAwait(true);
            if (!_presence.IsFirst)
            {
                logger.LogInformation("A popup for {GroupId} is already open; asked it to reload.", groupId);
                _presence.SignalReload();
                Shutdown(0);
                return;
            }
        }

        // Apply the persisted theme before the window is built so DynamicResource bindings
        // paint correctly on the first frame.
        LauncherThemeApplier.Apply(this, settings.Theme);
        var tTheme = Stopwatch.GetTimestamp();

        // Two-phase load: metadata first (group name, columns, app names — ~5 ms) so the
        // window can paint immediately, then per-app icon extraction in the background.
        var viewModel = _services.GetRequiredService<PopupViewModel>();
        await viewModel.LoadAsync().ConfigureAwait(true);
        var tVm = Stopwatch.GetTimestamp();

        // Icon extraction needs the tile metrics and the app list, both set by LoadAsync, but
        // not the window: it runs on the thread pool while the UI thread builds, places and
        // shows the window, so the icons are ready sooner after the first frame. Results are
        // applied through the dispatcher and bound when the window appears.
        viewModel.StartIconLoad();
        var tIcons = Stopwatch.GetTimestamp();

        var popup = _services.GetRequiredService<Views.PopupWindow>();
        var tWindow = Stopwatch.GetTimestamp();
        MainWindow = popup;

        // Timing summary, emitted from the Loaded handler. `processAge` is the wall-clock
        // from Process.StartTime to the moment the handler runs — not to tStart. What
        // elapsed before RunPopupModeAsync was entered (host start-up, runtime init, WPF
        // assembly load) is therefore processAge minus the last checkpoint; the
        // ContentRendered line below reports it explicitly as `runtime`. Loaded is not a
        // paint: layout is done, nothing has been rendered yet.
        //
        // Subscribe BEFORE Show(): a Window raises Loaded synchronously inside Show(), so
        // a handler attached afterwards never runs (v0.4.1/v0.4.2 emitted no timing line
        // at all because of this). tShown is still captured after Show() returns; if
        // Loaded ran inside Show() the checkpoint falls back to tLoaded to stay monotonic.
        var processStart = Process.GetCurrentProcess().StartTime;
        long tPlaced = 0;
        long tShown = 0;
        long tActivated = 0;

        // Attached after the constructor, so it runs after the window's own
        // SourceInitialized handler: the HWND exists and size + placement are computed.
        popup.SourceInitialized += (_, _) => tPlaced = Stopwatch.GetTimestamp();

        popup.Loaded += (_, _) =>
        {
            var tLoaded = Stopwatch.GetTimestamp();
            var processAge = (DateTime.Now - processStart).TotalMilliseconds;
            logger.LogInformation(
                "Startup timing (ms from tStart, processAge={ProcAge:F0}): aumid={Aumid:F0} settings={Settings:F0} di={Di:F0} theme={Theme:F0} vm={Vm:F0} window={Window:F0} placed={Placed:F0} show={Show:F0} loaded={Loaded:F0}",
                processAge,
                ToMs(tStart, tAumid),
                ToMs(tStart, tSettings),
                ToMs(tStart, tDi),
                ToMs(tStart, tTheme),
                ToMs(tStart, tVm),
                ToMs(tStart, tWindow),
                ToMs(tStart, tPlaced != 0 ? tPlaced : tLoaded),
                ToMs(tStart, tShown != 0 ? tShown : tLoaded),
                ToMs(tStart, tLoaded));

            // The same checkpoints, as one machine-readable record in perf-launcher-<day>.jsonl.
            // ms_since_process_start on the record is the time at which this handler ran.
            perf.Record(
                "popup.loaded",
                ("group", groupId),
                ("apps", viewModel.Apps.Count),
                ("process_age_ms", processAge),
                ("runtime_ms", processAge - ToMs(tStart, tLoaded)),
                ("aumid_ms", ToMs(tStart, tAumid)),
                ("settings_ms", ToMs(tStart, tSettings)),
                ("di_ms", ToMs(tStart, tDi)),
                ("theme_ms", ToMs(tStart, tTheme)),
                ("vm_ms", ToMs(tStart, tVm)),
                ("window_ms", ToMs(tStart, tWindow)),
                ("placed_ms", ToMs(tStart, tPlaced != 0 ? tPlaced : tLoaded)),
                ("loaded_ms", ToMs(tStart, tLoaded)));
        };

        // Second line, kept separate so the Loaded line survives if this one never comes.
        // ContentRendered is queued at Input priority and so runs once the UI thread's
        // render queue has drained: it marks the first frame being handed to the render
        // thread, not its arrival on screen. The checkpoints between Show() and here are
        // the synchronous work that stands between Loaded and that first frame.
        popup.ContentRendered += (_, _) =>
        {
            var rendered = ToMs(tStart, Stopwatch.GetTimestamp());
            var processAge = (DateTime.Now - processStart).TotalMilliseconds;
            logger.LogInformation(
                "First render (ms from tStart, processAge={ProcAge:F0}, runtime={Runtime:F0}): shown={Shown:F0} activated={Activated:F0} icons={Icons:F0} rendered={Rendered:F0}",
                processAge,
                processAge - rendered,
                ToMs(tStart, tShown),
                ToMs(tStart, tActivated),
                ToMs(tStart, tIcons),
                rendered);

            // The window's own marks (XAML load, HWND, DWM, layout, placement, Win32 messages),
            // in the order they were taken, measured from the same origin as the fields above.
            var fields = new List<(string Key, object? Value)>
            {
                ("group", groupId),
                ("process_age_ms", processAge),
                ("runtime_ms", processAge - rendered),
                ("shown_ms", ToMs(tStart, tShown)),
                ("activated_ms", ToMs(tStart, tActivated)),
                ("icons_started_ms", ToMs(tStart, tIcons)),
                ("rendered_ms", rendered),
            };
            fields.AddRange(popup.Marks.ToFields(tStart));
            perf.Record("popup.first_render", fields.ToArray());
        };

        // A press on the tile that opened the popup is also the click that starts the next
        // launcher; leave it the marker that tells it to stay hidden.
        popup.OutsidePress += (_, press) =>
        {
            if (TileToggle.IsOnOpeningTile(anchor, (int)press.Position.X, (int)press.Position.Y, VisualTreeHelper.GetDpi(popup).DpiScaleX))
            {
                press.IsOnOpeningTile = true;
                _tileToggle ??= TileToggle.MarkDismissed(groupId);
            }
        };

        popup.Marks.Mark("show_call");
        popup.Show();
        popup.Marks.Mark("show_returned");
        tShown = Stopwatch.GetTimestamp();

        // Explicitly request activation (parity with pin mode). Show() alone does not
        // reliably take foreground when Explorer still owns it from the tile click, and
        // dismiss-on-focus-loss depends on the popup having been activated once.
        popup.Activate();
        tActivated = Stopwatch.GetTimestamp();

        // Later launchers that add files to this group ask this popup to show them.
        _presence.ListenForReload(() => Dispatcher.BeginInvoke(new Func<Task>(viewModel.ReloadAsync)));

        // Deferred startup IO: prune stale icon-cache PNGs and old log files in the background
        // so the first paint of the popup is not blocked by ~10-70 ms of enumerate-and-delete.
        _services.GetRequiredService<IIconCache>().StartBackgroundPrune();
        _services.GetServices<ILoggerProvider>()
            .OfType<FileLoggerProvider>()
            .FirstOrDefault()?.StartBackgroundPrune();

        base.OnStartup(e);
    }

    /// <summary>
    /// Runs this process as the resident launcher: it stays alive after the popup closes, listens
    /// for the requests of later clicks, and shows an icon in the notification area. The click that
    /// started it is its first request.
    /// </summary>
    private async Task RunResidentAsync(
        StartupEventArgs e,
        string groupId,
        System.Windows.Point anchor,
        IReadOnlyList<string> files,
        ILogger logger,
        PerfRecorder perf,
        long tStart)
    {
        // Closing the last popup must not end the process; only the tray menu does.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = _services!;
        _host = new PopupHost(
            this,
            services.GetRequiredService<PopupSessionFactory>(),
            services.GetRequiredService<IAppSettingsStore>(),
            new HostRouter(),
            perf,
            services.GetRequiredService<ILogger<PopupHost>>());

        // Listening starts before the first popup is built, so a second click that arrives while
        // it opens already finds the pipe.
        var host = _host;
        _pipe = new HostPipeServer(HostEndpoint.PipeName, request => host.HandleAsync(request), logger);
        _pipe.Start();

        _tray = new TrayIcon("Taskbar Folders", OpenManager, () => Shutdown(0));

        // While idle the launcher hands its memory back; the next popup takes it again in no time.
        _trimmer = new IdleTrimmer(() => !host.HasOpenPopups, TimeSpan.FromSeconds(5), logger, perf);
        host.AllPopupsClosed += (_, _) => _trimmer.Schedule();
        logger.LogInformation("Launcher is resident (pipe {Pipe}).", HostEndpoint.PipeName);

        await host.HandleAsync(new HostRequest(groupId, (int)anchor.X, (int)anchor.Y, files), tStart).ConfigureAwait(true);

        // Deferred startup IO: prune stale icon-cache PNGs and old log files in the background.
        services.GetRequiredService<IIconCache>().StartBackgroundPrune();
        services.GetServices<ILoggerProvider>()
            .OfType<FileLoggerProvider>()
            .FirstOrDefault()?.StartBackgroundPrune();

        base.OnStartup(e);
    }

    // The Manager sits next to the launcher's folder in an installed layout.
    private static void OpenManager()
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath);
        if (directory is null)
        {
            return;
        }

        var manager = Path.GetFullPath(Path.Combine(directory, "..", "Manager", "TaskbarFolders.Manager.exe"));
        if (!File.Exists(manager))
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(manager) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            StartupFailureLogger.Log($"Could not start the Manager at '{manager}'.", ex);
        }
    }

    // WPF connects to the desktop compositor the first time any visual property changes, and
    // waits for the native render thread to answer: ~80 ms of a cold start (a CPU profile of
    // the popup's open, under PopupWindow's InitializeComponent). That start-up is per process,
    // not per window, so a throwaway element on a helper thread with its own dispatcher can pay
    // it while this thread builds the Application, the services and the view model.
    // Best effort: if anything goes wrong the real window simply pays the cost itself.
    private static void StartRenderWarmup()
    {
        var thread = new Thread(() =>
        {
            try
            {
                _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                var probe = new System.Windows.Controls.Border { Opacity = 0.5 };

                // Stays alive until the process ends: shutting the dispatcher down tears the
                // channel down again, and that teardown can hold up the real window's connect.
                GC.KeepAlive(probe);
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception)
            {
                // Warm-up only; the window works the same without it.
            }
        })
        {
            Name = "WpfRenderWarmup",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static double ToMs(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_tileToggle is not null)
        {
            // The window is gone; the process stays only until the tile click's launcher has
            // seen the marker, which also ends the wait early.
            _tileToggle.WaitForNextLaunch(TileToggle.Window);
            _tileToggle.Dispose();
            _tileToggle = null;
        }

        _trimmer?.Cancel();
        _trimmer = null;
        _pipe?.Dispose();
        _pipe = null;
        _tray?.Dispose();
        _tray = null;
        _election?.Dispose();
        _election = null;
        _host?.Dispose();
        _host = null;

        _presence?.Dispose();
        _presence = null;
        _services?.Dispose();
        _services = null;

        base.OnExit(e);
    }
}
