using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FluentAssertions;
using TaskbarFolders.Manager.Views;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Views;

/// <summary>
/// Once DWM has put Mica behind the Manager window, <see cref="MicaSurfaces.Use"/> must clear
/// everything WPF paints over it: the HWND's own clear colour and the opaque surface brushes,
/// replaced by the theme's translucent tints — by reference, so a theme switch reaches them.
/// WPF-driven on purpose: what is checked is WPF's composition target and resource lookup.
/// </summary>
public sealed class MicaSurfacesTests
{
    [Fact]
    public void Use_OnAWindowWithoutPanes_ClearsTheHwndBackgroundAndTheWindowsOwn()
    {
        // Settings has no sidebar: its content sits straight on the material.
        StaThread.Run(() =>
        {
            var window = new Window
            {
                Background = Brushes.White,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Width = 200,
                Height = 100,
            };
            window.Show();
            try
            {
                MicaSurfaces.Use(window);

                ((HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget.BackgroundColor.Should().Be(Colors.Transparent);
                ((SolidColorBrush)window.Background).Color.A.Should().Be(0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Use_ClearsTheHwndBackground_AndPaintsWithTheThemesTints()
    {
        StaThread.Run(() =>
        {
            var sidebar = new DockPanel();
            var content = new Border();
            var window = new Window
            {
                Background = Brushes.White,
                Content = new Grid { Children = { sidebar, content } },
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Width = 200,
                Height = 100,
            };
            var contentTint = new SolidColorBrush(Color.FromArgb(0x40, 1, 2, 3));
            var sidebarTint = new SolidColorBrush(Color.FromArgb(0x00, 4, 5, 6));
            window.Resources["ContentBackdropTint"] = contentTint;
            window.Resources["SidebarBackdropTint"] = sidebarTint;
            window.Show();
            try
            {
                MicaSurfaces.Use(window, sidebar, content);

                ((HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget.BackgroundColor.Should().Be(Colors.Transparent,
                    "WPF clears the client area to this colour, over the backdrop");
                ((SolidColorBrush)window.Background).Color.A.Should().Be(0, "the window itself paints nothing");
                content.Background.Should().BeSameAs(contentTint);
                sidebar.Background.Should().BeSameAs(sidebarTint);

                // A theme switch replaces the brushes; the surfaces follow.
                var lighter = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                window.Resources["ContentBackdropTint"] = lighter;
                content.Background.Should().BeSameAs(lighter);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
