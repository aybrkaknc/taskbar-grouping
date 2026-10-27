using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// Detail-pane view model for a single selected group. Manages the apps list, debounced
/// composite-icon preview, and add/remove operations. <see cref="Bind"/> swaps the
/// currently edited <see cref="GroupListItemViewModel"/> so the same instance can live
/// across selection changes (avoids allocating a fresh editor per click).
/// </summary>
public sealed partial class GroupEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>Delay before a composite-icon refresh fires after the last apps mutation.</summary>
    public static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>Pixel size of the composite preview rendered for the detail pane.</summary>
    public const int PreviewSize = 256;

    private readonly IIconExtractor _extractor;
    private readonly ICompositeIconGenerator _composer;
    private readonly IIconCache _cache;
    private readonly IGroupConfigStore _store;
    private readonly IGroupSyncService _syncService;
    private readonly IAppDataPathProvider _paths;
    private readonly IUserConfirmation _userConfirmation;
    private readonly IPinToTaskbarService _pinService;
    private readonly ILogger<GroupEditorViewModel>? _logger;

    private GroupListItemViewModel? _boundItem;
    private CancellationTokenSource? _previewCts;
    private readonly object _previewCtsLock = new();
    private int _pendingSaves;
    private bool _disposed;

    /// <summary>Initializes a new instance with the dependencies it needs to render previews and persist edits.</summary>
    public GroupEditorViewModel(
        IIconExtractor extractor,
        ICompositeIconGenerator composer,
        IIconCache cache,
        IGroupConfigStore store,
        IGroupSyncService syncService,
        IAppDataPathProvider paths,
        IUserConfirmation userConfirmation,
        IPinToTaskbarService pinService,
        ILogger<GroupEditorViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(userConfirmation);
        ArgumentNullException.ThrowIfNull(pinService);

        _extractor = extractor;
        _composer = composer;
        _cache = cache;
        _store = store;
        _syncService = syncService;
        _paths = paths;
        _userConfirmation = userConfirmation;
        _pinService = pinService;
        _logger = logger;
    }

    /// <summary>Apps currently bound to the editor — empty when no group is selected.</summary>
    public ObservableCollection<AppEntryViewModel> Apps { get; } = [];

    /// <summary>Currently bound group, or null when nothing is selected.</summary>
    public GroupListItemViewModel? BoundItem
    {
        get => _boundItem;
        private set => SetProperty(ref _boundItem, value);
    }

    /// <summary>Live composite preview, regenerated on <see cref="Apps"/> changes (debounced).</summary>
    [ObservableProperty]
    private BitmapSource? _compositeIconPreview;

    /// <summary>Re-routes the editor to a new group selection.</summary>
    /// <param name="item">The new selection, or <see langword="null"/> to clear.</param>
    public void Bind(GroupListItemViewModel? item)
    {
        if (ReferenceEquals(_boundItem, item))
        {
            return;
        }

        Apps.CollectionChanged -= OnAppsCollectionChanged;
        Apps.Clear();

        BoundItem = item;
        if (item is null)
        {
            CompositeIconPreview = null;
            return;
        }

        // Rebuild Apps inside a try/finally so an exception in LoadIconInto cannot leave
        // the editor in a state where the collection-changed handler is permanently detached.
        try
        {
            foreach (var entry in item.Config.Apps)
            {
                var appVm = new AppEntryViewModel(entry);
                Apps.Add(appVm);
                LoadIconInto(appVm);
            }
        }
        finally
        {
            Apps.CollectionChanged += OnAppsCollectionChanged;
            SchedulePreviewRefresh();
        }
    }

    /// <summary>
    /// True while an add or remove is saving and syncing its group. A re-read of the store in
    /// that window sees the file from before the save, so the Manager must not reload then.
    /// </summary>
    public bool IsSaving => _pendingSaves > 0;

    [RelayCommand]
    private async Task AddAppsAsync(IEnumerable<string>? paths)
    {
        // Held in a local: the editor can be rebound while the save is awaited (closing the
        // file picker re-activates the window, which may reload the groups).
        var item = _boundItem;
        if (paths is null || item is null)
        {
            return;
        }

        var added = 0;
        foreach (var raw in paths)
        {
            if (!AppEntry.TryCreateFromPath(raw, out var entry))
            {
                _logger?.LogDebug("Ignored non-executable drop {Path}", raw);
                continue;
            }

            item.Config.Apps.Add(entry);

            var appVm = new AppEntryViewModel(entry);
            Apps.Add(appVm);
            LoadIconInto(appVm);
            added++;
        }

        if (added > 0)
        {
            await SaveAndSyncAsync(item).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RemoveAppAsync(AppEntryViewModel? app)
    {
        var item = _boundItem;
        if (app is null || item is null)
        {
            return;
        }

        item.Config.Apps.Remove(app.Entry);
        Apps.Remove(app);

        await SaveAndSyncAsync(item).ConfigureAwait(true);
    }

    private async Task SaveAndSyncAsync(GroupListItemViewModel item)
    {
        // Every await here resumes on the UI thread, so a plain counter is enough.
        _pendingSaves++;
        try
        {
            await _store.SaveAsync(item.Config).ConfigureAwait(true);
            await _syncService.SyncAsync(item.Config).ConfigureAwait(true);
        }
        finally
        {
            _pendingSaves--;
        }

        item.NotifyAppCountChanged();
    }

    [RelayCommand]
    private async Task PinToTaskbarAsync()
    {
        if (_boundItem is null)
        {
            return;
        }

        // Make sure the .lnk exists before invoking the WinRT pin runner — the launcher
        // process needs the on-disk shortcut to derive its launch identity, and the future
        // pinned-tile click path uses the .lnk for AUMID-derived group resolution.
        if (!await EnsureShortcutExistsAsync(_boundItem.Id).ConfigureAwait(true))
        {
            return; // EnsureShortcutExistsAsync already notified the user.
        }

        var result = await _pinService.PinAsync(_boundItem.Id).ConfigureAwait(true);
        _logger?.LogInformation("PinToTaskbar for {GroupId} returned {Result}.", _boundItem.Id, result);

        switch (result)
        {
            case PinResult.Success:
                _userConfirmation.Notify(
                    "Pinned",
                    $"\"{_boundItem.Config.GroupName}\" is now on your taskbar.");
                break;

            case PinResult.UserDenied:
                // User clicked Cancel in the system dialog — they know what happened, no Notify.
                break;

            case PinResult.Unsupported:
                _logger?.LogWarning("Pin unsupported on this Windows build/SKU; opening Explorer fallback.");
                _userConfirmation.Notify(
                    "Pin not available",
                    "This Windows build does not allow apps to pin themselves to the taskbar. " +
                    "Opening the shortcut folder — right-click the .lnk and choose " +
                    "\"Pin to taskbar\" (or \"Show more options\" → \"Pin to taskbar\" on Win11 22H2+).");
                OpenShortcutInExplorer(_boundItem.Id);
                break;

            case PinResult.NotVerified:
                _logger?.LogWarning(
                    "Windows reported a successful pin for {GroupId} but no matching tile was found.",
                    _boundItem.Id);
                _userConfirmation.Notify(
                    "Pin not confirmed",
                    $"Windows reported that \"{_boundItem.Config.GroupName}\" was pinned, but no tile " +
                    "appeared on the taskbar. Opening the shortcut folder so you can pin it manually — " +
                    "right-click the .lnk and choose \"Pin to taskbar\".");
                OpenShortcutInExplorer(_boundItem.Id);
                break;

            case PinResult.Error:
            default:
                _userConfirmation.Notify(
                    "Pin failed",
                    "Pinning to the taskbar failed unexpectedly. Opening the shortcut folder " +
                    "so you can pin manually. Check %APPDATA%\\TaskbarFolders\\logs for details.");
                OpenShortcutInExplorer(_boundItem.Id);
                break;
        }
    }

    [RelayCommand]
    private void ShowShortcut()
    {
        if (_boundItem is null)
        {
            return;
        }

        OpenShortcutInExplorer(_boundItem.Id);
    }

    /// <summary>
    /// Validates the shortcut path, runs a one-shot sync if missing, and notifies the user
    /// when the .lnk still cannot be produced. Returns <see langword="true"/> if a usable
    /// shortcut exists at the end.
    /// </summary>
    private async Task<bool> EnsureShortcutExistsAsync(string groupId)
    {
        var shortcutPath = _paths.GetGroupShortcutFile(groupId);

        // Defence in depth: AppDataPathProvider already rejects malformed ids, but a future
        // path-provider implementation could differ. Verify the resolved path is inside the
        // shortcuts directory before handing it to Explorer — keeps any argument-injection
        // shape from sneaking into the command line.
        var shortcutsRoot = _paths.ShortcutsDirectory;
        var fullShortcutPath = Path.GetFullPath(shortcutPath);
        var fullRoot = Path.GetFullPath(shortcutsRoot);
        if (!fullShortcutPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogError(
                "Pin: resolved shortcut path {Path} is outside the shortcuts root {Root}.",
                fullShortcutPath, fullRoot);
            return false;
        }

        if (!File.Exists(fullShortcutPath) && _boundItem is not null)
        {
            _logger?.LogInformation(
                "Pin: shortcut {Path} missing — running a one-shot sync before continuing.",
                fullShortcutPath);
            try
            {
                await _syncService.SyncAsync(_boundItem.Config).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Pin: on-demand sync for group {GroupId} threw.", groupId);
            }
        }

        if (!File.Exists(fullShortcutPath))
        {
            _logger?.LogError("Pin: shortcut {Path} still missing after sync.", fullShortcutPath);
            _userConfirmation.Notify(
                "Shortcut not available",
                "The pinnable shortcut for this group could not be created. This usually means " +
                "the launcher binary cannot be located, or no app icon could be extracted. " +
                "Check the application log under %APPDATA%\\TaskbarFolders\\logs for the probed " +
                "paths, then add an app to the group to retry.");
            return false;
        }

        return true;
    }

    private void OpenShortcutInExplorer(string groupId)
    {
        var shortcutPath = _paths.GetGroupShortcutFile(groupId);
        var fullShortcutPath = Path.GetFullPath(shortcutPath);
        var fullRoot = Path.GetFullPath(_paths.ShortcutsDirectory);

        // Same defence-in-depth check as above so this entry point is safe even if the
        // caller bypasses EnsureShortcutExistsAsync.
        if (!fullShortcutPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogError("OpenShortcut: refused {Path} (outside shortcuts root).", fullShortcutPath);
            return;
        }
        if (!File.Exists(fullShortcutPath))
        {
            _logger?.LogWarning("OpenShortcut: {Path} does not exist.", fullShortcutPath);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{fullShortcutPath}\"",
            UseShellExecute = true,
        });
    }

    private void OnAppsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        SchedulePreviewRefresh();

    private async void SchedulePreviewRefresh()
    {
        CancellationToken token;
        lock (_previewCtsLock)
        {
            // Atomically retire the previous CTS before publishing the new one so two
            // concurrent CollectionChanged callbacks cannot race on the field and trip
            // an ObjectDisposedException when the second one tries to Cancel a CTS that
            // the first already disposed.
            var previous = _previewCts;
            _previewCts = new CancellationTokenSource();
            token = _previewCts.Token;

            previous?.Cancel();
            previous?.Dispose();
        }

        try
        {
            await Task.Delay(PreviewDebounce, token).ConfigureAwait(true);
            RegeneratePreview();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later change — fine.
        }
    }

    private void RegeneratePreview()
    {
        if (Apps.Count == 0)
        {
            CompositeIconPreview = null;
            return;
        }

        var icons = new List<BitmapSource>(Math.Min(Apps.Count, CompositeIconGenerator.MaxTiles));
        foreach (var app in Apps)
        {
            if (icons.Count >= CompositeIconGenerator.MaxTiles)
            {
                break;
            }

            var icon = app.Icon ?? ExtractAndCache(app.Path, PreviewSize);
            if (icon is not null)
            {
                icons.Add(icon);
            }
        }

        if (icons.Count == 0)
        {
            CompositeIconPreview = null;
            return;
        }

        CompositeIconPreview = _composer.GenerateComposite(icons, PreviewSize);
    }

    private void LoadIconInto(AppEntryViewModel app)
    {
        app.Icon = ExtractAndCache(app.Path, PreviewSize);
    }

    private BitmapSource? ExtractAndCache(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_cache.TryGet(path, size, out var cached))
        {
            return cached;
        }

        var icon = _extractor.ExtractIcon(path, size);
        if (icon is not null)
        {
            _cache.Set(path, size, icon);
        }
        return icon;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Apps.CollectionChanged -= OnAppsCollectionChanged;
        lock (_previewCtsLock)
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;
        }
        _disposed = true;
    }
}
