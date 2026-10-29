using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>An image the user picked as the icon of <paramref name="App"/>.</summary>
/// <param name="App">The app's row.</param>
/// <param name="ImagePath">The picked file.</param>
public sealed record AppIconChoice(AppEntryViewModel App, string ImagePath);

/// <summary>One choice of a grid setting on the Appearance tab.</summary>
/// <param name="Value">The value stored in the group.</param>
/// <param name="Label">What the choice reads as.</param>
public sealed record GridOption(int Value, string Label);

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
    private readonly ICustomGroupIconStore _customIcons;
    private readonly IPinnedTileRemover _pins;
    private readonly ILogger<GroupEditorViewModel>? _logger;

    private GroupListItemViewModel? _boundItem;
    private CancellationTokenSource? _previewCts;
    private readonly object _previewCtsLock = new();
    private int _pendingSaves;
    private bool _binding;
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
        ICustomGroupIconStore customIcons,
        IPinnedTileRemover pins,
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
        ArgumentNullException.ThrowIfNull(customIcons);
        ArgumentNullException.ThrowIfNull(pins);

        _extractor = extractor;
        _composer = composer;
        _cache = cache;
        _store = store;
        _syncService = syncService;
        _paths = paths;
        _userConfirmation = userConfirmation;
        _pinService = pinService;
        _customIcons = customIcons;
        _pins = pins;
        _logger = logger;
    }

    /// <summary>
    /// Whether the bound group already has a tile on the taskbar. While it has, Pin to taskbar
    /// is off: asking Windows to pin a pinned app again redrew the tile with a blank icon.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PinToTaskbarCommand))]
    private bool _isPinned;

    /// <summary>
    /// Re-reads whether the bound group is on the taskbar — the user may have pinned or unpinned
    /// it by hand while the Manager was in the background.
    /// </summary>
    public void RefreshPinState()
    {
        IsPinned = _boundItem is not null && _pins.IsPinned(_boundItem.Id);
        PinToTaskbarCommand.NotifyCanExecuteChanged();
    }

    private bool CanPinToTaskbar() => _boundItem is not null && !IsPinned;

    /// <summary>
    /// Raised after the editor has synced a group, which may have given it a new icon file.
    /// The argument is the group synced, which need not be the one bound by then.
    /// </summary>
    public event EventHandler<GroupListItemViewModel>? GroupSynced;

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

    /// <summary>
    /// Whether the bound group uses an image the user chose instead of the composite of its
    /// apps' icons. <see cref="CompositeIconPreview"/> then shows that image.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetGroupIconCommand))]
    private bool _hasCustomIcon;

    /// <summary>The column counts a group can choose, <see cref="GroupConfig.MinColumns"/> to <see cref="GroupConfig.MaxColumns"/>.</summary>
    public static IReadOnlyList<GridOption> ColumnOptions { get; } = Enumerable
        .Range(GroupConfig.MinColumns, GroupConfig.MaxColumns - GroupConfig.MinColumns + 1)
        .Select(c => new GridOption(c, c.ToString(CultureInfo.CurrentCulture)))
        .ToArray();

    /// <summary>The icon sizes a group can choose, one per <see cref="GroupConfig.IconSizes"/>.</summary>
    public static IReadOnlyList<GridOption> IconSizeOptions { get; } =
    [
        new(32, "Small"),
        new(48, "Medium"),
        new(64, "Large"),
        new(80, "Extra large"),
    ];

    /// <summary>Columns of the bound group's popup. Saved as it changes.</summary>
    [ObservableProperty]
    private int _columns = 3;

    /// <summary>Icon size of the bound group's popup, in DIPs. Saved as it changes.</summary>
    [ObservableProperty]
    private int _iconSize = GroupConfig.DefaultIconSize;

    /// <summary>Whether the bound group's popup shows app names. Saved as it changes.</summary>
    [ObservableProperty]
    private bool _showNames = true;

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
        HasCustomIcon = item is not null && _customIcons.Exists(item.Config.Id);
        RefreshPinState();
        if (item is null)
        {
            CompositeIconPreview = null;
            return;
        }

        // Showing the group's settings is not an edit; the change handlers must not save.
        _binding = true;
        try
        {
            Columns = item.Config.Columns;
            IconSize = item.Config.IconSize;
            ShowNames = item.Config.ShowNames;
        }
        finally
        {
            _binding = false;
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
                SaveOnEdit(item, appVm);
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
            SaveOnEdit(item, appVm);
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

    /// <summary>
    /// Makes a copy of the image at <paramref name="path"/> the bound group's icon, in place of
    /// the composite of its apps' icons, and regenerates the group's icon files.
    /// </summary>
    [RelayCommand]
    private async Task ChooseGroupIconAsync(string? path)
    {
        var item = _boundItem;
        if (item is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!await _customIcons.TryImportAsync(item.Config.Id, path).ConfigureAwait(true))
        {
            _userConfirmation.Notify(
                "Group icon",
                $"This file could not be read as an image, so the group's icon is unchanged:\n{path}");
            return;
        }

        await GroupIconChangedAsync(item, custom: true).ConfigureAwait(true);
    }

    /// <summary>Drops the bound group's chosen icon, so its icon is composed from its apps again.</summary>
    [RelayCommand(CanExecute = nameof(HasCustomIcon))]
    private async Task ResetGroupIconAsync()
    {
        var item = _boundItem;
        if (item is null)
        {
            return;
        }

        _customIcons.Delete(item.Config.Id);
        await GroupIconChangedAsync(item, custom: false).ConfigureAwait(true);
    }

    private async Task GroupIconChangedAsync(GroupListItemViewModel item, bool custom)
    {
        if (ReferenceEquals(_boundItem, item))
        {
            HasCustomIcon = custom;
            RegeneratePreview();
        }

        // Counted like a save: sync writes the icon files the activation re-read would race.
        _pendingSaves++;
        try
        {
            await _syncService.SyncAsync(item.Config).ConfigureAwait(true);
        }
        finally
        {
            _pendingSaves--;
        }

        GroupSynced?.Invoke(this, item);
    }

    /// <summary>
    /// Opens <paramref name="app"/>'s settings under its row, closing any other row's, or closes
    /// them when they are already open.
    /// </summary>
    [RelayCommand]
    private void ToggleAppDetails(AppEntryViewModel? app)
    {
        if (app is null)
        {
            return;
        }

        var open = !app.IsExpanded;
        CollapseAppDetails();
        app.IsExpanded = open;
    }

    /// <summary>Closes whichever app's settings are open.</summary>
    [RelayCommand]
    private void CollapseAppDetails()
    {
        foreach (var other in Apps)
        {
            other.IsExpanded = false;
        }
    }

    /// <summary>
    /// Stores a copy of the chosen image as the app's icon and regenerates the group's icon,
    /// whose composite shows it too.
    /// </summary>
    [RelayCommand]
    private async Task ChooseAppIconAsync(AppIconChoice? choice)
    {
        var item = _boundItem;
        if (item is null || choice is null || string.IsNullOrWhiteSpace(choice.ImagePath))
        {
            return;
        }

        var stored = await _customIcons.TryImportAppIconAsync(item.Config.Id, choice.ImagePath).ConfigureAwait(true);
        if (stored is null)
        {
            _userConfirmation.Notify(
                "App icon",
                $"This file could not be read as an image, so the app's icon is unchanged:\n{choice.ImagePath}");
            return;
        }

        choice.App.IconPath = stored;
        LoadIconInto(choice.App);
        await SaveAndSyncAsync(item).ConfigureAwait(true);
        SchedulePreviewRefresh();
    }

    /// <summary>Goes back to the program's own icon for <paramref name="app"/>.</summary>
    [RelayCommand]
    private async Task ResetAppIconAsync(AppEntryViewModel? app)
    {
        var item = _boundItem;
        if (item is null || app is null || app.IconPath is null)
        {
            return;
        }

        app.IconPath = null;
        LoadIconInto(app);
        await SaveAndSyncAsync(item).ConfigureAwait(true);
        SchedulePreviewRefresh();
    }

    /// <summary>
    /// Saves the group when an app's name or arguments are edited in its row. Neither shows in
    /// the group's icon, so there is nothing to sync.
    /// </summary>
    private void SaveOnEdit(GroupListItemViewModel item, AppEntryViewModel app)
    {
        app.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName is nameof(AppEntryViewModel.Name) or nameof(AppEntryViewModel.Arguments))
            {
                await SaveWithoutSyncAsync(item).ConfigureAwait(true);
            }
        };
    }

    partial void OnColumnsChanged(int value) => SaveGridSetting(c => c.Columns = value);

    partial void OnIconSizeChanged(int value) => SaveGridSetting(c => c.IconSize = value);

    partial void OnShowNamesChanged(bool value) => SaveGridSetting(c => c.ShowNames = value);

    /// <summary>
    /// Writes a grid setting into the bound group and saves it. The popup reads the settings
    /// each time it opens and the taskbar icon does not show them, so there is nothing to sync.
    /// </summary>
    private void SaveGridSetting(Action<GroupConfig> apply)
    {
        var item = _boundItem;
        if (_binding || item is null)
        {
            return;
        }

        apply(item.Config);
        _ = SaveWithoutSyncAsync(item);
    }

    // Never throws: a failed save is logged and reported, and the edit stays in memory.
    private async Task SaveWithoutSyncAsync(GroupListItemViewModel item)
    {
        _pendingSaves++;
        try
        {
            await _store.SaveAsync(item.Config).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not save an edit to {GroupId}.", item.Config.Id);
            _userConfirmation.Notify("Not saved", $"The change could not be saved:\n{ex.Message}");
        }
        finally
        {
            _pendingSaves--;
        }
    }

    private async Task SaveAndSyncAsync(GroupListItemViewModel item)
    {
        // Every await here resumes on the UI thread, so a plain counter is enough.
        _pendingSaves++;
        try
        {
            await _store.SaveAsync(item.Config).ConfigureAwait(true);
            // An app's icon image goes when nothing in the group points at it any more.
            _customIcons.PruneAppIcons(item.Config.Id, item.Config.Apps.Select(a => a.IconPath));
            await _syncService.SyncAsync(item.Config).ConfigureAwait(true);
        }
        finally
        {
            _pendingSaves--;
        }

        item.NotifyAppCountChanged();
        GroupSynced?.Invoke(this, item);
    }

    [RelayCommand(CanExecute = nameof(CanPinToTaskbar))]
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

        // Whatever Windows answered, the pinned folder says what is on the taskbar now.
        RefreshPinState();

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
        if (_boundItem is { } item && _customIcons.TryLoad(item.Config.Id) is { } custom)
        {
            CompositeIconPreview = custom;
            return;
        }

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

            // Same substitution as GroupSyncService, so the preview matches the pinned tile.
            icons.Add(app.Icon
                ?? AppIcons.Load(app.Entry, PreviewSize, _extractor, _cache)
                ?? PlaceholderIcon.Render(PreviewSize, PlaceholderIcon.NeutralColor));
        }

        CompositeIconPreview = _composer.GenerateComposite(icons, PreviewSize);
    }

    private void LoadIconInto(AppEntryViewModel app)
    {
        app.Icon = AppIcons.Load(app.Entry, PreviewSize, _extractor, _cache);
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
