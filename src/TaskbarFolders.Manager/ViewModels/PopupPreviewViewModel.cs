using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// What the preview on Appearance shows: the group's first apps laid out as the popup lays them
/// out. Its property names are the ones Core's shared tile template binds to (<see cref="Tile"/>,
/// <see cref="ShowNames"/>, and each tile's Icon, IconMissing and Name).
/// </summary>
public sealed partial class PopupPreviewViewModel : ObservableObject
{
    /// <summary>The most tiles the preview draws; enough to show a few rows at six columns.</summary>
    public const int MaxTiles = 12;

    private const int SampleTiles = 3;

    /// <summary>Size of each tile and the gap between them, as the popup computes it.</summary>
    [ObservableProperty]
    private TileMetrics _tile = TileMetrics.Default;

    /// <summary>Whether tiles show the app's name.</summary>
    [ObservableProperty]
    private bool _showNames = true;

    /// <summary>Columns the grid uses: the group's limit, capped by the number of tiles, as in the popup.</summary>
    [ObservableProperty]
    private int _effectiveColumns = 1;

    /// <summary>The tiles drawn: the group's apps, at most <see cref="MaxTiles"/>, or sample tiles for an empty group.</summary>
    [ObservableProperty]
    private IReadOnlyList<AppEntryViewModel> _tiles = [];

    /// <summary>How many apps the group has beyond the ones drawn.</summary>
    [ObservableProperty]
    private int _moreCount;

    /// <summary>Whether <see cref="Tiles"/> are samples standing in for an empty group's apps.</summary>
    [ObservableProperty]
    private bool _isSample;

    // The surface round the grid in PopupPreview.xaml: 12 DIP of padding and a 1 DIP border on each side.
    private const double Frame = 26;

    /// <summary>The width the preview wants at full size: its grid of cells and the frame round it.</summary>
    public double NaturalWidth => (EffectiveColumns * Tile.CellWidth) + Frame;

    /// <summary>Lays the preview out for the given apps and grid settings.</summary>
    /// <param name="apps">The group's apps, in order.</param>
    /// <param name="columns">The group's most columns per row.</param>
    /// <param name="iconSize">The group's icon size in DIPs.</param>
    /// <param name="showNames">Whether the group shows names.</param>
    /// <param name="tileSpacing">The group's gap between tiles in DIPs.</param>
    public void Update(IReadOnlyList<AppEntryViewModel> apps, int columns, int iconSize, bool showNames, int tileSpacing)
    {
        ArgumentNullException.ThrowIfNull(apps);

        IsSample = apps.Count == 0;
        Tiles = IsSample ? Samples() : apps.Take(MaxTiles).ToList();
        MoreCount = Math.Max(0, apps.Count - MaxTiles);
        Tile = TileMetrics.Compute(iconSize, showNames, tileSpacing);
        ShowNames = showNames;
        EffectiveColumns = Math.Max(1, Math.Min(columns, Tiles.Count));
        OnPropertyChanged(nameof(NaturalWidth));
    }

    private static List<AppEntryViewModel> Samples() =>
        Enumerable.Range(1, SampleTiles)
            .Select(i => new AppEntryViewModel(new AppEntry { Name = $"App {i}", Path = $"App {i}.exe" }))
            .ToList();
}
