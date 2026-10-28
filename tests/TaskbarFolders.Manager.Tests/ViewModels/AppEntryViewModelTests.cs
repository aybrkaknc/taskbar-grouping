using FluentAssertions;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

public class AppEntryViewModelTests
{
    [Fact]
    public void Name_WritesThroughTrimmed()
    {
        var entry = new AppEntry { Name = "LDPlayer 14", Path = @"C:\Apps\LDPlayer 14.lnk" };
        var sut = new AppEntryViewModel(entry);

        sut.Name = "  LDPlayer  ";

        entry.Name.Should().Be("LDPlayer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_FallsBackToTheFileName_WhenCleared(string cleared)
    {
        var entry = new AppEntry { Name = "Custom", Path = @"C:\Apps\LDPlayer 14.lnk" };
        var sut = new AppEntryViewModel(entry);

        sut.Name = cleared;

        sut.Name.Should().Be("LDPlayer 14");
        entry.Name.Should().Be("LDPlayer 14", "a tile without a label is not a valid outcome");
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" --incognito ", "--incognito")]
    public void Arguments_AreStoredTrimmed_AndBlankAsNone(string typed, string? stored)
    {
        var entry = new AppEntry { Name = "x", Path = "x.exe", Arguments = "--old" };
        var sut = new AppEntryViewModel(entry);

        sut.Arguments = typed;

        entry.Arguments.Should().Be(stored);
    }

    [Fact]
    public void IconPath_WritesThrough_AndDrivesHasCustomIcon()
    {
        var entry = new AppEntry { Name = "x", Path = "x.exe" };
        var sut = new AppEntryViewModel(entry);
        sut.HasCustomIcon.Should().BeFalse();

        sut.IconPath = @"C:\icons\x.png";

        entry.IconPath.Should().Be(@"C:\icons\x.png");
        sut.HasCustomIcon.Should().BeTrue();
    }
}
