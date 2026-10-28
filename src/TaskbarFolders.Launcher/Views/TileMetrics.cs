using System;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Views;

/// <summary>
/// The size of one popup tile for a group's icon size and name setting. Kept free of WPF so the
/// rules can be tested on their own; the tile template, the window's width and the size asked of
/// the icon extractor all read it, so they cannot drift apart.
/// </summary>
/// <param name="IconSize">Side of the icon box, in DIPs.</param>
/// <param name="Width">Width of the tile, in DIPs.</param>
/// <param name="Height">Height of the tile, in DIPs.</param>
/// <param name="LabelMaxWidth">Widest the name under the icon may be before it is trimmed, in DIPs.</param>
/// <param name="ExtractPx">Pixel size to ask the icon extractor for.</param>
public readonly record struct TileMetrics(int IconSize, double Width, double Height, double LabelMaxWidth, int ExtractPx)
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

    /// <summary>The tile of a group that never changed its settings: 96 × 96 with a 48 DIP icon.</summary>
    public static TileMetrics Default { get; } = Compute(GroupConfig.DefaultIconSize, showNames: true);

    /// <summary>Works out the tile for <paramref name="iconSize"/>.</summary>
    /// <param name="iconSize">Icon size in DIPs, normally one of <see cref="GroupConfig.IconSizes"/>.</param>
    /// <param name="showNames">Whether the name is shown under the icon.</param>
    public static TileMetrics Compute(int iconSize, bool showNames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iconSize, 1);

        var width = showNames ? Math.Max(MinWidthWithName, iconSize + (4 * Padding)) : iconSize + IconOnlyAllowance;
        var height = iconSize + (showNames ? NameAllowance : IconOnlyAllowance);

        // Twice the DIP size: at 150 % scaling a 48 DIP icon covers 72 pixels, and a source any
        // smaller than that is stretched and comes out soft.
        return new TileMetrics(iconSize, width, height, width - (2 * Padding), iconSize * 2);
    }
}
