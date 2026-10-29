using FluentAssertions;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

public class TileMetricsTests
{
    [Fact]
    public void Default_IsTheTileThePopupHasAlwaysHad()
    {
        // 96 x 96 with a 48 px icon and an 80 px label: a group that never touches the
        // settings must look exactly as before.
        var tile = TileMetrics.Default;

        tile.IconSize.Should().Be(GroupConfig.DefaultIconSize);
        tile.Width.Should().Be(96);
        tile.Height.Should().Be(96);
        tile.LabelMaxWidth.Should().Be(80);
        tile.Should().Be(TileMetrics.Compute(GroupConfig.DefaultIconSize, showNames: true));
    }

    [Theory]
    [InlineData(32, 96, 80)]
    [InlineData(48, 96, 96)]
    [InlineData(64, 96, 112)]
    [InlineData(80, 112, 128)]
    public void WithNames_IsWideEnoughForTheLabel_AndTallEnoughForIconAndLabel(int icon, double width, double height)
    {
        var tile = TileMetrics.Compute(icon, showNames: true);

        tile.Width.Should().Be(width);
        tile.Height.Should().Be(height);
        tile.LabelMaxWidth.Should().Be(width - 16, "the label fits inside the tile's 8 px padding");
    }

    [Theory]
    [InlineData(32, 56)]
    [InlineData(48, 72)]
    [InlineData(64, 88)]
    [InlineData(80, 104)]
    public void WithoutNames_IsASquareAroundTheIcon(int icon, double side)
    {
        var tile = TileMetrics.Compute(icon, showNames: false);

        tile.Width.Should().Be(side);
        tile.Height.Should().Be(side);
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(48, 96)]
    [InlineData(80, 160)]
    public void ExtractPx_IsTwiceTheIconSize_SoItStaysSharpOnScaledDisplays(int icon, int px)
    {
        TileMetrics.Compute(icon, showNames: true).ExtractPx.Should().Be(px);
    }

    [Fact]
    public void WithoutSpacing_EachCellIsJustTheTile()
    {
        var tile = TileMetrics.Default;

        tile.Spacing.Should().Be(0);
        tile.CellWidth.Should().Be(tile.Width);
        tile.CellHeight.Should().Be(tile.Height);
    }

    [Theory]
    [InlineData(48, true, 4)]
    [InlineData(48, true, 12)]
    [InlineData(80, true, 12)]
    [InlineData(32, false, 8)]
    public void Spacing_GrowsTheCell_AndLeavesTheTileAsItIs(int icon, bool showNames, int spacing)
    {
        var bare = TileMetrics.Compute(icon, showNames);

        var spaced = TileMetrics.Compute(icon, showNames, spacing);

        spaced.Width.Should().Be(bare.Width, "the tile itself, its label and its hover box keep their size");
        spaced.Height.Should().Be(bare.Height);
        spaced.LabelMaxWidth.Should().Be(bare.LabelMaxWidth);
        spaced.CellWidth.Should().Be(bare.Width + spacing, "half the gap on each side of the tile");
        spaced.CellHeight.Should().Be(bare.Height + spacing);
        spaced.Spacing.Should().Be(spacing);
    }

    [Fact]
    public void NegativeSpacing_IsRefused()
    {
        var act = () => TileMetrics.Compute(48, showNames: true, spacing: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
