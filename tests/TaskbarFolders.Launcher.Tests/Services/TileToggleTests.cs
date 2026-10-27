using System;
using System.Threading.Tasks;
using System.Windows;
using FluentAssertions;
using TaskbarFolders.Launcher.Services;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Services;

public sealed class TileToggleTests
{
    // Machine-wide names: a fresh group id per test keeps runs from seeing each other.
    private static string NewGroupId() => "toggle-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public void TryConsume_IsFalse_WhenNoPopupWasClosedFromItsTile()
    {
        TileToggle.TryConsume(NewGroupId()).Should().BeFalse("a normal tile click must open the popup");
    }

    [Fact]
    public void TryConsume_FindsTheMarker_OfTheSameGroupOnly()
    {
        var id = NewGroupId();
        using var marker = TileToggle.MarkDismissed(id);

        TileToggle.TryConsume(NewGroupId()).Should().BeFalse("groups do not share markers");
        TileToggle.TryConsume(id).Should().BeTrue();
    }

    [Fact]
    public void TryConsume_IsFalse_OnceTheDismissedPopupHasExited()
    {
        var id = NewGroupId();
        TileToggle.MarkDismissed(id).Dispose();

        TileToggle.TryConsume(id).Should().BeFalse("the next click after the toggle opens the popup again");
    }

    [Fact]
    public async Task WaitForNextLaunch_EndsAsSoonAsTheMarkerIsConsumed()
    {
        var id = NewGroupId();
        using var marker = TileToggle.MarkDismissed(id);
        var waiting = Task.Run(() => marker.WaitForNextLaunch(TimeSpan.FromSeconds(10)));

        TileToggle.TryConsume(id);

        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    [Fact]
    public void WaitForNextLaunch_GivesUp_WhenNoLauncherComes()
    {
        using var marker = TileToggle.MarkDismissed(NewGroupId());

        marker.WaitForNextLaunch(TimeSpan.FromMilliseconds(50)).Should().BeFalse();
    }

    [Theory]
    [InlineData(1000, 1050, 1.0, true)]    // same tile, a little off the first click
    [InlineData(1039, 1050, 1.0, true)]    // inside 40 DIP at 100 %
    [InlineData(1041, 1050, 1.0, false)]   // past it
    [InlineData(1059, 1050, 1.5, true)]    // 40 DIP is 60 px at 150 %
    [InlineData(1000, 400, 1.0, false)]    // up on the desktop
    public void IsOnOpeningTile_ComparesInDevicePixelsScaledByDpi(int x, int y, double scale, bool expected)
    {
        TileToggle.IsOnOpeningTile(new Point(1000, 1050), x, y, scale).Should().Be(expected);
    }
}
