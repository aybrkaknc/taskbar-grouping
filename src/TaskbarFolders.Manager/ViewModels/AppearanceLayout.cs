using System;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// How wide the Manager's window has to be for the preview on Appearance to stay readable. Kept
/// free of WPF so the arithmetic is tested on its own; the window asks it after each change and
/// grows to the answer, and never shrinks by itself.
/// </summary>
public static class AppearanceLayout
{
    /// <summary>
    /// The smallest scale the preview is drawn at before the window makes room: the preview
    /// scales down to fit its column, and below this its labels stop being legible.
    /// </summary>
    public const double MinPreviewScale = 0.75;

    /// <summary>
    /// The window width at which the preview, at its natural size scaled by
    /// <see cref="MinPreviewScale"/>, fits its column.
    /// </summary>
    /// <param name="currentWidth">The window's width now.</param>
    /// <param name="previewColumnWidth">The width the preview's column has now.</param>
    /// <param name="previewNaturalWidth">The width the preview wants at full size.</param>
    public static double RequiredWindowWidth(double currentWidth, double previewColumnWidth, double previewNaturalWidth) =>
        currentWidth + Math.Max(0, (previewNaturalWidth * MinPreviewScale) - previewColumnWidth);

    /// <summary>The width to give the window: at least what it has, at most the work area.</summary>
    /// <param name="currentWidth">The window's width now.</param>
    /// <param name="requiredWidth">What <see cref="RequiredWindowWidth"/> asked for.</param>
    /// <param name="workAreaWidth">The width of the screen's work area.</param>
    public static double Target(double currentWidth, double requiredWidth, double workAreaWidth) =>
        Math.Max(currentWidth, Math.Min(requiredWidth, workAreaWidth));

    /// <summary>The left edge that keeps a window of <paramref name="width"/> inside the work area.</summary>
    /// <param name="left">The window's left edge now.</param>
    /// <param name="width">The window's width after growing.</param>
    /// <param name="workAreaLeft">The work area's left edge.</param>
    /// <param name="workAreaWidth">The work area's width.</param>
    public static double Left(double left, double width, double workAreaLeft, double workAreaWidth) =>
        Math.Max(workAreaLeft, Math.Min(left, workAreaLeft + workAreaWidth - width));
}
