using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFolders.Core.Themes;
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

    /// <summary>The group's name, shown in the preview's header as the popup shows it.</summary>
    [ObservableProperty]
    private string _groupName = string.Empty;

    /// <summary>
    /// The colour of the preview's panel: the popup's backdrop as near as a WPF brush can come,
    /// with its tint laid over. See <see cref="UpdateSurface"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SurfaceBrush))]
    private Color _surface = Colors.Transparent;

    // The popup's padding (12 DIP each side), its narrowest width, and the preview's 1 DIP border.
    private const double Padding = 24;
    private const double PopupMinWidth = 220;
    private const double Border = 2;

    /// <summary>
    /// The width the preview wants at full size: the popup's width — its grid of cells and its
    /// padding, but no narrower than the popup ever is — and the preview's border.
    /// </summary>
    public double NaturalWidth => Math.Max((EffectiveColumns * Tile.CellWidth) + Padding, PopupMinWidth) + Border;

    /// <summary>The panel's brush, for binding.</summary>
    public SolidColorBrush SurfaceBrush
    {
        get
        {
            var brush = new SolidColorBrush(Surface);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Paints the panel as the popup's backdrop and tint would. Windows draws the materials behind
    /// a real window only, so they are approached: Mica and Mica Alt leave the panel clear, and
    /// the Manager's own Mica shows through; Acrylic is a frosted panel; Off is the popup's exact
    /// opaque panel. The tint is laid over as the popup lays it (<see cref="PopupTints.Over"/>).
    /// </summary>
    /// <param name="backdrop">The popup's backdrop, resolved.</param>
    /// <param name="tint">The popup's tint, resolved.</param>
    /// <param name="dark">Whether the dark theme is in use.</param>
    /// <param name="accent">The user's accent shades, for <see cref="PopupTint.Accent"/>.</param>
    public void UpdateSurface(PopupBackdrop backdrop, PopupTint tint, bool dark, AccentShades accent)
    {
        var panel = backdrop switch
        {
            PopupBackdrop.Off => PopupTints.SolidPanel(dark),
            PopupBackdrop.Acrylic => dark ? Color.FromArgb(0xB3, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xB3, 0xFC, 0xFC, 0xFC),
            _ => Colors.Transparent,
        };
        Surface = PopupTints.ColorFor(tint, dark, accent) is { } colour ? PopupTints.Over(colour, panel) : panel;
    }

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
