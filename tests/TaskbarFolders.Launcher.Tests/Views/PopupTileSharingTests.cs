using System.IO;
using System.Windows;
using FluentAssertions;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Views;

/// <summary>
/// The tile's look is defined once, in Core, so the Manager's preview cannot drift from the
/// popup. These pin that the popup takes it from there rather than keeping its own copy.
/// </summary>
public sealed class PopupTileSharingTests
{
    private const string SharedTilesPath = "/TaskbarFolders.Core;component/Themes/PopupTile.xaml";

    [Fact]
    public void TheSharedDictionary_LoadsFromCore_WithTheTileStyleAndContent()
    {
        StaThread.Run(() =>
        {
            // LoadComponent resolves the component path without the pack: scheme, which is not
            // registered until something else in the process has touched WPF's packaging.
            var tiles = (ResourceDictionary)Application.LoadComponent(new System.Uri(SharedTilesPath, System.UriKind.Relative));

            tiles["AppTileButton"].Should().BeOfType<Style>();
            tiles["AppTileFocusVisual"].Should().BeOfType<Style>();
            tiles["PopupTileContent"].Should().BeOfType<DataTemplate>();
            return 0;
        });
    }

    [Fact]
    public void ThePopup_MergesTheSharedTiles_AndDefinesNoTileStyleOfItsOwn()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "TaskbarFolders.Launcher", "Views", "PopupWindow.xaml"));

        xaml.Should().Contain(SharedTilesPath);
        xaml.Should().NotContain("x:Key=\"AppTileButton\"", "a second copy is exactly what drifts from the preview");
        xaml.Should().NotContain("x:Key=\"AppTileFocusVisual\"");
    }
}
