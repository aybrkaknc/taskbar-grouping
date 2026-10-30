using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Apps;
using TaskbarFolders.Core.Icons;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// The installed-apps picker (ADR-009): Windows' installed-apps list, a search over the names,
/// many apps picked at once. The group's own apps are shown as in the group and cannot be picked
/// again. Icons load one by one after the list is shown, and stop when the picker is disposed.
/// </summary>
public sealed partial class InstalledAppsPickerViewModel : ObservableObject, IDisposable
{
    // The row's icon: small, and the size asked of the shell and used as the cache key.
    private const int IconSize = 32;

    private readonly IInstalledAppsProvider _provider;
    private readonly IIconExtractor _extractor;
    private readonly IIconCache _cache;
    private readonly ILogger<InstalledAppsPickerViewModel>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private List<InstalledAppItemViewModel> _all = [];
    private Task _icons = Task.CompletedTask;

    /// <summary>Initializes the picker.</summary>
    /// <param name="provider">Reads the installed-apps list.</param>
    /// <param name="extractor">Draws an app's icon.</param>
    /// <param name="cache">Keeps drawn icons.</param>
    /// <param name="logger">Optional logger.</param>
    public InstalledAppsPickerViewModel(
        IInstalledAppsProvider provider,
        IIconExtractor extractor,
        IIconCache cache,
        ILogger<InstalledAppsPickerViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(cache);

        _provider = provider;
        _extractor = extractor;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Whether the list is being read.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Why the list could not be read, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _loadError;

    /// <summary>What the names are searched for; blank shows every app.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The rows the search keeps, in the list's order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<InstalledAppItemViewModel> _visible = [];

    /// <summary>How many apps are picked, hidden ones included.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private int _selectedCount;

    /// <summary>Whether a loaded list shows nothing (no app matches the search).</summary>
    public bool IsEmpty => !IsLoading && LoadError is null && Visible.Count == 0;

    /// <summary>Whether there is something to add.</summary>
    public bool CanAdd => SelectedCount > 0;

    /// <summary>The picked apps, in the list's order.</summary>
    public IReadOnlyList<InstalledApp> Selected => _all.Where(i => i.IsSelected).Select(i => i.App).ToList();

    /// <summary>
    /// Reads the list and shows it, then starts loading the icons. <paramref name="groupPaths"/>
    /// are the paths of the apps the group holds, compared ignoring case.
    /// </summary>
    /// <param name="groupPaths">The group's apps' <c>Path</c>s.</param>
    /// <returns>A task that completes once the list is shown.</returns>
    public async Task LoadAsync(IEnumerable<string> groupPaths)
    {
        ArgumentNullException.ThrowIfNull(groupPaths);

        // Taken now: the window may close, and dispose the source, while the list is read.
        var token = _cts.Token;
        var inGroup = new HashSet<string>(groupPaths, StringComparer.OrdinalIgnoreCase);
        IsLoading = true;
        LoadError = null;
        try
        {
            var apps = await _provider.ListAsync(token).ConfigureAwait(true);
            _all = apps
                .Select(a =>
                {
                    var path = a.ToEntry().Path;
                    var row = new InstalledAppItemViewModel(a, path, inGroup.Contains(path));
                    row.PropertyChanged += OnRowChanged;
                    return row;
                })
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "The installed-apps list could not be read.");
            LoadError = "Windows' list of installed apps could not be read. Try again, or use Add apps… to browse for a program.";
            _all = [];
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsLoading = false;
        }

        ApplySearch();
        _icons = Task.Run(() => LoadIcons(_all, token), CancellationToken.None);
    }

    /// <summary>For tests: completes once every icon has been tried.</summary>
    /// <returns>The icon loading task.</returns>
    internal Task WhenIconsLoadedAsync() => _icons;

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnLoadErrorChanged(string? value) => OnPropertyChanged(nameof(IsEmpty));

    private void ApplySearch()
    {
        var term = SearchText.Trim();
        Visible = term.Length == 0
            ? _all
            : _all.Where(i => i.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledAppItemViewModel.IsSelected))
        {
            SelectedCount = _all.Count(i => i.IsSelected);
        }
    }

    // One by one, in the list's order, so the rows on screen fill first; the shell lock
    // serialises them anyway. A frozen bitmap may be handed to the UI from here.
    private void LoadIcons(List<InstalledAppItemViewModel> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // A missing icon stays null (never a cached placeholder); the row draws its own glyph.
            row.Icon = AppIcons.Extract(row.StoredPath, IconSize, _extractor, _cache);
            row.IconMissing = row.Icon is null;
        }
    }
}
