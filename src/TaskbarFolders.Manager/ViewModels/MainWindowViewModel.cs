using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// Hosts the sidebar group list and the commands that mutate it. Persistence is delegated
/// to <see cref="IGroupConfigStore"/> so the view model is fully test-friendly.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IGroupConfigStore _store;
    private readonly IGroupSyncService _syncService;
    private readonly IUserConfirmation _confirmation;
    private readonly IGroupIconReader _iconReader;
    private readonly ILogger<MainWindowViewModel>? _logger;

    // Until the store has answered, an empty list means "not read yet", not "no groups".
    private bool _groupsLoaded;

    /// <summary>Initializes a new instance.</summary>
    public MainWindowViewModel(
        IGroupConfigStore store,
        GroupEditorViewModel editor,
        IGroupSyncService syncService,
        IUserConfirmation confirmation,
        IGroupIconReader iconReader,
        ILogger<MainWindowViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(iconReader);

        _store = store;
        Editor = editor;
        _syncService = syncService;
        _confirmation = confirmation;
        _iconReader = iconReader;
        _logger = logger;

        Editor.GroupSynced += (_, item) => item.Icon = _iconReader.TryRead(item.Id);
        Groups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoGroups));
    }

    /// <summary>Name given to a group created by <see cref="NewGroupCommand"/>, before the user renames it.</summary>
    public const string NewGroupBaseName = "New group";

    /// <summary>
    /// Asks the view to put the caret in the group name box with the name selected — after a
    /// group is created, on F2, or from the list's context menu.
    /// </summary>
    public event EventHandler? RenameRequested;

    /// <summary>
    /// Raised after a group is created, renamed or deleted, or the list is reloaded from disk —
    /// anything that changes which groups exist or what they are called.
    /// </summary>
    public event EventHandler? GroupListChanged;

    /// <summary>Detail-pane view model. Single instance reused across selection changes.</summary>
    public GroupEditorViewModel Editor { get; }

    partial void OnSelectedGroupChanged(GroupListItemViewModel? value)
    {
        Editor.Bind(value);
        EditedGroupName = value?.Name ?? string.Empty;
        RenameGroupCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Pending name for <see cref="RenameGroupCommand"/>; bound to the editor header. Follows
    /// the selection until the user edits it.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameGroupCommand))]
    private string _editedGroupName = string.Empty;

    /// <summary>Window title shown in the chrome.</summary>
    [ObservableProperty]
    private string _title = "TaskbarFolders Manager";

    /// <summary>Currently highlighted sidebar entry, or <see langword="null"/> if none.</summary>
    [ObservableProperty]
    private GroupListItemViewModel? _selectedGroup;

    /// <summary>Sidebar items in alphabetical order by <see cref="GroupListItemViewModel.Name"/>.</summary>
    public ObservableCollection<GroupListItemViewModel> Groups { get; } = [];

    /// <summary>
    /// True once the groups have been read and there are none: the window then shows the
    /// get-started steps instead of an empty editor.
    /// </summary>
    public bool HasNoGroups => _groupsLoaded && Groups.Count == 0;

    /// <summary>
    /// Replaces <see cref="Groups"/> with the contents of the store. Call once after construction
    /// (App bootstrap or window Loaded event).
    /// </summary>
    public async Task LoadGroupsAsync(CancellationToken cancellationToken = default)
    {
        var configs = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(true);

        Groups.Clear();
        foreach (var config in configs.OrderBy(g => g.GroupName, StringComparer.CurrentCultureIgnoreCase))
        {
            Groups.Add(new GroupListItemViewModel(config) { Icon = _iconReader.TryRead(config.Id) });
        }

        _groupsLoaded = true;
        OnPropertyChanged(nameof(HasNoGroups));
        _logger?.LogInformation("Loaded {Count} group(s) from store.", Groups.Count);
    }

    /// <summary>
    /// Re-reads the store and, only if a group differs from what is in memory, rebuilds
    /// <see cref="Groups"/> and reselects the previously selected group by id. Called when the
    /// window is activated: the popup can add or remove apps while the Manager is open, and
    /// the Manager's next save of that group would otherwise write its stale copy back.
    /// Returns <see langword="true"/> when the list was rebuilt.
    /// </summary>
    public async Task<bool> RefreshFromStoreAsync(CancellationToken cancellationToken = default)
    {
        // An editor save in flight means memory is ahead of the disk, not behind it. Checked
        // before and after the read: closing the add-apps picker activates the window just
        // before the add starts saving.
        if (Editor.IsSaving)
        {
            return false;
        }

        IReadOnlyList<GroupConfig> configs;
        try
        {
            configs = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Best effort: this runs from a window event, and a failed re-read must not take
            // the Manager down. The list in memory stays as it was.
            _logger?.LogWarning(ex, "Could not re-read groups on activation.");
            return false;
        }

        if (Editor.IsSaving || MatchesGroups(configs))
        {
            // Nothing changed on disk: leave the list, the selection and any half-typed
            // rename alone.
            return false;
        }

        var selectedId = SelectedGroup?.Id;
        await LoadGroupsAsync(cancellationToken).ConfigureAwait(true);
        SelectedGroup = selectedId is null
            ? null
            : Groups.FirstOrDefault(g => string.Equals(g.Id, selectedId, StringComparison.OrdinalIgnoreCase));

        _logger?.LogInformation("Groups changed on disk while the Manager was in the background; reloaded.");
        GroupListChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool MatchesGroups(IReadOnlyList<GroupConfig> configs)
    {
        if (configs.Count != Groups.Count)
        {
            return false;
        }

        var byId = Groups.ToDictionary(g => g.Id, g => g.Config, StringComparer.OrdinalIgnoreCase);
        foreach (var loaded in configs)
        {
            if (!byId.TryGetValue(loaded.Id, out var current) || !SameContent(current, loaded))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameContent(GroupConfig a, GroupConfig b) =>
        a.GroupName == b.GroupName &&
        a.Columns == b.Columns &&
        a.Apps.Count == b.Apps.Count &&
        a.Apps.Zip(b.Apps).All(pair =>
            pair.First.Path == pair.Second.Path &&
            pair.First.Name == pair.Second.Name &&
            pair.First.Arguments == pair.Second.Arguments);

    /// <summary>
    /// Creates a group under a placeholder name ("New group", or "New group 2" and so on when
    /// that is taken), selects it and asks the view to start renaming it. Nothing has to be
    /// typed first, so there is no disabled state to explain.
    /// </summary>
    [RelayCommand]
    private async Task NewGroupAsync()
    {
        var config = new GroupConfig { GroupName = UnusedGroupName() };
        await _store.SaveAsync(config).ConfigureAwait(true);

        var item = new GroupListItemViewModel(config);
        InsertAlphabetically(item);
        SelectedGroup = item;

        // No apps yet → SyncAsync is a no-op. Still call it so the contract stays consistent
        // (any future addition that needs to fire on every save sees the event).
        await _syncService.SyncAsync(config).ConfigureAwait(true);

        _logger?.LogInformation("Created group {Id} '{Name}'.", config.Id, config.GroupName);
        GroupListChanged?.Invoke(this, EventArgs.Empty);
        RenameRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects <paramref name="group"/> (the selected group when <see langword="null"/>) and
    /// asks the view to start renaming it.
    /// </summary>
    [RelayCommand]
    private void BeginRename(GroupListItemViewModel? group)
    {
        group ??= SelectedGroup;
        if (group is null)
        {
            return;
        }

        SelectedGroup = group;
        RenameRequested?.Invoke(this, EventArgs.Empty);
    }

    private string UnusedGroupName()
    {
        var taken = new HashSet<string>(Groups.Select(g => g.Name), StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(NewGroupBaseName))
        {
            return NewGroupBaseName;
        }

        var n = 2;
        while (taken.Contains($"{NewGroupBaseName} {n}"))
        {
            n++;
        }
        return $"{NewGroupBaseName} {n}";
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(GroupListItemViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        var confirmed = _confirmation.Confirm(
            caption: "Delete group?",
            message: $"Delete \"{group.Name}\" and its shortcut?\n\n"
                   + "If you pinned this group to the taskbar, remove the pin manually "
                   + "by right-clicking it and choosing \"Unpin from taskbar\".");
        if (!confirmed)
        {
            return;
        }

        await _store.DeleteAsync(group.Id).ConfigureAwait(true);
        _syncService.RemoveArtifacts(group.Id, group.Name);

        var index = Groups.IndexOf(group);
        Groups.Remove(group);

        if (ReferenceEquals(SelectedGroup, group))
        {
            // Move selection to the neighbour at the same index, if any.
            SelectedGroup = Groups.Count == 0
                ? null
                : Groups[Math.Min(index, Groups.Count - 1)];
        }

        _logger?.LogInformation("Deleted group {Id}.", group.Id);
        GroupListChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRenameGroup()
    {
        var trimmed = EditedGroupName?.Trim();
        return SelectedGroup is not null
            && !string.IsNullOrEmpty(trimmed)
            && !string.Equals(trimmed, SelectedGroup.Name, StringComparison.Ordinal);
    }

    [RelayCommand(CanExecute = nameof(CanRenameGroup))]
    private async Task RenameGroupAsync()
    {
        var group = SelectedGroup;
        var trimmed = EditedGroupName?.Trim();
        if (group is null || string.IsNullOrEmpty(trimmed) ||
            string.Equals(trimmed, group.Name, StringComparison.Ordinal))
        {
            return;
        }

        // Only the display name changes. The id — and with it the AUMID, the icon and the
        // shortcut paths — stays, which is what keeps an existing taskbar pin working.
        var previousName = group.Name;
        group.Name = trimmed;
        try
        {
            await _store.SaveAsync(group.Config).ConfigureAwait(true);
        }
        catch
        {
            group.Name = previousName;
            throw;
        }

        // The name box commits when it loses focus, and a click on another group both takes the
        // focus and changes the selection while the save is awaited. By then the box shows the
        // other group, and writing this name into it would let the next commit rename that one.
        if (ReferenceEquals(SelectedGroup, group))
        {
            EditedGroupName = trimmed;
        }
        RenameGroupCommand.NotifyCanExecuteChanged();
        MoveToAlphabeticalPosition(group);

        // Rewrites the shortcut and the Start Menu anchor under the new name; the anchor
        // written under the old name is removed once its replacement exists.
        await _syncService.SyncAsync(group.Config).ConfigureAwait(true);

        _logger?.LogInformation("Renamed group {Id} from '{Old}' to '{New}'.", group.Id, previousName, trimmed);
        GroupListChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves the name in the header box — on Enter or when the box loses focus. A blank or
    /// unchanged name is not an error: the box simply goes back to the current name.
    /// </summary>
    [RelayCommand]
    private async Task CommitRenameAsync()
    {
        if (CanRenameGroup())
        {
            await RenameGroupAsync().ConfigureAwait(true);
        }
        else
        {
            CancelRename();
        }
    }

    /// <summary>Discards an edit in the header box (Escape).</summary>
    [RelayCommand]
    private void CancelRename() => EditedGroupName = SelectedGroup?.Name ?? string.Empty;

    private void InsertAlphabetically(GroupListItemViewModel item)
    {
        Groups.Insert(AlphabeticalIndexFor(item), item);
    }

    private void MoveToAlphabeticalPosition(GroupListItemViewModel item)
    {
        // Move rather than remove-and-insert: removing the selected item would push a null
        // selection through the ListBox binding and rebuild the editor for nothing.
        var current = Groups.IndexOf(item);
        var target = AlphabeticalIndexFor(item);
        if (current >= 0 && current != target)
        {
            Groups.Move(current, target);
        }
    }

    /// <summary>Index <paramref name="item"/> belongs at among the other groups.</summary>
    private int AlphabeticalIndexFor(GroupListItemViewModel item)
    {
        var index = 0;
        foreach (var other in Groups)
        {
            if (!ReferenceEquals(other, item) &&
                StringComparer.CurrentCultureIgnoreCase.Compare(other.Name, item.Name) < 0)
            {
                index++;
            }
        }
        return index;
    }
}
