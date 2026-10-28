using System;
using FluentAssertions;
using TaskbarFolders.Launcher.Views;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// The grid used throughout: 8 tiles in 3 columns, the last row short.
/// <code>
/// 0 1 2
/// 3 4 5
/// 6 7
/// </code>
/// </summary>
public class TileNavigatorTests
{
    private const int Count = 8;
    private const int Columns = 3;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]   // a row end carries over to the next row
    [InlineData(7, 0)]   // the last tile wraps to the first
    public void Right_MovesAlongTheGridAsOneLine(int from, int to)
    {
        TileNavigator.Move(from, Count, Columns, TileMove.Right).Should().Be(to);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 2)]   // a row start carries back to the previous row
    [InlineData(0, 7)]   // the first tile wraps to the last
    public void Left_MovesAlongTheGridAsOneLine(int from, int to)
    {
        TileNavigator.Move(from, Count, Columns, TileMove.Left).Should().Be(to);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 7)]
    [InlineData(5, 7)]   // nothing below in the short last row: its last tile
    [InlineData(6, 0)]   // from the bottom row: the top of the same column
    [InlineData(7, 1)]
    public void Down_KeepsTheColumn(int from, int to)
    {
        TileNavigator.Move(from, Count, Columns, TileMove.Down).Should().Be(to);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(7, 4)]
    [InlineData(1, 7)]   // from the top row: the bottom of the same column
    [InlineData(2, 5)]   // the short last row has no third column, so the row above
    public void Up_KeepsTheColumn(int from, int to)
    {
        TileNavigator.Move(from, Count, Columns, TileMove.Up).Should().Be(to);
    }

    [Fact]
    public void HomeAndEnd_GoToTheFirstAndLastTile()
    {
        TileNavigator.Move(4, Count, Columns, TileMove.Home).Should().Be(0);
        TileNavigator.Move(4, Count, Columns, TileMove.End).Should().Be(7);
    }

    [Theory]
    [InlineData(TileMove.Left)]
    [InlineData(TileMove.Right)]
    [InlineData(TileMove.Up)]
    [InlineData(TileMove.Down)]
    public void ASingleTile_KeepsTheFocus(TileMove move)
    {
        TileNavigator.Move(0, 1, Columns, move).Should().Be(0);
    }

    [Fact]
    public void AFullGrid_WrapsDownToTheTop()
    {
        // 6 tiles, 3 columns: two full rows.
        TileNavigator.Move(5, 6, 3, TileMove.Down).Should().Be(2);
        TileNavigator.Move(2, 6, 3, TileMove.Up).Should().Be(5);
    }

    [Fact]
    public void RejectsAnEmptyGrid()
    {
        var act = () => TileNavigator.Move(0, 0, Columns, TileMove.Right);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
