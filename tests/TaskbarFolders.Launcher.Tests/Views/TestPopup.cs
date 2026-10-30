using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using TaskbarFolders.TestSupport;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// Builds and shows a real <see cref="PopupWindow"/> for the window tests: a group "Tools" of
/// <c>App 1</c>…<c>App n</c> at paths that do not exist, no icons, parked off any desktop.
/// Call on an STA thread.
/// </summary>
/// <remarks>
/// Shown because an ItemsControl only realises its tiles inside a real layout pass.
/// <c>ShowActivated = false</c> is load-bearing: a popup that is activated is deactivated again
/// as the test tears it down, and a test must not take the focus from whatever window the
/// person running the suite is using.
/// </remarks>
internal static class TestPopup
{
    public static (PopupWindow Window, PopupViewModel ViewModel) Show(
        int appCount = 1,
        int columns = 3,
        bool enableAnimations = false,
        bool launchSucceeds = false,
        ResourceDictionary? theme = null,
        Action<PopupViewModel>? beforeShow = null,
        int iconSize = GroupConfig.DefaultIconSize,
        bool showNames = true,
        int tileSpacing = 0,
        PopupBackdrop? groupBackdrop = null,
        PopupBackdrop settingsBackdrop = PopupBackdrop.Mica)
    {
        var config = new GroupConfig { Id = "g1", GroupName = "Tools", Columns = columns, IconSize = iconSize, ShowNames = showNames, TileSpacing = tileSpacing, Backdrop = groupBackdrop };
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
        launcher.Setup(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>())).Returns(launchSucceeds);

        var viewModel = new PopupViewModel(
            store.Object,
            extractor.Object,
            cache.Object,
            launcher.Object,
            Mock.Of<IGroupSyncService>(),
            new LauncherOptions("g1"));
        viewModel.LoadAsync().GetAwaiter().GetResult();
        beforeShow?.Invoke(viewModel);

        var positionHelper = new Mock<ITaskbarPositionHelper>();
        positionHelper
            .Setup(h => h.ComputePlacement(It.IsAny<Size>(), It.IsAny<PopupPositionPreference>()))
            .Returns(new PopupPlacement(-32000, -32000));

        var window = new PopupWindow(viewModel, positionHelper.Object, new AppSettings { EnableAnimations = enableAnimations, PopupBackdrop = settingsBackdrop });

        // Production merges the theme into Application.Resources; with no Application in the
        // test host, the window's own resources sit on the same lookup path.
        if (theme is not null)
        {
            window.Resources.MergedDictionaries.Add(theme);
        }

        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
        return (window, viewModel);
    }

    /// <summary>
    /// Closes the popup and waits until it really has. With animations on, the first Close() only
    /// starts the fade-out; a test that ends its STA thread then leaves the window alive, and the
    /// next message to it crashes the whole test host (NullReferenceException in
    /// HwndSubclass.SubclassWndProc) — CI aborted the Launcher run on exactly that.
    /// </summary>
    public static void Close(Window window)
    {
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        StaThread.PumpUntil(() => closed, TimeSpan.FromSeconds(2));
    }
}
