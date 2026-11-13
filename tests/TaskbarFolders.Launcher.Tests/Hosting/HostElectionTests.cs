using System;
using FluentAssertions;
using TaskbarFolders.Launcher.Hosting;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Hosting;

public sealed class HostElectionTests
{
    // Machine-wide names: a unique one per test keeps runs, and a real resident launcher, apart.
    private static string NewName() => @"Local\TaskbarFolders.Test.Host." + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryBecomeHost_IsTrueOnlyForTheFirstProcess()
    {
        var name = NewName();
        using var first = new HostElection(name);
        using var second = new HostElection(name);

        first.TryBecomeHost().Should().BeTrue();
        second.TryBecomeHost().Should().BeFalse("the name is already held");
    }

    [Fact]
    public void HostExists_ReflectsWhetherSomeoneHoldsTheName()
    {
        var name = NewName();
        HostElection.HostExists(name).Should().BeFalse();

        using var host = new HostElection(name);
        host.TryBecomeHost();

        HostElection.HostExists(name).Should().BeTrue();
    }

    [Fact]
    public void TryBecomeHost_IsTrueAgain_OnceTheHostIsGone()
    {
        var name = NewName();
        var gone = new HostElection(name);
        gone.TryBecomeHost().Should().BeTrue();
        gone.Dispose();

        using var next = new HostElection(name);

        next.TryBecomeHost().Should().BeTrue();
    }

    [Fact]
    public void TryBecomeHost_CalledTwiceOnTheHost_StaysTrue()
    {
        using var host = new HostElection(NewName());

        host.TryBecomeHost().Should().BeTrue();
        host.TryBecomeHost().Should().BeTrue();
    }
}
