using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Manager.Views;

/// <summary>
/// Puts Mica behind a Manager window and clears what WPF paints over it: the HWND's own clear
/// colour and the window's opaque background. A window with panes gives them the theme's
/// translucent tints (<c>SidebarBackdropTint</c>, <c>ContentBackdropTint</c>) by resource
/// reference, so a theme switch reaches them. Every Manager window goes through here, so the
/// app wears one material.
/// </summary>
/// <remarks>
/// <see cref="Use(Window)"/> is only for a window where DWM accepted the backdrop and the frame
/// was extended into the client area — which is what <see cref="TryApply"/> checks: on an older
/// Windows the opaque surface brushes stay, since a transparent client area there renders black.
/// </remarks>
public static class MicaSurfaces
{
    /// <summary>
    /// Asks DWM for Mica behind the whole of <paramref name="window"/> and, if it agreed, clears
    /// the window's own background with <see cref="Use(Window)"/>.
    /// </summary>
    /// <param name="window">A window whose HWND exists (from <c>SourceInitialized</c> on).</param>
    /// <returns><see langword="true"/> if Mica is behind the window; panes may then be tinted.</returns>
    public static bool TryApply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = new WindowInteropHelper(window).Handle;
        if (!WindowBackdrop.TryApply(hwnd, WindowBackdropKind.Mica)
            || !WindowBackdrop.TryExtendFrameIntoClientArea(hwnd))
        {
            return false;
        }

        Use(window);
        return true;
    }

    /// <summary>Lets the backdrop show through a window without panes, such as Settings.</summary>
    /// <param name="window">A window whose HWND exists (after <c>SourceInitialized</c>).</param>
    public static void Use(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // WPF clears the client area to this colour before drawing; any other colour covers
        // the backdrop, whatever the brushes above it are.
        if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }

        window.Background = Brushes.Transparent;
    }

    /// <summary>Lets the backdrop show through a window with a sidebar and a content area.</summary>
    /// <param name="window">A window whose HWND exists (after <c>SourceInitialized</c>).</param>
    /// <param name="sidebar">The navigation pane, laid straight on the material.</param>
    /// <param name="content">The surface behind the content area, given the layer tint.</param>
    public static void Use(Window window, Panel sidebar, Border content)
    {
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(content);

        Use(window);
        sidebar.SetResourceReference(Panel.BackgroundProperty, "SidebarBackdropTint");
        content.SetResourceReference(Border.BackgroundProperty, "ContentBackdropTint");
    }
}
