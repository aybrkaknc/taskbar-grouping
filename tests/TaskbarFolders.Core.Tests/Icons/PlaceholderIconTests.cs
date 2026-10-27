using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

public class PlaceholderIconTests
{
    [Fact]
    public void Shape_IsFrozen_SoWindowsOnAnyThreadCanShareIt()
    {
        PlaceholderIcon.Shape.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void Shape_StaysInsideTheDesignSquare_WithRoomForTheStroke()
    {
        var bounds = PlaceholderIcon.Shape.Bounds;
        var half = PlaceholderIcon.StrokeThickness / 2;

        bounds.Left.Should().BeGreaterThanOrEqualTo(half);
        bounds.Top.Should().BeGreaterThanOrEqualTo(half);
        bounds.Right.Should().BeLessThanOrEqualTo(PlaceholderIcon.DesignSize - half);
        bounds.Bottom.Should().BeLessThanOrEqualTo(PlaceholderIcon.DesignSize - half);
    }

    [Fact]
    public void Render_ProducesAFrozenSquareOfTheRequestedSize()
    {
        var bitmap = PlaceholderIcon.Render(128, PlaceholderIcon.NeutralColor);

        bitmap.IsFrozen.Should().BeTrue();
        bitmap.PixelWidth.Should().Be(128);
        bitmap.PixelHeight.Should().Be(128);
    }

    [Fact]
    public void Render_DrawsTheOutlineInTheGivenColour_AndLeavesTheInsideTransparent()
    {
        var bitmap = PlaceholderIcon.Render(96, Colors.Red);
        var pixels = ReadPixels(bitmap);

        // Left edge of the window outline: x = 3 design units = 12 px at 96 px, mid-height.
        var edge = PixelAt(pixels, bitmap.PixelWidth, 12, 48);
        edge.A.Should().BeGreaterThan(200);
        edge.R.Should().BeGreaterThan(200);
        edge.G.Should().BeLessThan(40);

        PixelAt(pixels, bitmap.PixelWidth, 48, 60).A.Should().Be(0, "the outline is not filled");
        PixelAt(pixels, bitmap.PixelWidth, 1, 1).A.Should().Be(0, "the corner lies outside the outline");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Render_RejectsANonPositiveSize(int size)
    {
        var act = () => PlaceholderIcon.Render(size, Colors.Gray);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static byte[] ReadPixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static Color PixelAt(byte[] pixels, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]);
    }
}
