using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

public class CompositeIconGeneratorTests
{
    private static RenderTargetBitmap SolidIcon(Color color, int size = 64) => TestImages.Solid(color, size, size);

    // --- ComputeTileRects geometry --------------------------------------------------

    [Fact]
    public void ComputeTileRects_OneIcon_FillsEightyPercentCentred()
    {
        var rects = CompositeIconGenerator.ComputeTileRects(1, 256);

        rects.Should().HaveCount(1);
        rects[0].Width.Should().BeApproximately(256 * 0.84, 0.01);
        rects[0].Height.Should().BeApproximately(256 * 0.84, 0.01);
        // Centred: top-left offset equals (256 - 256*0.84) / 2 = 256 * 0.08
        rects[0].Left.Should().BeApproximately(256 * 0.08, 0.01);
        rects[0].Top.Should().BeApproximately(256 * 0.08, 0.01);
    }

    [Fact]
    public void ComputeTileRects_TwoIcons_AreSideBySideAndVerticallyCentred()
    {
        var rects = CompositeIconGenerator.ComputeTileRects(2, 256);

        rects.Should().HaveCount(2);
        rects[0].Width.Should().BeApproximately(rects[1].Width, 0.01);
        rects[0].Height.Should().BeApproximately(rects[1].Height, 0.01);
        rects[0].Top.Should().BeApproximately(rects[1].Top, 0.01);

        // Horizontally: left tile starts at padding, right tile starts after gap
        var padding = 256 * CompositeIconGenerator.PaddingFraction;
        rects[0].Left.Should().BeApproximately(padding, 0.01);
        rects[1].Left.Should().BeGreaterThan(rects[0].Right);

        // Both tiles vertically centred — distance from top equals distance to bottom
        var topGap = rects[0].Top;
        var bottomGap = 256 - rects[0].Bottom;
        topGap.Should().BeApproximately(bottomGap, 0.01);
    }

    [Fact]
    public void ComputeTileRects_ThreeIcons_IosLayout_HasBottomTileHorizontallyCentred()
    {
        var rects = CompositeIconGenerator.ComputeTileRects(3, 256);

        rects.Should().HaveCount(3);
        // All three tiles the same size
        rects[0].Width.Should().BeApproximately(rects[1].Width, 0.01);
        rects[1].Width.Should().BeApproximately(rects[2].Width, 0.01);

        // Top two on the same row
        rects[0].Top.Should().BeApproximately(rects[1].Top, 0.01);

        // Bottom tile below the top row
        rects[2].Top.Should().BeGreaterThan(rects[0].Bottom);

        // Bottom tile horizontally centred relative to the composite
        var bottomCentre = (rects[2].Left + rects[2].Right) / 2;
        bottomCentre.Should().BeApproximately(128, 0.5);
    }

    [Fact]
    public void ComputeTileRects_FourIcons_FormsTwoByTwoGrid()
    {
        var rects = CompositeIconGenerator.ComputeTileRects(4, 256);

        rects.Should().HaveCount(4);
        // Row 0 shares Top, Row 1 shares Top
        rects[0].Top.Should().BeApproximately(rects[1].Top, 0.01);
        rects[2].Top.Should().BeApproximately(rects[3].Top, 0.01);
        // Column 0 shares Left, Column 1 shares Left
        rects[0].Left.Should().BeApproximately(rects[2].Left, 0.01);
        rects[1].Left.Should().BeApproximately(rects[3].Left, 0.01);
        // Symmetry: padding on top = padding on bottom (within rounding)
        var topPad = rects[0].Top;
        var bottomPad = 256 - rects[3].Bottom;
        topPad.Should().BeApproximately(bottomPad, 0.01);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void ComputeTileRects_ThrowsForOutOfRangeCount(int count)
    {
        var act = () => CompositeIconGenerator.ComputeTileRects(count, 256);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ComputeTileRects_ThrowsForNonPositiveOutputSize(int size)
    {
        var act = () => CompositeIconGenerator.ComputeTileRects(1, size);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // --- GenerateComposite end-to-end -----------------------------------------------

    [Fact]
    public void GenerateComposite_ReturnsFrozenBitmapWithRequestedSize()
    {
        var sut = new CompositeIconGenerator();
        var icons = new[] { SolidIcon(Colors.Red), SolidIcon(Colors.Blue) };

        var result = sut.GenerateComposite(icons, 128);

        result.Should().NotBeNull();
        result.PixelWidth.Should().Be(128);
        result.PixelHeight.Should().Be(128);
        result.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void GenerateComposite_CapsAtFourIcons_WhenMoreSupplied()
    {
        var sut = new CompositeIconGenerator();
        var icons = new List<BitmapSource>
        {
            SolidIcon(Colors.Red),
            SolidIcon(Colors.Green),
            SolidIcon(Colors.Blue),
            SolidIcon(Colors.Yellow),
            SolidIcon(Colors.Magenta),
            SolidIcon(Colors.Cyan),
        };

        // Smoke: rendering five+ icons must not throw — the generator uses only the first four.
        var act = () => sut.GenerateComposite(icons);

        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateComposite_ThrowsForEmptyIconList()
    {
        var sut = new CompositeIconGenerator();

        var act = () => sut.GenerateComposite([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateComposite_ThrowsForNonPositiveOutputSize()
    {
        var sut = new CompositeIconGenerator();

        var act = () => sut.GenerateComposite(new[] { SolidIcon(Colors.Red) }, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GenerateComposite_PixelDataIsNonTransparent_WhereTilesShouldBe()
    {
        // Sanity check that something was actually drawn: read the centre pixel of each
        // tile rect and assert non-zero alpha.
        var sut = new CompositeIconGenerator();
        var icons = new[]
        {
            SolidIcon(Colors.Red),
            SolidIcon(Colors.Green),
            SolidIcon(Colors.Blue),
            SolidIcon(Colors.Yellow),
        };

        var composite = sut.GenerateComposite(icons, 256);
        var rects = CompositeIconGenerator.ComputeTileRects(4, 256);

        var pixels = new byte[composite.PixelWidth * composite.PixelHeight * 4];
        composite.CopyPixels(pixels, composite.PixelWidth * 4, 0);

        foreach (var rect in rects)
        {
            var cx = (int)(rect.Left + rect.Width / 2);
            var cy = (int)(rect.Top + rect.Height / 2);
            var alphaIndex = (cy * composite.PixelWidth + cx) * 4 + 3;
            pixels[alphaIndex].Should().BeGreaterThan(0, $"centre of tile {rect} should be opaque");
        }
    }
}
