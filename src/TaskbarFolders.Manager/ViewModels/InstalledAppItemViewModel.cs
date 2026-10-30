using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFolders.Core.Apps;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>One row of the installed-apps picker.</summary>
public sealed partial class InstalledAppItemViewModel : ObservableObject
{
    /// <summary>Initializes a row.</summary>
    /// <param name="app">The installed app.</param>
    /// <param name="storedPath">What the app would be stored as (<see cref="InstalledApp.ToEntry"/>).</param>
    /// <param name="isInGroup">Whether the group already holds it; such a row cannot be picked.</param>
    public InstalledAppItemViewModel(InstalledApp app, string storedPath, bool isInGroup)
    {
        ArgumentNullException.ThrowIfNull(app);

        App = app;
        StoredPath = storedPath;
        IsInGroup = isInGroup;
    }

    /// <summary>The installed app.</summary>
    public InstalledApp App { get; }

    /// <summary>The name Start shows.</summary>
    public string Name => App.Name;

    /// <summary>What the app is stored as, and drawn from.</summary>
    public string StoredPath { get; }

    /// <summary>Whether the group already holds the app.</summary>
    public bool IsInGroup { get; }

    /// <summary>The app's icon; <see langword="null"/> until it is loaded.</summary>
    [ObservableProperty]
    private BitmapSource? _icon;

    /// <summary>Whether the app has no icon to show, once loading has tried.</summary>
    [ObservableProperty]
    private bool _iconMissing;

    /// <summary>Whether the app is picked. Stays false for an app already in the group.</summary>
    [ObservableProperty]
    private bool _isSelected;

    // A row of the group's own app does not take the tick.
    partial void OnIsSelectedChanged(bool value)
    {
        if (value && IsInGroup)
        {
            IsSelected = false;
        }
    }
}
