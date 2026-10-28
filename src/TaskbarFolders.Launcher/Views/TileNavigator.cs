using System;

namespace TaskbarFolders.Launcher.Views;

/// <summary>A key that moves the focus through the popup's tiles.</summary>
public enum TileMove
{
    /// <summary>The previous tile; from the first, the last.</summary>
    Left,

    /// <summary>The next tile; from the last, the first.</summary>
    Right,

    /// <summary>The tile above; from the top row, the bottom one in the same column.</summary>
    Up,

    /// <summary>The tile below; from the bottom row, the top one in the same column.</summary>
    Down,

    /// <summary>The first tile.</summary>
    Home,

    /// <summary>The last tile.</summary>
    End,
}

/// <summary>
/// Where a key moves the focus in the popup's grid, which fills row by row and whose last row
/// may be short. Kept free of WPF so the rules can be tested on their own.
/// </summary>
public static class TileNavigator
{
    /// <summary>The index the focus moves to.</summary>
    /// <param name="index">Index of the focused tile.</param>
    /// <param name="count">Number of tiles; at least one.</param>
    /// <param name="columns">Columns of the grid; at least one.</param>
    /// <param name="move">The key pressed.</param>
    public static int Move(int index, int count, int columns, TileMove move)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        index = Math.Clamp(index, 0, count - 1);

        var lastRow = (count - 1) / columns;
        var row = index / columns;
        var column = index % columns;

        return move switch
        {
            // Left and Right read the grid as one line, so they carry over row ends.
            TileMove.Left => index == 0 ? count - 1 : index - 1,
            TileMove.Right => index == count - 1 ? 0 : index + 1,
            TileMove.Up => row > 0 ? index - columns : InColumn(lastRow, column, count, columns),
            // Into a short last row that has no tile in this column: its last tile.
            TileMove.Down => row < lastRow ? Math.Min(index + columns, count - 1) : column,
            TileMove.Home => 0,
            TileMove.End => count - 1,
            _ => index,
        };
    }

    // The tile in the given row and column, or the row above's when the row is too short.
    private static int InColumn(int row, int column, int count, int columns)
    {
        var candidate = row * columns + column;
        return candidate < count ? candidate : candidate - columns;
    }
}
