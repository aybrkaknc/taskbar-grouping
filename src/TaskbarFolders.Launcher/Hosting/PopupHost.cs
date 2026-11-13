using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Hosting;

namespace TaskbarFolders.Launcher.Hosting;

/// <summary>
/// The resident launcher's popup logic: turns each <see cref="HostRequest"/> into a popup shown, a
/// popup closed, a popup reloaded or nothing, and keeps track of which groups have a popup open.
/// Requests are handled one at a time on the UI thread, so the several requests Explorer sends for
/// a multi-selection never race each other. A new <see cref="PopupSession"/> and window are built
/// for every popup; nothing a popup owns outlives it.
/// </summary>
public sealed class PopupHost : IDisposable
{
    private readonly Application _app;
    private readonly PopupSessionFactory _factory;
    private readonly IAppSettingsStore _settingsStore;
    private readonly HostRouter _router;
    private readonly PerfRecorder _perf;
    private readonly ILogger<PopupHost> _logger;
    private readonly Dictionary<string, PopupSession> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<PopupSession> _closing = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _cold = true;

    /// <summary>Initializes a new instance.</summary>
    public PopupHost(
        Application app,
        PopupSessionFactory factory,
        IAppSettingsStore settingsStore,
        HostRouter router,
        PerfRecorder perf,
        ILogger<PopupHost> logger)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(perf);
        ArgumentNullException.ThrowIfNull(logger);

