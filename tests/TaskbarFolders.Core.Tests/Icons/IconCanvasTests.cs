using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

/// <summary>
/// The shell's 256 px image list does not enlarge a program that has no large icon: it hands back
/// a 256 × 256 canvas with the small icon drawn in its top-left corner. Shown as it is, that
/// reads as a tiny icon. These tests pin how such a canvas is cut back to the icon.
/// </summary>
public sealed class IconCanvasTests
{
    /// <summary>A canvas with an opaque block of <paramref name="drawnWidth"/> × <paramref name="drawnHeight"/> at (x, y).</summary>
    private static BitmapSource Canvas(int side, int drawnWidth, int drawnHeight, int x = 0, int y = 0)
    {
        var stride = side * 4;
        var pixels = new byte[stride * side];
        for (var row = y; row < y + drawnHeight; row++)
        {
            for (var col = x; col < x + drawnWidth; col++)
            {
                var i = (row * stride) + (col * 4);
                pixels[i] = 200;
                pixels[i + 1] = 100;
                pixels[i + 2] = 50;
                pixels[i + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    [Theory]
    [InlineData(48, 48, 48)]
    [InlineData(45, 48, 48)]     // CardLock.exe: 45 × 48 drawn, a 48 px icon
    [InlineData(32, 32, 32)]
    [InlineData(31, 20, 32)]
    [InlineData(100, 100, 128)]
    public void ASmallIconInTheTopLeftCorner_IsCutBackToItsNativeSize(int drawnWidth, int drawnHeight, int expectedSide)
    {
        var result = IconCanvas.TrimToIcon(Canvas(256, drawnWidth, drawnHeight));

        result.PixelWidth.Should().Be(expectedSide);
        result.PixelHeight.Should().Be(expectedSide);
        result.IsFrozen.Should().BeTrue("the result crosses threads on its way to the UI");
    }

    [Fact]
    public void AnIconWithAnEmptyEdgeRowAndColumn_IsStillTheOneDrawnFromTheCorner()
    {
        // Opaque from (2, 3) to (42, 42): a 48 px icon whose first column and row are empty.
        var result = IconCanvas.TrimToIcon(Canvas(256, 41, 40, x: 2, y: 3));

        result.PixelWidth.Should().Be(48);
        result.PixelHeight.Should().Be(48);
    }

    [Fact]
    public void ACanvasTheIconFills_IsLeftAlone()
    {
        var full = Canvas(256, 256, 256);

        IconCanvas.TrimToIcon(full).Should().BeSameAs(full);
    }

    [Fact]
    public void AnIconDrawnWellInsideTheCanvas_IsLeftAlone()
    {
        // 200 of 256 is a large icon with margins, not a small one in a corner.
        var large = Canvas(256, 200, 200);

        IconCanvas.TrimToIcon(large).Should().BeSameAs(large);
    }

    [Fact]
    public void ASmallIconNotInTheCorner_IsLeftAlone()
    {
        // Centred content is somebody's deliberate design; only the shell's corner habit is undone.
        var centred = Canvas(256, 48, 48, x: 104, y: 104);

        IconCanvas.TrimToIcon(centred).Should().BeSameAs(centred);
    }

    [Fact]
    public void ABlankCanvas_IsLeftAlone()
    {
        var blank = Canvas(256, 0, 0);

        IconCanvas.TrimToIcon(blank).Should().BeSameAs(blank);
    }

    [Fact]
    public void ASmallBitmap_IsLeftAlone()
    {
        // Only the large canvases are the problem; a 48 px icon at 48 px is just the icon.
        var small = Canvas(48, 40, 40);

        IconCanvas.TrimToIcon(small).Should().BeSameAs(small);
    }
}
