using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarFolders.TestSupport;

/// <summary>Bitmaps for tests that need an icon but not a particular one.</summary>
internal static class TestImages
{
    /// <summary>
    /// A frozen bitmap filled with <paramref name="color"/>. Frozen, so it can be made on a
    /// test thread and handed to code that uses it on another.
    /// </summary>
    public static RenderTargetBitmap Solid(Color color, int width = 32, int height = 32)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
