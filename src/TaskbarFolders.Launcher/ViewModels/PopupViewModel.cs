using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.ViewModels;

/// <summary>
/// Backing view model for <see cref="Views.PopupWindow"/>. Two-phase load:
/// <see cref="LoadAsync"/> populates names + grid metadata fast (no icons), then
/// <see cref="StartIconLoad"/> extracts and assigns icons in parallel after the
/// window has been shown so the popup appears instantly rather than waiting for
/// per-app shell icon extraction.
/// </summary>
public sealed partial class PopupViewModel : ObservableObject, IDisposable
{
    /// <summary>Pixel size requested from the icon extractor for tiles.</summary>
    public const int IconSize = 64;

    private readonly IGroupConfigStore _store;
    private readonly IIconExtractor _extractor;
    private readonly IIconCache _cache;
    private readonly IProcessLauncher _launcher;
    private readonly LauncherOptions _options;
    private readonly ILogger<PopupViewModel>? _logger;

    private readonly object _iconLoadLock = new();
    private CancellationTokenSource? _iconLoadCts;

    /// <summary>Initializes a new instance.</summary>
    public PopupViewModel(
        IGroupConfigStore store,
        IIconExtractor extractor,
        IIconCache cache,
        IProcessLauncher launcher,
        LauncherOptions options,
        ILogger<PopupViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _extractor = extractor;
        _cache = cache;
        _launcher = launcher;
        _options = options;
        _logger = logger;
    }

    /// <summary>Display name of the loaded group; empty string until <see cref="LoadAsync"/> completes.</summary>
    [ObservableProperty]
    private string _groupName = string.Empty;

    /// <summary>Number of grid columns for the popup; mirrors <see cref="GroupConfig.Columns"/>.</summary>
    [ObservableProperty]
    private int _columns = 3;

    /// <summary>True when the loaded group does not exist or is empty — used by the UI to show an inline error.</summary>
    [ObservableProperty]
    private bool _isUnavailable;

    /// <summary>Transient error message after a failed launch; <see langword="null"/> when no error is current.</summary>
    [ObservableProperty]
    private string? _lastError;

    /// <summary>App entries to render. Empty until <see cref="LoadAsync"/> populates it.</summary>
    public ObservableCollection<PopupAppViewModel> Apps { get; } = [];

    /// <summary>Raised after a successful launch so the window can dismiss itself.</summary>
    public event EventHandler? LaunchSucceeded;

    /// <summary>Raised when the user asks to dismiss the popup, e.g. with its close button.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Reads the group config and populates <see cref="Apps"/> with icon-less view models.
    /// Returns fast (~5 ms) so the window can be shown without waiting for shell icon
    /// extraction. Call <see cref="StartIconLoad"/> after <c>Window.Show</c> to stream
    /// icons in.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var config = await _store.LoadAsync(_options.GroupId, cancellationToken).ConfigureAwait(true);
        if (config is null)
        {
            _logger?.LogError("Launcher started for unknown group {GroupId}.", _options.GroupId);
            IsUnavailable = true;
            GroupName = "(Unknown group)";
            return;
        }

        GroupName = config.GroupName;
        Columns = config.Columns;

        Apps.Clear();
        foreach (var entry in config.Apps)
        {
            Apps.Add(new PopupAppViewModel(entry));
        }

        if (Apps.Count == 0)
        {
            IsUnavailable = true;
            _logger?.LogWarning("Group {GroupId} is empty.", _options.GroupId);
        }
    }

    /// <summary>
    /// Starts a fire-and-forget per-app icon extraction. Each task checks the cache, falls
    /// back to <see cref="IIconExtractor.ExtractIcon"/> on a thread-pool thread, freezes the
    /// resulting <see cref="BitmapSource"/> so it can cross threads, then assigns it to the
    /// owning <see cref="PopupAppViewModel"/>. Replaces an earlier sync per-app extraction
    /// that blocked the UI thread for 200 ms–3 s before the popup could paint.
    /// </summary>
    public void StartIconLoad()
    {
        CancellationToken token;
        lock (_iconLoadLock)
        {
            // Atomically retire any previous load (e.g. caller invoking Start twice) so
            // its tasks observe cancellation and do not race with the new batch.
            // Ordering invariant: Cancel BEFORE Dispose; new CTS BEFORE the token capture.
            // Per-app tasks from the previous batch hold a captured token struct pointing
            // at the now-disposed old CTS — IsCancellationRequested still returns true
            // (the Cancel call set the flag before Dispose), so they observe cancellation
            // correctly even after the swap.
            _iconLoadCts?.Cancel();
            _iconLoadCts?.Dispose();
            _iconLoadCts = new CancellationTokenSource();
            token = _iconLoadCts.Token;
        }

        foreach (var app in Apps)
        {
            _ = LoadIconForAsync(app, token);
        }
    }

    /// <summary>
    /// Cancels any in-flight icon-load tasks. Called by the window on Close so post-close
    /// task completions cannot mutate a disposed view model.
    /// </summary>
    public void CancelIconLoad()
    {
        lock (_iconLoadLock)
        {
            _iconLoadCts?.Cancel();
            _iconLoadCts?.Dispose();
            _iconLoadCts = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => CancelIconLoad();

    [RelayCommand]
    private void LaunchApp(PopupAppViewModel? app)
    {
        // Always clear the previous error so a successful click hides the banner;
        // a failed click re-sets it below.
        LastError = null;

        if (app is null)
        {
            return;
        }

        var ok = _launcher.Launch(app.Path, app.Arguments);
        if (ok)
        {
            _logger?.LogInformation("Launched {Path} from group {GroupId}.", app.Path, _options.GroupId);
            LaunchSucceeded?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _logger?.LogWarning("Failed to launch {Path}; popup stays open.", app.Path);
            LastError = $"Could not launch \"{app.Name}\".";
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task LoadIconForAsync(PopupAppViewModel app, CancellationToken token)
    {
        try
        {
            if (_cache.TryGet(app.Path, IconSize, out var cached))
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                app.Icon = cached;
                return;
            }

            // Extract + freeze on the thread-pool thread so the BitmapSource can safely cross
            // threads when assignment marshals back to the UI thread via ConfigureAwait(true).
            var icon = await Task.Run(
                () =>
                {
                    var bitmap = _extractor.ExtractIcon(app.Path, IconSize);
                    if (bitmap is not null && bitmap.CanFreeze && !bitmap.IsFrozen)
                    {
                        bitmap.Freeze();
                    }
                    return bitmap;
                },
                token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (icon is not null)
            {
                _cache.Set(app.Path, IconSize, icon);
                app.Icon = icon;
            }
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-extraction — drop silently.
        }
        catch (Exception ex)
        {
            // A single bad path must not crash the launcher process; the tile shows the
            // empty placeholder. Log so support can correlate with shell errors.
            _logger?.LogWarning(ex, "Failed to extract icon for {Path}.", app.Path);
        }
    }
}
