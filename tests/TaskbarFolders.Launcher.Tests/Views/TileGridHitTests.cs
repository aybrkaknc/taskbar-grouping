using System.Windows;
using FluentAssertions;
using TaskbarFolders.Launcher.Views;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// Which cell of the popup's grid the pointer is over while a tile is dragged, and how fast the
/// grid scrolls when the tile is held near its top or bottom. The grid is a UniformGrid, so its
/// cells divide its size evenly.
/// </summary>
public sealed class TileGridHitTests
{
    // Three columns of 100 × 100, seven tiles: rows of 3, 3 and 1.
    private static readonly Size _grid = new(300, 300);

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(150, 50, 1)]
    [InlineData(299, 99, 2)]
    [InlineData(10, 150, 3)]
    [InlineData(250, 250, 6)]   // the empty cells of the last row count as the last tile
    [InlineData(50, 250, 6)]
    public void IndexAt_IsTheCellUnderThePointer(double x, double y, int expected)
    {
        TileGridHit.IndexAt(new Point(x, y), _grid, columns: 3, count: 7).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, -1)]
    [InlineData(301, 50)]
    [InlineData(50, 301)]
    public void IndexAt_IsMinusOne_OutsideTheGrid(double x, double y)
    {
        TileGridHit.IndexAt(new Point(x, y), _grid, columns: 3, count: 7).Should().Be(-1, "a tile dropped there goes back");
    }

    [Fact]
    public void IndexAt_IsMinusOne_ForAnEmptyOrUnlaidGrid()
    {
        TileGridHit.IndexAt(new Point(1, 1), _grid, columns: 3, count: 0).Should().Be(-1);
        TileGridHit.IndexAt(new Point(1, 1), new Size(0, 0), columns: 3, count: 4).Should().Be(-1);
        TileGridHit.IndexAt(new Point(1, 1), _grid, columns: 0, count: 4).Should().Be(-1);
    }

    [Theory]
    [InlineData(5, -1)]          // in the top band: scroll up
    [InlineData(200, 0)]         // in the middle: still
    [InlineData(395, 1)]         // in the bottom band: scroll down
    [InlineData(-20, -1)]        // above the view, still held: keep scrolling up
    [InlineData(450, 1)]
    public void EdgeScroll_MovesTowardsTheEdgeTheTileIsHeldAt(double y, int direction)
    {
        var step = TileGridHit.EdgeScroll(y, viewportHeight: 400);

        Math.Sign(step).Should().Be(direction);
    }

    [Fact]
    public void EdgeScroll_IsFasterTheCloserTheTileIsToTheEdge()
    {
        var near = TileGridHit.EdgeScroll(2, viewportHeight: 400);
        var inside = TileGridHit.EdgeScroll(TileGridHit.EdgeBand - 4, viewportHeight: 400);

        Math.Abs(near).Should().BeGreaterThan(Math.Abs(inside));
        Math.Abs(near).Should().BeLessOrEqualTo(TileGridHit.MaxScrollStep);
    }
}
