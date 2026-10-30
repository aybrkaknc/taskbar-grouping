using System.Linq;
using System.Windows.Media;
using FluentAssertions;
using TaskbarFolders.Core.Themes;
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
    [InlineData(1, 48, true, 0, 220 + 2)]
    [InlineData(3, 48, true, 0, 3 * 96 + 26)]
    [InlineData(3, 80, true, 12, 3 * (112 + 12) + 26)]
    [InlineData(2, 32, false, 8, 220 + 2)] // two small tiles: the popup is still 220 wide
    public void NaturalWidth_IsTheGridPlusThePreviewsFrame_NoNarrowerThanThePopup(int columns, int iconSize, bool showNames, int spacing, double expected)
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
    [Theory]
    [InlineData(true, 0x20, 0x20, 0x20)]
    [InlineData(false, 0xF3, 0xF3, 0xF3)]
    public void Off_IsThePopupsOpaquePanel(bool dark, byte r, byte g, byte b)
    {
        var sut = new PopupPreviewViewModel();

        sut.UpdateSurface(PopupBackdrop.Off, PopupTint.None, dark, PopupTints.DefaultAccent);

        sut.Surface.Should().Be(Color.FromRgb(r, g, b), "the popup paints exactly this panel");
    }

    [Theory]
    [InlineData(PopupBackdrop.Mica)]
    [InlineData(PopupBackdrop.MicaAlt)]
    public void Mica_IsLeftClear_SoTheManagersOwnMicaShows(PopupBackdrop backdrop)
    {
        var sut = new PopupPreviewViewModel();

        sut.UpdateSurface(backdrop, PopupTint.None, dark: true, PopupTints.DefaultAccent);

        sut.Surface.A.Should().Be(0);
    }

    [Fact]
    public void Acrylic_IsAFrostedPanel()
    {
        var sut = new PopupPreviewViewModel();

        sut.UpdateSurface(PopupBackdrop.Acrylic, PopupTint.None, dark: true, PopupTints.DefaultAccent);

        sut.Surface.A.Should().BeInRange((byte)1, (byte)254, "Windows blurs what is behind; the preview can only frost it");
    }

    [Theory]
    [InlineData(PopupBackdrop.Off)]
    [InlineData(PopupBackdrop.Mica)]
    public void ATint_IsLaidOverTheSurface_AsThePopupLaysIt(PopupBackdrop backdrop)
    {
        var sut = new PopupPreviewViewModel();
        sut.UpdateSurface(backdrop, PopupTint.None, dark: true, PopupTints.DefaultAccent);
        var plain = sut.Surface;

        sut.UpdateSurface(backdrop, PopupTint.Forest, dark: true, PopupTints.DefaultAccent);

        sut.Surface.Should().Be(PopupTints.Over(PopupTints.ColorFor(PopupTint.Forest, dark: true, PopupTints.DefaultAccent)!.Value, plain));
        sut.SurfaceBrush.Color.Should().Be(sut.Surface);
    }
}
