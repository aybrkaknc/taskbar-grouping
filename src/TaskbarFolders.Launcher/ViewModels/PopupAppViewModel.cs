using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.ViewModels;

/// <summary>
/// Read-only view model for a single app shown in the launcher popup. The launcher does not
/// edit entries — clicks invoke the launch command on the parent <see cref="PopupViewModel"/>.
/// </summary>
public sealed partial class PopupAppViewModel : ObservableObject
{
    /// <summary>Initializes a new instance from an existing entry.</summary>
    /// <param name="entry">Source entry from the group config.</param>
    public PopupAppViewModel(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        Name = entry.Name;
        Path = entry.Path;
        Arguments = entry.Arguments;
        IconPath = entry.IconPath;
    }

    /// <summary>Underlying entry. Public so launch logic can dispatch on it.</summary>
    public AppEntry Entry { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Absolute path to the executable or shortcut.</summary>
    public string Path { get; }

    /// <summary>Optional command-line arguments.</summary>
    public string? Arguments { get; }

    /// <summary>The image chosen as this app's icon, used in place of the program's own when it loads.</summary>
    public string? IconPath { get; }

    /// <summary>Extracted icon shown in the grid tile.</summary>
    [ObservableProperty]
    private BitmapSource? _icon;

    /// <summary>
    /// Whether the app turned out to have no icon. Kept apart from a <c>null</c>
    /// <see cref="Icon"/>, which also means "still loading", so the placeholder only appears
    /// once extraction has finished without one.
    /// </summary>
    [ObservableProperty]
    private bool _iconMissing;
}
