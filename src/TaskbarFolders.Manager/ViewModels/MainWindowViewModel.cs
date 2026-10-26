using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<MainWindowViewModel>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public MainWindowViewModel(
        IGroupConfigStore store,
        GroupEditorViewModel editor,
        IGroupSyncService syncService,
        IUserConfirmation confirmation,
        ILogger<MainWindowViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(confirmation);

        _store = store;
        Editor = editor;
        _syncService = syncService;
        _confirmation = confirmation;
        _logger = logger;
    }

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

    /// <summary>Pending name for <see cref="AddGroupCommand"/>; bound to the sidebar TextBox.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGroupCommand))]
    private string _newGroupName = string.Empty;

    /// <summary>Currently highlighted sidebar entry, or <see langword="null"/> if none.</summary>
    [ObservableProperty]
    private GroupListItemViewModel? _selectedGroup;

    /// <summary>Sidebar items in alphabetical order by <see cref="GroupListItemViewModel.Name"/>.</summary>
    public ObservableCollection<GroupListItemViewModel> Groups { get; } = [];

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
            Groups.Add(new GroupListItemViewModel(config));
        }

        _logger?.LogInformation("Loaded {Count} group(s) from store.", Groups.Count);
    }

    // The pre-v0.4.4 command had no CanExecute: clicking "+ Add" with an empty name box
    // hit a silent early-return and the button appeared dead. Disabling the button (and
    // the Enter shortcut, which checks CanExecute) makes the empty-name state visible.
    private bool CanAddGroup() => !string.IsNullOrWhiteSpace(NewGroupName);

    [RelayCommand(CanExecute = nameof(CanAddGroup))]
    private async Task AddGroupAsync()
    {
        // Defence in depth for any future caller that bypasses CanExecute.
        var trimmed = NewGroupName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        var config = new GroupConfig { GroupName = trimmed };
        await _store.SaveAsync(config).ConfigureAwait(true);

        var item = new GroupListItemViewModel(config);
        InsertAlphabetically(item);
        SelectedGroup = item;
        NewGroupName = string.Empty;

        // No apps yet → SyncAsync is a no-op. Still call it so the contract stays consistent
        // (any future addition that needs to fire on every save sees the event).
        await _syncService.SyncAsync(config).ConfigureAwait(true);

        _logger?.LogInformation("Created group {Id} '{Name}'.", config.Id, config.GroupName);
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

        EditedGroupName = trimmed;
        RenameGroupCommand.NotifyCanExecuteChanged();
        MoveToAlphabeticalPosition(group);

        // Rewrites the shortcut and the Start Menu anchor under the new name; the anchor
        // written under the old name is removed once its replacement exists.
        await _syncService.SyncAsync(group.Config).ConfigureAwait(true);

        _logger?.LogInformation("Renamed group {Id} from '{Old}' to '{New}'.", group.Id, previousName, trimmed);
    }

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
