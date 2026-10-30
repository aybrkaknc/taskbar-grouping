using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// In edit mode only the pencil stays in the popup's header: the add, pin and close buttons fade
/// out (at once when animations are off) and come back when editing ends. They keep their place,
/// so the pencil does not jump. WPF-driven on purpose: what is checked is the realised visibility.
/// </summary>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupEditModeHeaderTests
{
    private static readonly string[] _hidden = ["AddAppsButton", "PinButton", "CloseButton"];

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

                // The pencil moves to where the close button was, the header's right end.
                var header = (FrameworkElement)window.FindName("HeaderBar");
                var close = others.Single(b => b.Name == "CloseButton");
                double RightOf(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), header).X;
                double RightOfSlot(FrameworkElement e) => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(e).Right - e.Margin.Right;
                StaThread.PumpUntil(() => Math.Abs(RightOf(pencil) - RightOfSlot(close)) < 0.5, TimeSpan.FromSeconds(2));
                RightOf(pencil).Should().BeApproximately(RightOfSlot(close), 0.5);

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
