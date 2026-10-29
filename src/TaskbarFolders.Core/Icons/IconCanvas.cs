using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// Undoes the shell's habit with programs that have no large icon: asked for its 256 px image
/// list, it does not enlarge the small icon but returns a 256 × 256 canvas with the icon in the
/// top-left corner. Shown as it is, that is a tiny icon in the corner of its tile.
/// </summary>
internal static class IconCanvas
{
    // The sizes a Windows icon comes in, smallest first.
    private static readonly int[] _nativeSizes = [16, 24, 32, 48, 64, 96, 128, 192];

    // A canvas is only taken for the shell's corner habit when the icon fills less than this part
    // of it in both directions: a large icon with margins (200 of 256) is not one.
    private const double CornerFraction = 0.75;

    // How far from the corner the icon's first opaque pixel may be: an icon with an empty column
    // or row at its edge is still the one drawn from the corner.
    private const int CornerMargin = 8;

    // Smaller canvases are the shell's own sizes, not a jumbo cell with something small in it.
    private const int MinCanvasSide = 128;

    /// <summary>
    /// Cuts a canvas whose only content is a small icon in its top-left corner back to the
    /// smallest native icon size that holds it; anything else is returned as it is.
    /// </summary>
    /// <param name="image">The extracted icon.</param>
    /// <returns>The same instance, or a frozen cropped copy.</returns>
    public static BitmapSource TrimToIcon(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.PixelWidth < MinCanvasSide || image.PixelHeight < MinCanvasSide)
        {
            return image;
        }

        var box = OpaqueBox(image);
        if (box is not { } drawn
            || drawn.Left > CornerMargin
            || drawn.Top > CornerMargin
            || drawn.Left + drawn.Width >= image.PixelWidth * CornerFraction
            || drawn.Top + drawn.Height >= image.PixelHeight * CornerFraction)
        {
            return image;
        }

        // The icon's own size: up to its far edges, since the shell draws it from the corner.
        var extent = Math.Max(drawn.Left + drawn.Width, drawn.Top + drawn.Height);
        var side = Array.Find(_nativeSizes, s => s >= extent);
        if (side <= 0)
        {
            return image;
        }

        var cropped = new CroppedBitmap(image, new System.Windows.Int32Rect(0, 0, side, side));
        cropped.Freeze();
        return cropped;
    }

    private static (int Left, int Top, int Width, int Height)? OpaqueBox(BitmapSource image)
    {
        var converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Alpha above a whisper: the shell leaves faint residue round some icons.
                if (pixels[(((y * width) + x) * 4) + 3] > 8)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
