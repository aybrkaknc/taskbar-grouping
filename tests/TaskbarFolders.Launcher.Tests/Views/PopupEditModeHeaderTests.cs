using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// In edit mode the popup's header holds the pencil and the max-per-row number: the add, pin and
/// close buttons fade out (at once when animations are off) and come back when editing ends. They
/// keep their place, so the pencil does not jump. WPF-driven on purpose: what is checked is the
/// realised visibility and position.
/// </summary>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupEditModeHeaderTests
{
    private static readonly string[] _hidden = ["AddAppsButton", "PinButton", "CloseButton"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditMode_ShowsTheMaxPerRowNumber_NextToThePencil_AndItWidensThePopup(bool animations)
    {
        StaThread.Run(() =>
        {
            var (window, viewModel) = TestPopup.Show(appCount: 6, columns: 3, enableAnimations: animations);
            try
            {
                StaThread.PumpUntil(() => window.IsLoaded, TimeSpan.FromSeconds(2));
                var number = (Button)window.FindName("ColumnsButton");
                var pencil = (Button)window.FindName("EditButton");
                var header = (FrameworkElement)window.FindName("HeaderBar");
                number.Visibility.Should().Be(Visibility.Collapsed, "it belongs to edit mode");

                viewModel.ToggleEditCommand.Execute(null);
                double Left(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), header).X;
                double Right(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), header).X;
                var gap = ((Button)window.FindName("PinButton")).Margin.Right;
                StaThread.PumpUntil(
                    () => number.Visibility == Visibility.Visible && number.Opacity == 1 && Math.Abs(Left(number) - Right(pencil) - gap) < 0.5,
                    TimeSpan.FromSeconds(2));

                number.Visibility.Should().Be(Visibility.Visible);
                number.IsHitTestVisible.Should().BeTrue();
                (Left(number) - Right(pencil)).Should().BeApproximately(gap, 0.5, "it sits right of the pencil");
                Right(number).Should().BeApproximately(header.ActualWidth, 0.5, "it takes the close button's place at the end");
                number.BorderThickness.Left.Should().BeGreaterThan(0, "it is framed, so it reads as a box to click");
                number.Content.Should().Be(3);

                var before = window.Width;
                number.Command.Execute(null);
                StaThread.PumpUntil(() => window.Width > before, TimeSpan.FromSeconds(2));
                window.Width.Should().BeGreaterThan(before, "four tiles to a row make a wider popup");
                number.Content.Should().Be(4);

                // The change is animated: the number pops, the tiles glide to their new cells.
                var tiles = (ItemsControl)window.FindName("TileList");
                var moved = Enumerable.Range(0, tiles.Items.Count)
                    .Select(i => tiles.ItemContainerGenerator.ContainerFromIndex(i))
                    .OfType<UIElement>()
                    .Count(t => t.RenderTransform is System.Windows.Media.TranslateTransform { HasAnimatedProperties: true });
                var popped = number.RenderTransform is System.Windows.Media.ScaleTransform { HasAnimatedProperties: true };
                if (animations)
                {
                    popped.Should().BeTrue("the number pops when it changes");
                    moved.Should().BeGreaterThan(0, "tiles that change cell glide there");
                }
                else
                {
                    popped.Should().BeFalse();
                    moved.Should().Be(0, "with animations off the tiles jump");
                }

                viewModel.ToggleEditCommand.Execute(null);
                StaThread.PumpUntil(() => number.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(2));
                number.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                TestPopup.Close(window);
            }

            return 0;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditMode_HidesEveryHeaderButtonButThePencil_AndBringsThemBack(bool animations)
    {
        StaThread.Run(() =>
        {
            var (window, viewModel) = TestPopup.Show(appCount: 3, enableAnimations: animations);
            try
            {
                StaThread.PumpUntil(() => window.IsLoaded, TimeSpan.FromSeconds(2));
                var pencil = (Button)window.FindName("EditButton");
                var others = _hidden.Select(n => (Button)window.FindName(n)).ToList();

                viewModel.ToggleEditCommand.Execute(null);
                StaThread.PumpUntil(() => others.All(b => b.Visibility == Visibility.Hidden), TimeSpan.FromSeconds(2));

                others.Should().OnlyContain(b => b.Visibility == Visibility.Hidden && !b.IsHitTestVisible,
                    "they keep their place but can no longer be seen or clicked");
                pencil.Visibility.Should().Be(Visibility.Visible);

                // The pencil moves to where the pin button was; the max-per-row number takes the
                // close button's place, at the header's right end.
                var header = (FrameworkElement)window.FindName("HeaderBar");
                var pin = others.Single(b => b.Name == "PinButton");
                double RightOf(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), header).X;
                double RightOfSlot(FrameworkElement e) => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(e).Right - e.Margin.Right;
                StaThread.PumpUntil(() => Math.Abs(RightOf(pencil) - RightOfSlot(pin)) < 0.5, TimeSpan.FromSeconds(2));
                RightOf(pencil).Should().BeApproximately(RightOfSlot(pin), 0.5);

                viewModel.ToggleEditCommand.Execute(null);
                StaThread.PumpUntil(
                    () => others.All(b => b.Visibility == Visibility.Visible && b.Opacity == 1) && Math.Abs(RightOf(pencil) - RightOfSlot(pencil)) < 0.5,
                    TimeSpan.FromSeconds(2));

                others.Should().OnlyContain(b => b.Visibility == Visibility.Visible && b.IsHitTestVisible && b.Opacity == 1);
                RightOf(pencil).Should().BeApproximately(RightOfSlot(pencil), 0.5, "the pencil goes back to its own place");
            }
            finally
            {
                TestPopup.Close(window);
            }

            return 0;
        });
    }
}
