using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Models;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// Covers how <see cref="PopupWindow"/> resizes when the launch-failure strip appears and
/// disappears.
/// </summary>
/// <remarks>
/// These need the real XAML tree, because what broke here twice is a WPF dependency-property
/// rule rather than arithmetic. The strip docks Bottom in a fill-last DockPanel, so a height
/// computed from tile rows alone let it take its space out of the fixed 96 px tiles and clip
/// the last row (issue #24). Measuring the strip on its own to fix that meant writing
/// Visibility from code — which discards a OneWay binding permanently, leaving the strip
/// behind as an empty bar that clipped the tiles again on the next click. A view-model test
/// can reach neither failure.
///
/// WPF objects require an STA thread; xUnit runs MTA, so each test body is marshalled onto a
/// short-lived STA thread. The window is never shown: sizing runs off the LastError
/// PropertyChanged handler wired in the constructor, and Measure needs no HWND.
/// </remarks>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupWindowSizingTests
{
    private const int TilePx = 96;
    private const int PaddingPx = 12;

    /// <summary>Six apps over three columns: two rows of tiles plus the outer padding.</summary>
    private const double TwoRowGridHeight = (2 * TilePx) + (2 * PaddingPx);

    /// <summary>
    /// The window height with no error strip: the grid, its padding and the header row. The
    /// header is measured rather than hard-coded, so a restyled header does not break these
    /// tests while a header that stops being counted still does.
    /// </summary>
    private static double ExpectedHeight(PopupWindow window) =>
        TwoRowGridHeight + ((FrameworkElement)window.FindName("HeaderBar")).DesiredSize.Height;

    [Fact]
    public void LastError_GrowsTheWindowSoTheTilesKeepTheirFullHeight()
    {
        WithPopup(appCount: 6, columns: 3, (window, viewModel, _) =>
        {
            window.Height.Should().Be(ExpectedHeight(window), "no error yet, so the window is header, grid and padding");

            viewModel.LastError = "Could not launch \"Broken App\".";

            window.Height.Should().BeGreaterThan(
                ExpectedHeight(window),
                "the strip docks below the tiles, so its height has to be added to the window "
                + "rather than taken out of the fixed 96 px rows");
        });
    }

    [Fact]
    public void LastError_Cleared_CollapsesTheStripAgainAndReturnsToGridHeight()
    {
        WithPopup(appCount: 6, columns: 3, (window, viewModel, strip) =>
        {
            viewModel.LastError = "Could not launch \"Broken App\".";
            var withStrip = window.Height;

            // LaunchApp clears LastError before every attempt, so this runs on every click
            // that follows a failure — the path that used to leave an empty red bar behind
            // and clip the bottom tile row a second time.
            viewModel.LastError = null;

            strip.Visibility.Should().Be(
                Visibility.Collapsed,
                "clearing LastError has to hide the strip again; a window that shrinks while "
                + "the strip stays visible clips the tiles exactly as issue #24 described");
            window.Height.Should().Be(ExpectedHeight(window), "with no strip the window is header, grid and padding");
            withStrip.Should().BeGreaterThan(window.Height);
        });
    }

    [Fact]
    public void ResizeCycle_LeavesTheStripVisibilityBindingIntact()
    {
        WithPopup(appCount: 6, columns: 3, (_, viewModel, strip) =>
        {
            viewModel.LastError = "Could not launch \"Broken App\".";
            viewModel.LastError = null;
            viewModel.LastError = "Could not launch \"Other App\".";

            BindingOperations.GetBindingExpression(strip, UIElement.VisibilityProperty)
                .Should().NotBeNull(
                    "sizing must never write Visibility on the strip: a local value replaces a "
                    + "OneWay binding for good, ClearValue does not bring it back, and the "
                    + "property then falls back to its default, which is Visible");
            strip.Visibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void AddingATileThatStartsANewRow_GrowsTheWindowByAFullRow()
    {
        // A tile added from the popup used to arrive with its name cut off: the window was
        // resized before the ItemsControl had laid the new tile out, so the new row only got
        // as much height as the old layout had, and the label at the bottom of the tile fell
        // outside the window until the popup was reopened.
        WithPopup(appCount: 3, columns: 3, (window, viewModel, _) =>
        {
            var oneRow = window.Height;

            viewModel.AddAppsAsync([@"C:\does\not\exist\app4.exe"]).GetAwaiter().GetResult();
            StaThread.Pump(TimeSpan.FromMilliseconds(300));

            window.Height.Should().Be(oneRow + TilePx, "the fourth tile opens a second row of 96 px");
        });
    }

    [Fact]
    public void AddingATileToTheSameRow_ShowsItsLabelInsideTheWindow()
    {
        WithPopup(appCount: 1, columns: 3, (window, viewModel, _) =>
        {
            viewModel.AddAppsAsync([@"C:\does\not\exist\Second.exe"]).GetAwaiter().GetResult();
            StaThread.Pump(TimeSpan.FromMilliseconds(300));

            var label = FindDescendants<TextBlock>(window).Single(t => t.Text == "Second");
            label.IsVisible.Should().BeTrue();
            label.ActualHeight.Should().BeGreaterThan(0, "the label must have been laid out");
            var bottom = label.TranslatePoint(new Point(0, label.ActualHeight), window).Y;
            bottom.Should().BeLessOrEqualTo(window.Height, "the label has to be inside the window, not cut off");
        });
    }

    private static System.Collections.Generic.IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    // Issue #32: past eight rows the grid used to be clipped at MaxHeight with nothing to show
    // that more apps existed.
    [Theory]
    [InlineData(30, 3, false)]
    [InlineData(30, 3, true)]   // the strip is where the last row went missing first
    [InlineData(60, 6, false)]  // widest grid: the scroll bar must not squeeze six columns
    public void LargeGroup_ScrollsToEveryTile_InsideMaxHeight(int appCount, int columns, bool withError)
    {
        WithPopup(appCount, columns, (window, viewModel, _) =>
        {
            if (withError)
            {
                viewModel.LastError = "Could not launch \"Broken App\".";
            }

            // Let the layout the real popup gets from its dispatcher catch up with the size.
            StaThread.Pump(TimeSpan.FromMilliseconds(200));
            var rows = (appCount + columns - 1) / columns;
            var scroller = (ScrollViewer)window.FindName("TileScroller");

            // Windows caps every window at the screen height plus its frame
            // (SM_CYMAXTRACK) — 788 on a 1024x768 CI runner — so the ceiling the popup
            // reaches is the lower of the two.
            var ceiling = Math.Min(window.MaxHeight, SystemParameters.MaximumWindowTrackHeight);
            window.ActualHeight.Should().BeApproximately(ceiling, 1, "the popup grows to its ceiling and no further");
            scroller.Should().NotBeNull("tiles past the ceiling have to be reachable");
            scroller.ExtentHeight.Should().BeGreaterOrEqualTo(rows * TilePx, "every row is in the scrollable content");
            scroller.ScrollableHeight.Should().BeGreaterThan(0);
            scroller.ViewportWidth.Should().BeGreaterOrEqualTo(columns * TilePx, "the scroll bar must not cut into the tiles");
        });
    }

    [Fact]
    public void GroupThatFits_ShowsNoScrollBar_AndKeepsItsWidth()
    {
        WithPopup(appCount: 6, columns: 3, (window, _, _) =>
        {
            var scroller = (ScrollViewer)window.FindName("TileScroller");

            scroller.Should().NotBeNull();
            scroller.ComputedVerticalScrollBarVisibility.Should().Be(Visibility.Collapsed);
            window.Width.Should().Be((3 * TilePx) + (2 * PaddingPx));
        });
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(64, true)]
    [InlineData(80, true)]
    [InlineData(48, false)]
    [InlineData(80, false)]
    public void GridSettings_SizeTheTilesAndTheWindow(int iconSize, bool showNames)
    {
        var tile = TileMetrics.Compute(iconSize, showNames);
        WithPopup(appCount: 4, columns: 4, iconSize, showNames, (window, _, _) =>
        {
            var tileRoot = FindDescendants<Button>(window).First(b => b.Name == "TileButton").Parent as FrameworkElement;
            var label = FindDescendants<TextBlock>(window).Single(t => t.Text == "App 1");

            tileRoot!.ActualWidth.Should().Be(tile.Width);
            tileRoot.ActualHeight.Should().Be(tile.Height);
            label.Visibility.Should().Be(showNames ? Visibility.Visible : Visibility.Collapsed);
            window.Width.Should().Be(Math.Max(window.MinWidth, (4 * tile.Width) + (2 * PaddingPx)));
        });
    }

    [Fact]
    public void LargestTiles_AtSixColumns_FitBesideTheScrollBar()
    {
        var tile = TileMetrics.Compute(80, showNames: true);
        WithPopup(appCount: 60, columns: 6, iconSize: 80, showNames: true, (window, _, _) =>
        {
            StaThread.Pump(TimeSpan.FromMilliseconds(200));
            var scroller = (ScrollViewer)window.FindName("TileScroller");

            scroller.ComputedVerticalScrollBarVisibility.Should().Be(Visibility.Visible);
            window.Width.Should().BeLessOrEqualTo(window.MaxWidth);
            scroller.ViewportWidth.Should().BeGreaterOrEqualTo(6 * tile.Width, "MaxWidth must leave room for six of the largest tiles and the bar");
        });
    }

    [Fact]
    public void FewerAppsThanColumns_TakeOnlyTheRoomTheyNeed()
    {
        // Columns is the most a row may hold: two apps under a limit of five are two tiles wide,
        // not five tiles' worth of empty space.
        WithPopup(appCount: 2, columns: 5, (window, _, _) =>
        {
            window.Width.Should().Be(Math.Max(window.MinWidth, (2 * TilePx) + (2 * PaddingPx)),
                "two tiles and their padding, but never narrower than the window's minimum");
        });
    }

    [Fact]
    public void UnavailableGroup_StaysAtMinHeight()
    {
        WithPopup(appCount: 0, columns: 3, (window, viewModel, _) =>
        {
            // An unavailable group shows the centred banner instead of the grid; the window
            // must not collapse around it.
            viewModel.IsUnavailable.Should().BeTrue();

            window.Height.Should().Be(window.MinHeight);
        });
    }

    /// <summary>
    /// Builds a popup on an STA thread, shows it off-screen, runs <paramref name="body"/>
    /// against it and closes it again.
    /// </summary>
    private static void WithPopup(int appCount, int columns, Action<PopupWindow, PopupViewModel, Border> body) =>
        WithPopup(appCount, columns, GroupConfig.DefaultIconSize, showNames: true, body);

    private static void WithPopup(int appCount, int columns, int iconSize, bool showNames, Action<PopupWindow, PopupViewModel, Border> body)
    {
        StaThread.Run(() =>
        {
            var (window, viewModel, strip) = CreatePopup(appCount, columns, iconSize, showNames);
            try
            {
                body(window, viewModel, strip);
            }
            finally
            {
                window.Close();
            }

            return 0;
        });
    }

    private static (PopupWindow window, PopupViewModel viewModel, Border strip) CreatePopup(int appCount, int columns, int iconSize, bool showNames)
    {
        var (window, viewModel) = TestPopup.Show(appCount, columns, iconSize: iconSize, showNames: showNames);
        var strip = window.FindName("ErrorStrip") as Border
            ?? throw new InvalidOperationException("PopupWindow.xaml no longer names the error strip 'ErrorStrip'.");
        return (window, viewModel, strip);
    }
}
