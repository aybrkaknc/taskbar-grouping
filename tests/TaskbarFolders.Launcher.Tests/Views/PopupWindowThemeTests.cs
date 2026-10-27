using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
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
/// Checks the colours the popup actually paints with, against the real theme dictionaries.
/// </summary>
/// <remarks>
/// The app tiles are <see cref="Button"/>s, and WPF's built-in Button theme style sets
/// <see cref="Control.Foreground"/> to the system control-text colour (black). A theme-style
/// setter outranks property inheritance, so the theme's <c>ForegroundDefault</c> set on the
/// popup root never reached the labels inside the tiles: they rendered black in the dark
/// theme as well, over a black halo. The same precedence rule hid Manager text until v0.4.7.
/// Only the realised visual tree shows the effective value, so these tests build the real
/// popup, as <see cref="PopupWindowSizingTests"/> does, on an STA thread.
/// </remarks>
[Collection(PopupWindowTestGroup.Name)]
public sealed class PopupWindowThemeTests
{
    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void TileLabels_UseTheThemeForeground(string themeFile)
    {
        var (label, expected) = OnStaThread(() =>
        {
            var theme = LoadTheme(themeFile);
            var window = CreatePopup(theme);
            try
            {
                var text = FindDescendants<TextBlock>(window).First(t => t.Text == "App 1");
                return (((SolidColorBrush)text.Foreground).Color, ((SolidColorBrush)theme["ForegroundDefault"]).Color);
            }
            finally
            {
                window.Close();
            }
        });

        label.Should().Be(expected, $"the tile labels must take ForegroundDefault from {themeFile}");
    }

    private static ResourceDictionary LoadTheme(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(
            FindRepoRoot(), "src", "TaskbarFolders.Launcher", "Themes", fileName));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static PopupWindow CreatePopup(ResourceDictionary theme)
    {
        var config = new GroupConfig { Id = "g1", GroupName = "Tools", Columns = 3 };
        config.Apps.Add(new AppEntry { Name = "App 1", Path = @"C:\does\not\exist\app1.exe" });
        config.Apps.Add(new AppEntry { Name = "App 2", Path = @"C:\does\not\exist\app2.exe" });

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
            new AppSettings { EnableAnimations = false });

        // Production merges the theme into Application.Resources; with no Application in
        // the test host, the window's own resources sit on the same lookup path.
        window.Resources.MergedDictionaries.Add(theme);

        // Shown because an ItemsControl only realises its tiles inside a real layout pass.
        // ShowActivated=false: see PopupWindowSizingTests for why an activated popup takes
        // the test host down during teardown.
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static System.Collections.Generic.IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskbarFolders.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
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
