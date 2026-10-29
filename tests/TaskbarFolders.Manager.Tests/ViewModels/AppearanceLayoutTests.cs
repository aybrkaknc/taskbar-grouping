using FluentAssertions;
using TaskbarFolders.Manager.ViewModels;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

/// <summary>
/// How wide the Manager's window has to be for the preview on Appearance to stay readable. The
/// window grows to that width and never shrinks by itself.
/// </summary>
public sealed class AppearanceLayoutTests
{
    [Fact]
    public void RequiredWidth_IsTheCurrentWidth_WhenThePreviewHasRoomEnough()
    {
        // The column is 400 wide, the preview 300 natural: shown at full size.
        AppearanceLayout.RequiredWindowWidth(currentWidth: 1100, previewColumnWidth: 400, previewNaturalWidth: 300)
            .Should().Be(1100);
    }

    [Fact]
    public void RequiredWidth_IsTheCurrentWidth_WhenThePreviewIsShrunkNoFurtherThanTheMinimumScale()
    {
        // 300 natural at the 0.75 minimum scale needs 225; the column has exactly that.
        AppearanceLayout.RequiredWindowWidth(1100, previewColumnWidth: 225, previewNaturalWidth: 300)
            .Should().Be(1100);
    }

    [Fact]
    public void RequiredWidth_GrowsByWhatTheColumnIsShort_WhenThePreviewWouldShrinkBelowTheMinimumScale()
    {
        // 600 natural needs 450 at the minimum scale; the column has 200, so 250 more.
        AppearanceLayout.RequiredWindowWidth(1000, previewColumnWidth: 200, previewNaturalWidth: 600)
            .Should().Be(1250);
    }

    [Fact]
    public void MinimumScale_IsThreeQuarters()
    {
        AppearanceLayout.MinPreviewScale.Should().Be(0.75);
    }

    [Theory]
    [InlineData(1000, 1250, 1920, 1250)]   // fits the screen
    [InlineData(1000, 2400, 1920, 1920)]   // never wider than the work area
    [InlineData(1500, 1200, 1920, 1500)]   // never narrower than it is
    public void Target_FollowsTheRequirement_WithinTheWorkArea(double current, double required, double workArea, double expected)
    {
        AppearanceLayout.Target(current, required, workArea).Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 1250, 0, 1920, 100)]       // room to the right: stays
    [InlineData(1000, 1250, 0, 1920, 670)]      // would overflow the right edge: shifted left
    [InlineData(1000, 1920, 0, 1920, 0)]        // as wide as the screen: flush left
    [InlineData(-1600, 1250, -1920, 1920, -1600)]  // a second monitor to the left, work area -1920..0
    public void Left_KeepsTheWindowInsideTheWorkArea(double left, double width, double areaLeft, double areaWidth, double expected)
    {
        AppearanceLayout.Left(left, width, areaLeft, areaWidth).Should().Be(expected);
    }
}
