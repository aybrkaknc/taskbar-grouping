using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
/// Keyboard use of the popup (upstream #26): the first tile holds the focus once the popup
/// is drawn, and the arrow keys move it by <see cref="TileNavigator"/>.
/// </summary>
/// <remarks>
/// The window is shown without activation (see PopupWindowSizingTests for why), so it never
/// receives keyboard focus; what is checked is the logical focus, which is what Enter acts on
/// once the window is active. Keys are raised as routed events on the window.
/// </remarks>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupWindowKeyboardTests
{
    [Fact]
    public void FirstTile_HoldsTheFocus_OnceThePopupIsDrawn()
    {
        WithPopup(appCount: 5, columns: 3, window =>
        {
            window.IndexOfFocusedTile().Should().Be(0, "Enter launches the first app without a click");
            FocusedTileName(window).Should().Be("App 1");
        });
    }

    [Theory]
    [InlineData(Key.Right, 1)]
    [InlineData(Key.Down, 3)]
    [InlineData(Key.Left, 4)]   // wraps to the last of five
    [InlineData(Key.End, 4)]
    public void ArrowKeys_MoveTheFocusBetweenTiles(Key key, int expected)
    {
        WithPopup(appCount: 5, columns: 3, window =>
        {
            var handled = Press(window, key);

            handled.Should().BeTrue("the popup handles the key instead of WPF's own navigation");
            window.IndexOfFocusedTile().Should().Be(expected);
        });
    }

    [Fact]
    public void ArrowKey_FromAHeaderButton_GoesToTheFirstTile()
    {
        WithPopup(appCount: 5, columns: 3, window =>
        {
            var close = (Button)window.FindName("CloseButton")
                ?? throw new InvalidOperationException("PopupWindow.xaml no longer names its close button 'CloseButton'.");
            FocusManager.SetFocusedElement(window, close);
            window.IndexOfFocusedTile().Should().Be(-1);

            Press(window, Key.Down);

            window.IndexOfFocusedTile().Should().Be(0);
        });
    }

    [Fact]
    public void OtherKeys_AreLeftAlone()
    {
        WithPopup(appCount: 5, columns: 3, window =>
        {
            Press(window, Key.A).Should().BeFalse();
            window.IndexOfFocusedTile().Should().Be(0);
        });
    }

    private static bool Press(PopupWindow window, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        window.RaiseEvent(args);
        return args.Handled;
    }

    private static string? FocusedTileName(PopupWindow window) =>
        FocusManager.GetFocusedElement(window) is FrameworkElement { DataContext: PopupAppViewModel app } ? app.Name : null;

    private static void WithPopup(int appCount, int columns, Action<PopupWindow> body)
    {
        OnStaThread(() =>
        {
            var window = CreatePopup(appCount, columns);
            try
            {
                // ContentRendered, then the Loaded-priority focus it queues.
                PumpDispatcher(TimeSpan.FromMilliseconds(300));
                body(window);
            }
            finally
            {
                window.Close();
            }
            return 0;
        });
    }

    private static PopupWindow CreatePopup(int appCount, int columns)
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

        var viewModel = new PopupViewModel(
            store.Object,
            extractor.Object,
            cache.Object,
            Mock.Of<IProcessLauncher>(),
            Mock.Of<IGroupSyncService>(),
            new LauncherOptions("g1"));
        viewModel.LoadAsync().GetAwaiter().GetResult();

        // Off any real desktop, so running the suite never flashes a window.
        var positionHelper = new Mock<ITaskbarPositionHelper>();
        positionHelper
            .Setup(h => h.ComputePlacement(It.IsAny<Size>(), It.IsAny<PopupPositionPreference>()))
            .Returns(new PopupPlacement(-32000, -32000));

        var window = new PopupWindow(viewModel, positionHelper.Object, new AppSettings { EnableAnimations = false });
        window.ShowActivated = false;
        window.Show();
        return window;
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
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
