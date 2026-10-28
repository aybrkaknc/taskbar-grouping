using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// Row in the editor's app list — wraps a single <see cref="AppEntry"/> and carries the
/// extracted icon for display. Two-way bindings on <see cref="Name"/>, <see cref="Path"/>,
/// and <see cref="Arguments"/> write through to the underlying entry so saves see edits.
/// <see cref="DisplayPath"/> is the presentation form of <see cref="Path"/>; the entry always
/// stores and launches the real one.
/// </summary>
public sealed partial class AppEntryViewModel : ObservableObject
{
    /// <summary>Initializes a wrapper around an existing app entry.</summary>
    /// <param name="entry">Underlying app entry; identity is shared by reference.</param>
    public AppEntryViewModel(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        _name = entry.Name;
        _path = entry.Path;
        _arguments = entry.Arguments;
        _iconPath = entry.IconPath;
    }

    /// <summary>Underlying entry. Public so the editor view model can persist it.</summary>
    public AppEntry Entry { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPath))]
    private string _path;

    /// <summary>
    /// <see cref="Path"/> with the user's profile directory collapsed to <c>%USERPROFILE%</c>,
    /// for display only. The row's tooltip carries the real path.
    /// </summary>
    public string DisplayPath => PathDisplay.ForDisplay(Path);

    [ObservableProperty]
    private string? _arguments;

    /// <summary>
    /// The icon shown for the app: the image in <see cref="IconPath"/> when there is one,
    /// otherwise the program's own. Set by the editor.
    /// </summary>
    [ObservableProperty]
    private BitmapSource? _icon;

    /// <summary>The stored copy of an image chosen as this app's icon, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomIcon))]
    private string? _iconPath;

    /// <summary>Whether the app's settings are open under its row. At most one row is open.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Whether the app shows an image the user chose rather than the program's own icon.</summary>
    public bool HasCustomIcon => IconPath is not null;

    /// <summary>The name the app gets when its display name is cleared: the file name.</summary>
    public string DefaultName => System.IO.Path.GetFileNameWithoutExtension(Path);

    // A cleared name falls back to the file name rather than leaving an unlabelled tile.
    partial void OnNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Name = DefaultName;
            return;
        }

        Entry.Name = value.Trim();
    }

    partial void OnPathChanged(string value) => Entry.Path = value;

    // Blank means "no arguments", stored as null so it is left out of the file.
    partial void OnArgumentsChanged(string? value) =>
        Entry.Arguments = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    partial void OnIconPathChanged(string? value) => Entry.IconPath = value;
}