        _app = app;
        _factory = factory;
        _settingsStore = settingsStore;
        _router = router;
        _perf = perf;
        _logger = logger;
    }

    /// <summary>Raised on the UI thread when the last open popup has closed.</summary>
    public event EventHandler? AllPopupsClosed;

    /// <summary>Whether any popup is open.</summary>
    public bool HasOpenPopups => _open.Count > 0;

    /// <summary>
    /// Handles one request. Safe to call from any thread, and never throws: a request that fails is
    /// logged and dropped, because the host has to stay up for the next click.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="origin">
    /// A <see cref="Stopwatch.GetTimestamp"/> reading to measure the open from; now when omitted.
    /// The first popup passes the launcher's own start so its record matches the old ones.
    /// </param>
    /// <returns>A task that completes when the request has been dealt with.</returns>
    public async Task HandleAsync(HostRequest request, long? origin = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var received = origin ?? Stopwatch.GetTimestamp();
        try
        {
            await _app.Dispatcher.InvokeAsync(() => HandleOnUiAsync(request, received)).Task.Unwrap().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request for group {GroupId} failed.", request.GroupId);
        }
    }

    private async Task HandleOnUiAsync(HostRequest request, long received)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            var groupId = request.GroupId;
            _open.TryGetValue(groupId, out var session);
            var isOpen = session is not null && !_closing.Contains(session);

            switch (_router.Decide(groupId, isOpen, request.Files.Count > 0))
            {
                case HostAction.Ignore:
                    _logger.LogInformation("Request for {GroupId} ignored: this click closed its popup.", groupId);
                    break;

                case HostAction.Close:
                    _logger.LogInformation("Request for {GroupId}: its popup is open; closing it.", groupId);
                    session!.Window!.Close();
                    break;

                case HostAction.Reload:
                    _logger.LogInformation("Request for {GroupId}: adding {Count} file(s) to its open popup.", groupId, request.Files.Count);
                    await session!.Adder.AddAsync(request.Files).ConfigureAwait(true);
                    await session.ViewModel.ReloadAsync().ConfigureAwait(true);
                    break;

                default:
                    await ShowAsync(request, received).ConfigureAwait(true);
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ShowAsync(HostRequest request, long origin)
    {
        var groupId = request.GroupId;
        var cold = _cold;
        _cold = false;

        var settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        var tSettings = Stopwatch.GetTimestamp();

        // Applied before the window is built so DynamicResource bindings paint right on the first
        // frame, and again for every popup so a theme changed in the Manager shows up.
        LauncherThemeApplier.Apply(_app, settings.Theme);
        var tTheme = Stopwatch.GetTimestamp();

        var anchor = new Point(request.X, request.Y);
        var session = _factory.Create(groupId, settings, anchor);
        try
        {
            // Files first, so the popup opens already showing them.
            if (request.Files.Count > 0)
            {
                await session.Adder.AddAsync(request.Files).ConfigureAwait(true);
            }

            await session.ViewModel.LoadAsync().ConfigureAwait(true);
            var tVm = Stopwatch.GetTimestamp();

            // Icon extraction needs the tile metrics and the app list, not the window: it runs on
            // the pool while the UI thread builds and shows the window.
            session.ViewModel.StartIconLoad();
            var tIcons = Stopwatch.GetTimestamp();

            var window = session.CreateWindow();
            var tWindow = Stopwatch.GetTimestamp();

            long tShown = 0;
            long tActivated = 0;

            window.Closing += (_, _) => _closing.Add(session);
            window.Closed += (_, _) => OnClosed(session);

            // A press on the tile that opened the popup is also the click whose request arrives next;
            // remember it, so that request does not reopen what the press just closed.
            window.OutsidePress += (_, press) =>
            {
                if (TileToggle.IsOnOpeningTile(anchor, (int)press.Position.X, (int)press.Position.Y, VisualTreeHelper.GetDpi(window).DpiScaleX))
                {
                    press.IsOnOpeningTile = true;
                    _router.MarkDismissedByOwnTile(groupId);
                }
            };

            window.ContentRendered += (_, _) =>
            {
                var fields = new List<(string Key, object? Value)>
                {
                    ("group", groupId),
                    ("host", true),
                    ("cold", cold),
                    ("apps", session.ViewModel.Apps.Count),
                    ("settings_ms", PerfRecorder.ElapsedMs(origin, tSettings)),
                    ("theme_ms", PerfRecorder.ElapsedMs(origin, tTheme)),
                    ("vm_ms", PerfRecorder.ElapsedMs(origin, tVm)),
                    ("icons_started_ms", PerfRecorder.ElapsedMs(origin, tIcons)),
                    ("window_ms", PerfRecorder.ElapsedMs(origin, tWindow)),
                    ("shown_ms", PerfRecorder.ElapsedMs(origin, tShown)),
                    ("activated_ms", PerfRecorder.ElapsedMs(origin, tActivated)),
                    ("rendered_ms", PerfRecorder.ElapsedMs(origin, Stopwatch.GetTimestamp())),
                };
                if (cold)
                {
                    using var self = Process.GetCurrentProcess();
                    fields.Add(("process_age_ms", (DateTime.Now - self.StartTime).TotalMilliseconds));
                }

                fields.AddRange(window.Marks.ToFields(origin));
                _perf.Record("popup.first_render", fields.ToArray());
            };

            _open[groupId] = session;

            window.Marks.Mark("show_call");
            window.Show();
            window.Marks.Mark("show_returned");
            tShown = Stopwatch.GetTimestamp();

            // Show() alone does not reliably take the foreground, and dismiss-on-focus-loss depends
            // on the popup having been activated once.
            window.Activate();
            tActivated = Stopwatch.GetTimestamp();
        }
        catch
        {
            // The popup never opened; do not leave its session behind. The caller logs the cause.
            if (_open.TryGetValue(groupId, out var current) && ReferenceEquals(current, session))
            {
                _open.Remove(groupId);
            }

            session.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    private void OnClosed(PopupSession session)
    {
        _closing.Remove(session);
        if (_open.TryGetValue(session.GroupId, out var current) && ReferenceEquals(current, session))
        {
            _open.Remove(session.GroupId);
        }

        session.Dispose();
        _logger.LogInformation("Popup for {GroupId} closed.", session.GroupId);

        if (_open.Count == 0)
        {
            AllPopupsClosed?.Invoke(this, EventArgs.Empty);
        }
    }
}
