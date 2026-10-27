using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// Sidebar entry representing a single <see cref="GroupConfig"/>. Mutating <see cref="Name"/>
/// writes back to the wrapped config so changes propagate when the host view model saves.
/// </summary>
public sealed partial class GroupListItemViewModel : ObservableObject
{
    /// <summary>Initializes a new wrapper around an existing config.</summary>
    /// <param name="config">Underlying configuration; identity is shared by reference.</param>
    public GroupListItemViewModel(GroupConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Config = config;
        _name = config.GroupName;
    }

    /// <summary>Underlying configuration. Visible so the editor view model can mutate it directly.</summary>
    public GroupConfig Config { get; }

    /// <summary>Stable identifier delegating to <see cref="Config"/>.</summary>
    public string Id => Config.Id;

    /// <summary>Display name. Two-way bound; assignment keeps <see cref="Config"/> in sync.</summary>
    [ObservableProperty]
    private string _name;

    partial void OnNameChanged(string value)
    {
        Config.GroupName = value;
    }

    /// <summary>Convenience pass-through for sidebar item count badges.</summary>
    public int AppCount => Config.Apps.Count;

    /// <summary><see cref="AppCount"/> as words: "1 app", "3 apps".</summary>
    public string AppCountText => AppCount == 1 ? "1 app" : $"{AppCount} apps";

    /// <summary>
    /// The icon the group has on the taskbar, or <see langword="null"/> while it has none; the
    /// list then draws a folder glyph.
    /// </summary>
    [ObservableProperty]
    private BitmapSource? _icon;

    /// <summary>
    /// Raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/> for
    /// <see cref="AppCount"/> and <see cref="AppCountText"/>. Called by the editor after it
    /// mutates <see cref="Config"/>.Apps so the sidebar badge refreshes.
    /// </summary>
    public void NotifyAppCountChanged()
    {
        OnPropertyChanged(nameof(AppCount));
        OnPropertyChanged(nameof(AppCountText));
    }
}
