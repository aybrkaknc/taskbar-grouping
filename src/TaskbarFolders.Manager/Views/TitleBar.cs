using System.Windows;
using System.Windows.Media;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Manager.Views;

/// <summary>
/// Gives a Manager window's title bar the app's own colour: the theme's window background, so the
/// caption reads as part of the window rather than as a strip of the Windows accent colour. Dark or
/// light follows the theme as before. Call again after a theme change to repaint it.
/// </summary>
public static class TitleBar
{
    private const string WindowBackgroundKey = "WindowBackground";

    /// <summary>Applies the theme's dark/light mode and window-background colour to a title bar.</summary>
    /// <param name="hwnd">The window's handle.</param>
    /// <param name="dark">Whether the theme is dark.</param>
    /// <returns><see langword="true"/> if the colour was applied; the dark mode is applied regardless.</returns>
    public static bool Apply(System.IntPtr hwnd, bool dark)
    {
        _ = WindowBackdrop.TrySetDarkTitleBar(hwnd, dark);

        if (Application.Current?.TryFindResource(WindowBackgroundKey) is not SolidColorBrush colour)
        {
            return false;
        }

        return WindowBackdrop.TrySetCaptionColor(hwnd, colour.Color.R, colour.Color.G, colour.Color.B);
    }
}
