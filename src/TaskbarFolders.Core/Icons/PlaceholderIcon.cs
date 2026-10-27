using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// The generic icon shown for an app that has none of its own: a plain window outline. One
/// shape serves every surface — the popup and the Manager list stroke <see cref="Shape"/> with
/// their theme's subtle foreground, the composite group icon draws <see cref="Render"/> in
/// <see cref="NeutralColor"/>.
/// </summary>
/// <remarks>
/// <see cref="IIconExtractor"/> still returns <c>null</c> for such apps; the placeholder is
/// substituted where an icon is displayed or composed, so callers can tell "no icon" from
/// "icon" (upstream issue #27).
/// </remarks>
[SupportedOSPlatform("windows")]
public static class PlaceholderIcon
{
    /// <summary>Edge length of the square design space <see cref="Shape"/> is drawn in.</summary>
    public const double DesignSize = 24;

    /// <summary>Stroke width in design units; scales with the rendered size.</summary>
    public const double StrokeThickness = 1.5;

    /// <summary>
    /// Stroke colour for the composite group icon. The taskbar follows the system theme rather
    /// than the app's, so this grey is chosen to read on both a light and a dark taskbar.
    /// </summary>
    public static readonly Color NeutralColor = Color.FromRgb(0x8A, 0x8A, 0x8A);

    /// <summary>
    /// The outline in a <see cref="DesignSize"/>-unit square: a rounded window with a title
    /// bar. Frozen, so XAML on any thread can share it.
    /// </summary>
    public static Geometry Shape { get; } = CreateShape();

    /// <summary>Draws the placeholder into a frozen square bitmap.</summary>
    /// <param name="size">Edge length in pixels.</param>
    /// <param name="color">Stroke colour.</param>
    public static BitmapSource Render(int size, Color color)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Size must be positive.");
        }

        var pen = new Pen(new SolidColorBrush(color), StrokeThickness);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.PushTransform(new ScaleTransform(size / DesignSize, size / DesignSize));
            ctx.DrawGeometry(null, pen, Shape);
            ctx.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static GeometryGroup CreateShape()
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(3, 4, 18, 16), 2.5, 2.5));
        group.Children.Add(new LineGeometry(new Point(3, 8.5), new Point(21, 8.5)));
        group.Freeze();
        return group;
    }
}
