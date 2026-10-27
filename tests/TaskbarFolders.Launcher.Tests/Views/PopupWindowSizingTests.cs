using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
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
            PumpDispatcher(TimeSpan.FromMilliseconds(300));

            window.Height.Should().Be(oneRow + TilePx, "the fourth tile opens a second row of 96 px");
        });
    }

    [Fact]
    public void AddingATileToTheSameRow_ShowsItsLabelInsideTheWindow()
    {
        WithPopup(appCount: 1, columns: 3, (window, viewModel, _) =>
        {
            viewModel.AddAppsAsync([@"C:\does\not\exist\Second.exe"]).GetAwaiter().GetResult();
            PumpDispatcher(TimeSpan.FromMilliseconds(300));

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

    /// <summary>Runs the dispatcher for a while, so deferred layout and sizing work can happen.</summary>
    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
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
    private static void WithPopup(int appCount, int columns, Action<PopupWindow, PopupViewModel, Border> body)
    {
        OnStaThread(() =>
        {
            var (window, viewModel, strip) = CreatePopup(appCount, columns);
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

    private static (PopupWindow window, PopupViewModel viewModel, Border strip) CreatePopup(
        int appCount,
        int columns)
    {
        var config = new GroupConfig { Id = "g1", GroupName = "Tools", Columns = columns };
        for (var i = 0; i < appCount; i++)
        {
            config.Apps.Add(new AppEntry { Name = $"App {i + 1}", Path = $@"C:\does\not\exist\app{i + 1}.exe" });
        }

        var store = new Mock<IGroupConfigStore>();
        store.Setup(s => s.LoadAsync("g1", It.IsAny<CancellationToken>())).ReturnsAsync(config);

        var extractor = new Mock<IIconExtractor>();
        extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns((BitmapSource?)null);

        var cache = new Mock<IIconCache>();
        BitmapSource? unused;
        cache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out unused)).Returns(false);

        var launcher = new Mock<IProcessLauncher>();
        launcher.Setup(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>())).Returns(false);

        var viewModel = new PopupViewModel(
            store.Object,
            extractor.Object,
            cache.Object,
            launcher.Object,
            Mock.Of<IGroupSyncService>(),
            new LauncherOptions("g1"));
        viewModel.LoadAsync().GetAwaiter().GetResult();

        // Park the popup far off any real desktop: these tests must not flash a window in
        // front of whoever is running them.
        var positionHelper = new Mock<ITaskbarPositionHelper>();
        positionHelper
            .Setup(h => h.ComputePlacement(It.IsAny<Size>(), It.IsAny<PopupPositionPreference>()))
            .Returns(new PopupPlacement(-32000, -32000));

        // Animations off so SourceInitialized takes the SnapToEndState path instead of
        // arming a storyboard that no dispatcher loop here would ever run.
        var window = new PopupWindow(
            viewModel,
            positionHelper.Object,
            new AppSettings { EnableAnimations = false });

        // Shown deliberately. An ItemsControl only realises its containers inside a real
        // layout pass, and without one the tile grid measures to nothing — which is the
        // whole quantity these tests are about. UpdateLayout on a window with no
        // PresentationSource is a no-op, so there is no headless shortcut here.
        //
        // ShowActivated=false is load-bearing, not tidiness: an activated popup gets a
        // Deactivated from the window manager as the test thread tears it down, and
        // OnDeactivated calls Close() on a window that is already closing, which throws out
        // of a WndProc and takes the whole test host with it.
        window.ShowActivated = false;
        window.Show();

        var strip = window.FindName("ErrorStrip") as Border
            ?? throw new InvalidOperationException("PopupWindow.xaml no longer names the error strip 'ErrorStrip'.");

        return (window, viewModel, strip);
    }

    private static T OnStaThread<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("STA test body failed.", failure);
        }

        return result;
    }
}
