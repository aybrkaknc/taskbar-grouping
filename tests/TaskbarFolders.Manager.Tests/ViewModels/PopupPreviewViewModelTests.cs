using System.Linq;
using FluentAssertions;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

/// <summary>What the preview on Appearance shows for a group's apps and grid settings.</summary>
public sealed class PopupPreviewViewModelTests
{
    private static AppEntryViewModel[] Apps(int count) =>
        Enumerable.Range(1, count).Select(i => new AppEntryViewModel(new AppEntry { Name = $"App {i}", Path = $"app{i}.exe" })).ToArray();

    [Fact]
    public void Update_TakesTheTileFromTheSettings_AndTheColumnsTheAppsNeed()
    {
        var sut = new PopupPreviewViewModel();

        sut.Update(Apps(5), columns: 3, iconSize: 64, showNames: false, tileSpacing: 8);

        sut.Tile.Should().Be(TileMetrics.Compute(64, showNames: false, spacing: 8));
        sut.ShowNames.Should().BeFalse();
        sut.EffectiveColumns.Should().Be(3);
        sut.Tiles.Should().HaveCount(5);
        sut.MoreCount.Should().Be(0);
    }

    [Theory]
    [InlineData(3, 48, true, 0, 3 * 96 + 26)]
    [InlineData(3, 80, true, 12, 3 * (112 + 12) + 26)]
    [InlineData(2, 32, false, 8, 2 * (56 + 8) + 26)]
    public void NaturalWidth_IsTheGridPlusThePreviewsFrame(int columns, int iconSize, bool showNames, int spacing, double expected)
    {
        var sut = new PopupPreviewViewModel();

        sut.Update(Apps(6), columns, iconSize, showNames, spacing);

        sut.NaturalWidth.Should().Be(expected);
    }

    [Fact]
    public void FewerAppsThanColumns_UseOnlyAsManyColumns_LikeThePopup()
    {
        var sut = new PopupPreviewViewModel();

        sut.Update(Apps(2), columns: 5, iconSize: 48, showNames: true, tileSpacing: 0);

        sut.EffectiveColumns.Should().Be(2);
    }

    [Fact]
    public void ALargeGroup_ShowsTheFirstTwelve_AndCountsTheRest()
    {
        var apps = Apps(20);
        var sut = new PopupPreviewViewModel();

        sut.Update(apps, columns: 6, iconSize: 48, showNames: true, tileSpacing: 0);

        sut.Tiles.Should().Equal(apps.Take(PopupPreviewViewModel.MaxTiles));
        sut.MoreCount.Should().Be(20 - PopupPreviewViewModel.MaxTiles);
        PopupPreviewViewModel.MaxTiles.Should().Be(12);
    }

    [Fact]
    public void AnEmptyGroup_ShowsSampleTiles_WithThePlaceholderIcon()
    {
        var sut = new PopupPreviewViewModel();

        sut.Update([], columns: 3, iconSize: 48, showNames: true, tileSpacing: 0);

        sut.Tiles.Should().HaveCount(3);
        sut.Tiles.Should().OnlyContain(t => t.IconMissing, "there are no icons to show yet");
        sut.IsSample.Should().BeTrue();
        sut.EffectiveColumns.Should().Be(3);
    }

    [Fact]
    public void AppEntryViewModel_IsMissingItsIcon_UntilOneIsSet()
    {
        var app = Apps(1)[0];

        app.IconMissing.Should().BeTrue();
        app.Icon = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
        app.IconMissing.Should().BeFalse();
    }
}
