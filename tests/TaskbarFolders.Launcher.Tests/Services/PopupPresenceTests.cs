using System;
using System.Threading;
using FluentAssertions;
using TaskbarFolders.Launcher.Services;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Services;

public sealed class PopupPresenceTests
{
    // Machine-wide names: a fresh group id per test keeps runs from seeing each other.
    private static string NewGroupId() => "presence-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public void Claim_IsFirstOnlyForTheFirstLauncherOfAGroup()
    {
        var id = NewGroupId();
        using var first = PopupPresence.Claim(id);
        using var second = PopupPresence.Claim(id);
        using var otherGroup = PopupPresence.Claim(NewGroupId());

        first.IsFirst.Should().BeTrue();
        second.IsFirst.Should().BeFalse("a launcher for the same group already holds the marker");
        otherGroup.IsFirst.Should().BeTrue("groups do not share markers");
    }

    [Fact]
    public void Claim_IsFirstAgain_OnceThePopupHasGone()
    {
        var id = NewGroupId();
        PopupPresence.Claim(id).Dispose();

        using var later = PopupPresence.Claim(id);

        later.IsFirst.Should().BeTrue();
    }

    [Fact]
    public void SignalReload_ReachesTheListeningPopup()
    {
        var id = NewGroupId();
        using var popup = PopupPresence.Claim(id);
        using var reloaded = new ManualResetEventSlim();
        popup.ListenForReload(reloaded.Set);

        using var launcher = PopupPresence.Claim(id);
        launcher.SignalReload();

        reloaded.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void SignalReload_SentBeforeListening_IsStillDelivered()
    {
        // The second launcher can finish before the first has built its popup.
        var id = NewGroupId();
        using var popup = PopupPresence.Claim(id);
        using var launcher = PopupPresence.Claim(id);
        launcher.SignalReload();

        using var reloaded = new ManualResetEventSlim();
        popup.ListenForReload(reloaded.Set);

        reloaded.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }
}
