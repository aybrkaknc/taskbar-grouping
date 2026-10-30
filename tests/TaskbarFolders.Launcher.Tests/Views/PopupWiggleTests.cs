using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// In edit mode the popup's tiles wiggle, as icons do on a phone, and stop when editing ends.
/// With animations turned off in the settings they stay still. WPF-driven on purpose: what is
/// checked is the running animation on the realised tiles.
/// </summary>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupWiggleTests
{
    private static List<FrameworkElement> TileRoots(Window window)
    {
        var list = (ItemsControl)window.FindName("TileList");
        return Enumerable.Range(0, list.Items.Count)
            .Select(i => (FrameworkElement)VisualTreeHelper.GetChild(list.ItemContainerGenerator.ContainerFromIndex(i), 0))
            .ToList();
    }

    private static bool Wiggles(FrameworkElement tile) =>
        tile.RenderTransform is RotateTransform { HasAnimatedProperties: true };

    private static bool Upright(FrameworkElement tile) =>
        tile.RenderTransform is not RotateTransform rotate || rotate.Angle == 0;

    [Fact]
    public void EditMode_MakesEveryTileWiggle_AndLeavingItStopsThem()
    {
        StaThread.Run(() =>
        {
            var (window, viewModel) = TestPopup.Show(appCount: 4, enableAnimations: true);
            try
            {
                StaThread.PumpUntil(() => window.IsLoaded, TimeSpan.FromSeconds(2));
                TileRoots(window).Should().NotContain(t => Wiggles(t), "tiles are still until editing starts");

                viewModel.ToggleEditCommand.Execute(null);
                StaThread.PumpUntil(() => TileRoots(window).All(Wiggles), TimeSpan.FromSeconds(2));
                TileRoots(window).Should().OnlyContain(t => Wiggles(t));

                viewModel.ToggleEditCommand.Execute(null);
                StaThread.PumpUntil(() => !TileRoots(window).Any(Wiggles), TimeSpan.FromSeconds(2));
                TileRoots(window).Select(t => !Wiggles(t) && Upright(t)).Should().OnlyContain(ok => ok, "every tile is upright again");
            }
            finally
            {
                window.Close();
            }

            return 0;
        });
    }

    [Fact]
    public void NeighbouringTiles_StartInOppositeDirections()
    {
        StaThread.Run(() =>
        {
            var (window, viewModel) = TestPopup.Show(appCount: 2, enableAnimations: true);
            try
            {
                StaThread.PumpUntil(() => window.IsLoaded, TimeSpan.FromSeconds(2));
                viewModel.ToggleEditCommand.Execute(null);
                StaThread.PumpUntil(() => TileRoots(window).All(Wiggles), TimeSpan.FromSeconds(2));

                var directions = TileRoots(window)
                    .Select(t => Math.Sign(((RotateTransform)t.RenderTransform).Angle + 0.0))
                    .ToList();
                StaThread.PumpUntil(() => false, TimeSpan.FromMilliseconds(30));

                // Sampled at the same moment, two neighbours lean opposite ways (or one is
                // crossing upright).
                (directions[0] * directions[1]).Should().BeLessOrEqualTo(0);
            }
            finally
            {
                window.Close();
            }

            return 0;
        });
    }

    [Fact]
    public void WithAnimationsOff_TheTilesStayStillInEditMode()
    {
        StaThread.Run(() =>
        {
            var (window, viewModel) = TestPopup.Show(appCount: 3, enableAnimations: false);
            try
            {
                StaThread.PumpUntil(() => window.IsLoaded, TimeSpan.FromSeconds(2));
                viewModel.ToggleEditCommand.Execute(null);
                StaThread.PumpUntil(() => false, TimeSpan.FromMilliseconds(200));

                TileRoots(window).Should().NotContain(t => Wiggles(t), "the setting turns every animation off");
            }
            finally
            {
                window.Close();
            }

            return 0;
        });
    }
}
