using System;

namespace TaskbarFolders.Shared.Models;

/// <summary>
/// The size of one popup tile for a group's icon size, name setting and tile spacing. Kept free
/// of WPF so the rules can be tested on their own, and in Shared so the Manager's preview reads
/// the same numbers as the popup; the tile template, the window's width and the size asked of the
/// icon extractor all read it, so they cannot drift apart.
/// </summary>
/// <param name="IconSize">Side of the icon box, in DIPs.</param>
/// <param name="Width">Width of the tile itself — its hover box — in DIPs.</param>
/// <param name="Height">Height of the tile itself, in DIPs.</param>
/// <param name="LabelMaxWidth">Widest the name under the icon may be before it is trimmed, in DIPs.</param>
/// <param name="ExtractPx">Pixel size to ask the icon extractor for.</param>
/// <param name="Spacing">Gap between neighbouring tiles, in DIPs; half of it on each side of a tile.</param>
public readonly record struct TileMetrics(int IconSize, double Width, double Height, double LabelMaxWidth, int ExtractPx, int Spacing = 0)
{
    // The tile button's padding on each side (AppTileButton in PopupWindow.xaml).
    private const double Padding = 8;

    // With names: the narrowest tile, so small icons still leave the label 80 DIPs, and the room
    // below the icon for the 6 DIP gap and one line of 11 pt text, with the slack the popup has
    // always had. 48 + 48 = 96: the default is the tile from before these settings existed.
    private const double MinWidthWithName = 96;
    private const double NameAllowance = 48;

    // Without names: the icon, its padding and a little air.
    private const double IconOnlyAllowance = 24;

    /// <summary>The tile of a group that never changed its settings: 96 × 96 with a 48 DIP icon, no gap.</summary>
    public static TileMetrics Default { get; } = Compute(GroupConfig.DefaultIconSize, showNames: true);

    /// <summary>Width of the grid cell a tile takes up: the tile and its share of the gaps.</summary>
    public double CellWidth => Width + Spacing;

    /// <summary>Height of the grid cell a tile takes up.</summary>
    public double CellHeight => Height + Spacing;

    /// <summary>The margin around each tile that makes up the gap: half on each side.</summary>
    public double HalfSpacing => Spacing / 2.0;

    /// <summary>Works out the tile for <paramref name="iconSize"/>.</summary>
    /// <param name="iconSize">Icon size in DIPs, normally one of <see cref="GroupConfig.IconSizes"/>.</param>
    /// <param name="showNames">Whether the name is shown under the icon.</param>
    /// <param name="spacing">Gap between tiles in DIPs, normally one of <see cref="GroupConfig.TileSpacings"/>.</param>
    public static TileMetrics Compute(int iconSize, bool showNames, int spacing = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iconSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(spacing);

        var width = showNames ? Math.Max(MinWidthWithName, iconSize + (4 * Padding)) : iconSize + IconOnlyAllowance;
        var height = iconSize + (showNames ? NameAllowance : IconOnlyAllowance);

        // Twice the DIP size: at 150 % scaling a 48 DIP icon covers 72 pixels, and a source any
        // smaller than that is stretched and comes out soft.
        return new TileMetrics(iconSize, width, height, width - (2 * Padding), iconSize * 2, spacing);
    }
}
