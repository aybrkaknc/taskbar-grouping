using System;
using FluentAssertions;
using TaskbarFolders.Launcher.Hosting;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Hosting;

public sealed class HostRouterTests
{
    private DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private HostRouter NewRouter() => new(() => _now);

    [Fact]
    public void Decide_ShowsAClosedGroup()
    {
        NewRouter().Decide("g", isOpen: false, hasFiles: false).Should().Be(HostAction.Show);
    }

    [Fact]
    public void Decide_ClosesAnOpenGroup_WhenItsTileIsClickedAgain()
    {
        NewRouter().Decide("g", isOpen: true, hasFiles: false).Should().Be(HostAction.Close);
    }

    [Fact]
    public void Decide_IgnoresTheRequestThatFollowsAPressOnTheOwnTile()
    {
        var router = NewRouter();
        router.MarkDismissedByOwnTile("g");

        // The press already closed the popup, so the group reads as closed when the request comes.
        router.Decide("g", isOpen: false, hasFiles: false).Should().Be(HostAction.Ignore);
    }

    [Fact]
    public void Decide_ConsumesTheDismissal_SoTheNextClickOpensThePopup()
    {
        var router = NewRouter();
        router.MarkDismissedByOwnTile("g");
        router.Decide("g", isOpen: false, hasFiles: false);

        router.Decide("g", isOpen: false, hasFiles: false).Should().Be(HostAction.Show);
    }

    [Fact]
    public void Decide_ForgetsADismissalOlderThanTheWindow()
    {
        var router = NewRouter();
        router.MarkDismissedByOwnTile("g");
        _now += HostRouter.DismissWindow + TimeSpan.FromMilliseconds(1);

        router.Decide("g", isOpen: false, hasFiles: false).Should().Be(HostAction.Show);
    }

    [Fact]
    public void Decide_KeepsADismissalPerGroup()
    {
        var router = NewRouter();
        router.MarkDismissedByOwnTile("a");

        router.Decide("b", isOpen: false, hasFiles: false).Should().Be(HostAction.Show);
        router.Decide("a", isOpen: false, hasFiles: false).Should().Be(HostAction.Ignore);
    }

    [Fact]
    public void Decide_TreatsGroupIdsCaseInsensitively()
    {
        var router = NewRouter();
        router.MarkDismissedByOwnTile("Group");

        router.Decide("group", isOpen: false, hasFiles: false).Should().Be(HostAction.Ignore);
    }

    [Fact]
    public void Decide_NeverTogglesWhenFilesCameWithTheRequest()
    {
        var router = NewRouter();
        router.MarkDismissedByOwnTile("g");

        // A multi-selection from Explorer is one request per file; each has to be added.
        router.Decide("g", isOpen: true, hasFiles: true).Should().Be(HostAction.Reload);
        router.Decide("g", isOpen: false, hasFiles: true).Should().Be(HostAction.Show);
    }
}
