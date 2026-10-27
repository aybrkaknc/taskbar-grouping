using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
/// How the popup closes: every trigger ends in <c>Close()</c>, and <c>OnClosing</c> turns the
/// first request into a short fade when animations are on.
/// </summary>
/// <remarks>
/// The fade test runs a real dispatcher loop with <see cref="Dispatcher.PushFrame"/>: the
/// storyboard, its <c>Completed</c> event and the safety timer all need one, and only a
/// running loop shows that the deferred close actually finishes.
/// </remarks>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupWindowCloseTests
{
    [Fact]
    public void Close_WithAnimationsOff_ClosesAtOnce()
    {
        var closed = OnStaThread(() =>
        {
            var window = CreatePopup(enableAnimations: false);
            var wasClosed = false;
            window.Closed += (_, _) => wasClosed = true;

            window.Close();

            return wasClosed;
        });

        closed.Should().BeTrue("with animations off there is nothing to wait for");
    }

    [Fact]
    public void Close_WithAnimationsOn_FadesFirstAndThenCloses()
    {
        var (closedAtOnce, stillVisible, closedAfterFade, elapsedMs) = OnStaThread(() =>
        {
            var window = CreatePopup(enableAnimations: true);
            var closed = false;
            var frame = new DispatcherFrame();
            window.Closed += (_, _) =>
            {
                closed = true;
                frame.Continue = false;
            };

            var watch = Stopwatch.StartNew();
            window.Close();
            var atOnce = closed;
            var visible = window.IsVisible;

            // Second request while fading: must neither throw nor restart anything.
            window.Close();

            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();

            return (atOnce, visible, closed, watch.ElapsedMilliseconds);
        });

        closedAtOnce.Should().BeFalse("the first close request is replaced by the fade");
        stillVisible.Should().BeTrue("the popup stays on screen while it fades");
        closedAfterFade.Should().BeTrue("the fade, or its safety net, has to finish the close");
        elapsedMs.Should().BeLessThan(1000, "the fade is a fraction of a second, the safety net 300 ms");
    }

    [Fact]
    public void Escape_ClosesThePopup()
    {
        var closed = OnStaThread(() =>
        {
            var window = CreatePopup(enableAnimations: false);
            var wasClosed = false;
            window.Closed += (_, _) => wasClosed = true;

            var source = PresentationSource.FromVisual(window)
                ?? throw new InvalidOperationException("The popup has no presentation source.");
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });

            return wasClosed;
        });

        closed.Should().BeTrue();
    }

    [Fact]
    public void Escape_LeavesEditModeFirst_ThenCloses()
    {
        var (editingAfterFirst, closedAfterFirst, closedAfterSecond) = OnStaThread(() =>
        {
            var window = CreatePopup(enableAnimations: false);
            var viewModel = (PopupViewModel)window.DataContext;
            var closed = false;
            window.Closed += (_, _) => closed = true;
            viewModel.ToggleEditCommand.Execute(null);

            PressEscape(window);
            var editing = viewModel.IsEditing;
            var closedFirst = closed;

            PressEscape(window);
            return (editing, closedFirst, closed);
        });

        editingAfterFirst.Should().BeFalse("the first Escape backs out of edit mode");
        closedAfterFirst.Should().BeFalse("and leaves the popup open");
        closedAfterSecond.Should().BeTrue("the second Escape closes it");
    }

    [Fact]
    public void Pinned_StaysOpenAfterALaunch_AndIgnoresOutsideInteraction_ButEscapeStillCloses()
    {
        var (dismissesWhilePinned, closedByLaunch, dismissesUnpinned, closedByEscape) = OnStaThread(() =>
        {
            var window = CreatePopup(enableAnimations: false, launchSucceeds: true);
            var viewModel = (PopupViewModel)window.DataContext;
            var closed = false;
            window.Closed += (_, _) => closed = true;

            viewModel.TogglePinCommand.Execute(null);
            var dismisses = window.DismissesOnOutsideInteraction;
            viewModel.LaunchAppCommand.Execute(viewModel.Apps[0]);
            var closedLaunch = closed;

            viewModel.TogglePinCommand.Execute(null);
            var dismissesAgain = window.DismissesOnOutsideInteraction;
            viewModel.TogglePinCommand.Execute(null);

            PressEscape(window);
            return (dismisses, closedLaunch, dismissesAgain, closed);
        });

        dismissesWhilePinned.Should().BeFalse("a pinned popup ignores clicks elsewhere and focus loss");
        closedByLaunch.Should().BeFalse("a pinned popup stays open to launch the next app");
        dismissesUnpinned.Should().BeTrue("unpinning restores the normal dismissal");
        closedByEscape.Should().BeTrue("Escape closes a pinned popup");
    }

    [Fact]
    public void Unpinned_ClosesAfterALaunch()
    {
        var closed = OnStaThread(() =>
        {
            var window = CreatePopup(enableAnimations: false, launchSucceeds: true);
            var viewModel = (PopupViewModel)window.DataContext;
            var wasClosed = false;
            window.Closed += (_, _) => wasClosed = true;

            viewModel.LaunchAppCommand.Execute(viewModel.Apps[0]);
            return wasClosed;
        });

        closed.Should().BeTrue();
    }

    private static void PressEscape(Window window)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("The popup has no presentation source.");
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });
    }

    [Theory]
    [InlineData(150, 150, false)] // middle
    [InlineData(100, 100, false)] // top-left corner is inside (inclusive)
    [InlineData(199, 199, false)] // last pixel inside
    [InlineData(200, 150, true)]  // right edge is exclusive
    [InlineData(150, 200, true)]  // bottom edge is exclusive
    [InlineData(99, 150, true)]   // left of the window
    [InlineData(150, 99, true)]   // above the window
    [InlineData(-5, -5, true)]    // another monitor to the left
    public void IsOutside_TreatsTheRectangleLikeAWin32Rect(int x, int y, bool expected)
    {
        OutsideClickWatcher.IsOutside(x, y, left: 100, top: 100, right: 200, bottom: 200)
            .Should().Be(expected);
    }

    private static PopupWindow CreatePopup(bool enableAnimations, bool launchSucceeds = false)
    {
        var config = new GroupConfig { Id = "g1", GroupName = "Tools", Columns = 3 };
        config.Apps.Add(new AppEntry { Name = "App 1", Path = @"C:\does\not\exist\app1.exe" });

        var store = new Mock<IGroupConfigStore>();
        store.Setup(s => s.LoadAsync("g1", It.IsAny<CancellationToken>())).ReturnsAsync(config);

        var extractor = new Mock<IIconExtractor>();
        extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns((BitmapSource?)null);

        var cache = new Mock<IIconCache>();
        BitmapSource? unused;
        cache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out unused)).Returns(false);

        var viewModel = new PopupViewModel(
            store.Object,
            extractor.Object,
            cache.Object,
            Mock.Of<IProcessLauncher>(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>()) == launchSucceeds),
            Mock.Of<IGroupSyncService>(),
            new LauncherOptions("g1"));
        viewModel.LoadAsync().GetAwaiter().GetResult();

        // Off any real desktop, so running the suite never flashes a window.
        var positionHelper = new Mock<ITaskbarPositionHelper>();
        positionHelper
            .Setup(h => h.ComputePlacement(It.IsAny<Size>(), It.IsAny<PopupPositionPreference>()))
            .Returns(new PopupPlacement(-32000, -32000));

        var window = new PopupWindow(
            viewModel,
            positionHelper.Object,
            new AppSettings { EnableAnimations = enableAnimations });

        // ShowActivated=false: see PopupWindowSizingTests for why an activated popup takes the
        // test host down during teardown.
        window.ShowActivated = false;
        window.Show();
        return window;
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
