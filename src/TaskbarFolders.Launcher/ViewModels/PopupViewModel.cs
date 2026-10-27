using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
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
    private readonly IGroupSyncService _syncService;
    private readonly LauncherOptions _options;
    private readonly ILogger<PopupViewModel>? _logger;

    private readonly object _iconLoadLock = new();
    private CancellationTokenSource? _iconLoadCts;

    // The group as loaded, kept so edits made in the popup can be saved back. Null when the
    // group does not exist, in which case nothing can be edited.
    private GroupConfig? _config;

    /// <summary>Initializes a new instance.</summary>
    public PopupViewModel(
        IGroupConfigStore store,
        IIconExtractor extractor,
        IIconCache cache,
        IProcessLauncher launcher,
        IGroupSyncService syncService,
        LauncherOptions options,
        ILogger<PopupViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _extractor = extractor;
        _cache = cache;
        _launcher = launcher;
        _syncService = syncService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// True once an existing group has been loaded; the popup only offers to edit a group it
    /// could read.
    /// </summary>
    [ObservableProperty]
    private bool _canEditGroup;

    /// <summary>
    /// Edit mode: each tile shows a remove badge, and a tile click no longer launches the app.
    /// </summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>
    /// Pinned open: clicks elsewhere, losing the focus and launching an app no longer close the
    /// popup. Close, Escape and a click on the group's own tile still do. Lasts for this popup
    /// only; the next one opens unpinned.
    /// </summary>
    [ObservableProperty]
    private bool _isPinned;

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

        _config = config;
        CanEditGroup = true;
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

    /// <summary>
    /// Re-reads the group after another launcher changed it — a file sent from Explorer while
    /// this popup is open — and rebuilds the tiles from the stored config. Icons come back from
    /// the icon cache, so the rebuild is quick.
    /// </summary>
    public async Task ReloadAsync()
    {
        var config = await _store.LoadAsync(_options.GroupId).ConfigureAwait(true);
        if (config is null)
        {
            return;
        }

        _config = config;
        GroupName = config.GroupName;
        Columns = config.Columns;

        Apps.Clear();
        foreach (var entry in config.Apps)
        {
            Apps.Add(new PopupAppViewModel(entry));
        }

        IsUnavailable = Apps.Count == 0;
        if (IsUnavailable)
        {
            IsEditing = false;
        }

        StartIconLoad();
        _logger?.LogInformation("Reloaded group {GroupId}: {Count} app(s).", _options.GroupId, Apps.Count);
    }

    /// <summary>
    /// Adds the picked files to the group: anything <see cref="AppEntry.TryCreateFromPath"/>
    /// accepts becomes a tile at the end of the grid, then the group is saved and re-synced so
    /// the taskbar icon and shortcuts follow. Returns how many apps were added.
    /// </summary>
    /// <param name="paths">Files chosen in the picker.</param>
    public async Task<int> AddAppsAsync(IEnumerable<string>? paths)
    {
        if (paths is null || _config is null)
        {
            return 0;
        }

        var added = new List<PopupAppViewModel>();
        foreach (var path in paths)
        {
            if (!AppEntry.TryCreateFromPath(path, out var entry))
            {
                _logger?.LogDebug("Ignored {Path}: not an .exe or .lnk.", path);
                continue;
            }

            _config.Apps.Add(entry);
            var app = new PopupAppViewModel(entry);
            Apps.Add(app);
            added.Add(app);
        }

        if (added.Count == 0)
        {
            return 0;
        }

        IsUnavailable = false;
        LoadIconsFor(added);
        _logger?.LogInformation("Added {Count} app(s) to group {GroupId} from the popup.", added.Count, _options.GroupId);
        await PersistAsync().ConfigureAwait(true);
        return added.Count;
    }

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    [RelayCommand]
    private void ToggleEdit()
    {
        // Nothing to edit in a group that could not be loaded or has no apps left.
        IsEditing = !IsEditing && CanEditGroup && Apps.Count > 0;
    }

    /// <summary>
    /// Removes one app from the group — the entry only; the file it points at is untouched —
    /// then saves and re-syncs. Removing the last app leaves edit mode and shows the empty state.
    /// </summary>
    [RelayCommand]
    private async Task RemoveAppAsync(PopupAppViewModel? app)
    {
        if (app is null || _config is null || !Apps.Contains(app))
        {
            return;
        }

        _config.Apps.Remove(app.Entry);
        Apps.Remove(app);
        if (Apps.Count == 0)
        {
            IsEditing = false;
            IsUnavailable = true;
        }

        _logger?.LogInformation("Removed {Path} from group {GroupId} in the popup.", app.Path, _options.GroupId);
        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Saves the edited group and regenerates its icon and shortcuts. A failure is shown in the
    /// error strip rather than thrown: the popup is a short-lived window with nobody to catch it.
    /// </summary>
    private async Task PersistAsync()
    {
        if (_config is null)
        {
            return;
        }

        try
        {
            await _store.SaveAsync(_config).ConfigureAwait(true);
            await _syncService.SyncAsync(_config).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            _logger?.LogError(ex, "Saving group {GroupId} from the popup failed.", _options.GroupId);
            LastError = "Could not save the change to this group.";
        }
    }

    /// <summary>Loads icons for tiles added after the initial batch, without cancelling it.</summary>
    private void LoadIconsFor(IEnumerable<PopupAppViewModel> apps)
    {
        CancellationToken token;
        lock (_iconLoadLock)
        {
            _iconLoadCts ??= new CancellationTokenSource();
            token = _iconLoadCts.Token;
        }

        foreach (var app in apps)
        {
            _ = LoadIconForAsync(app, token);
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

        // In edit mode the tiles are being arranged, not used; a stray click must not start
        // an app the user is about to remove.
        if (app is null || IsEditing)
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
