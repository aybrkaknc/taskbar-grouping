using System;
using System.Windows;

namespace TaskbarFolders.Launcher.Views;

/// <summary>
/// The arithmetic of dragging a tile in the popup's grid, kept free of the visual tree so it can
/// be tested on its own: which cell the pointer is over, and how far to scroll a group taller than
/// the popup while the tile is held near the top or bottom.
/// </summary>
public static class TileGridHit
{
    /// <summary>How close to the top or bottom of the scrolled area, in DIPs, the grid starts scrolling.</summary>
    public const double EdgeBand = 32;

    /// <summary>The most the grid scrolls in one step, in DIPs, at the very edge.</summary>
    public const double MaxScrollStep = 14;

    /// <summary>
    /// The index of the tile whose cell holds <paramref name="point"/>; the empty cells after the
    /// last tile count as the last tile. -1 outside the grid, and for a grid with nothing in it.
    /// </summary>
    /// <param name="point">The pointer, relative to the grid's top-left corner.</param>
    /// <param name="gridSize">The grid's laid-out size; its cells divide it evenly.</param>
    /// <param name="columns">The columns the grid uses (<c>EffectiveColumns</c>).</param>
    /// <param name="count">How many tiles there are.</param>
    public static int IndexAt(Point point, Size gridSize, int columns, int count)
    {
        if (count <= 0 || columns <= 0 || gridSize.Width <= 0 || gridSize.Height <= 0
            || point.X < 0 || point.Y < 0 || point.X > gridSize.Width || point.Y > gridSize.Height)
        {
            return -1;
        }

        var rows = (count + columns - 1) / columns;
        var column = Math.Min(columns - 1, (int)(point.X / (gridSize.Width / columns)));
        var row = Math.Min(rows - 1, (int)(point.Y / (gridSize.Height / rows)));
        return Math.Min(count - 1, (row * columns) + column);
    }

    /// <summary>
    /// How far to scroll for a tile held at <paramref name="y"/>: negative to scroll up when it is
    /// within <see cref="EdgeBand"/> of the top (or above it), positive near the bottom, 0 between,
    /// and faster the closer it is to the edge.
    /// </summary>
    /// <param name="y">The pointer, relative to the top of the scrolled area.</param>
    /// <param name="viewportHeight">The height of the scrolled area.</param>
    public static double EdgeScroll(double y, double viewportHeight)
    {
        if (y < EdgeBand)
        {
            return -MaxScrollStep * Math.Min(1, (EdgeBand - y) / EdgeBand);
        }

        if (y > viewportHeight - EdgeBand)
        {
            return MaxScrollStep * Math.Min(1, (y - (viewportHeight - EdgeBand)) / EdgeBand);
        }

        return 0;
    }
}
