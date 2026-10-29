using System.Diagnostics;
using FluentAssertions;
using Moq;
using TaskbarFolders.Manager.Services;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Services;

public class PinBlockerRulesTests
{
    [Theory]
    [InlineData(null, null, null, PinBlocker.Unknown)]
    [InlineData(null, null, 1, PinBlocker.Unknown)]
    [InlineData(null, null, 0, PinBlocker.NotificationsOff)]
    [InlineData(null, 0, 1, PinBlocker.Unknown)]
    [InlineData(null, 1, 1, PinBlocker.TokenRequired)]
    [InlineData(0, null, 1, PinBlocker.Unknown)]
    [InlineData(1, null, 1, PinBlocker.PolicyBlocked)]
    public void Classify_NamesTheCauseTheMachineShows(int? policy, int? laf, int? toast, PinBlocker expected)
    {
        PinBlockerRules.Classify(policy, laf, toast).Should().Be(expected);
    }

    [Fact]
    public void Classify_PutsWhatTheUserCannotFixBeforeNotifications()
    {
        // Turning notifications on changes nothing while a policy or a missing key blocks pinning.
        PinBlockerRules.Classify(noPinningPolicy: 1, lafSeedValue: 1, toastEnabled: 0).Should().Be(PinBlocker.PolicyBlocked);
        PinBlockerRules.Classify(noPinningPolicy: null, lafSeedValue: 1, toastEnabled: 0).Should().Be(PinBlocker.TokenRequired);
    }
}

public class PinHelperTests
{
    [Fact]
    public void RevealInExplorer_SelectsTheFile()
    {
        var processes = new Mock<IProcessRunner>();
        ProcessStartInfo? started = null;
        processes.Setup(p => p.TryStart(It.IsAny<ProcessStartInfo>())).Callback<ProcessStartInfo>(p => started = p).Returns(true);

        new PinHelper(processes.Object).RevealInExplorer(@"C:\Data\shortcuts\g.lnk");

        started!.FileName.Should().Be("explorer.exe");
        started.Arguments.Should().Be("/select,\"C:\\Data\\shortcuts\\g.lnk\"");
        started.UseShellExecute.Should().BeTrue();
    }

    [Fact]
    public void OpenNotificationSettings_OpensTheSettingsPage()
    {
        var processes = new Mock<IProcessRunner>();
        ProcessStartInfo? started = null;
        processes.Setup(p => p.TryStart(It.IsAny<ProcessStartInfo>())).Callback<ProcessStartInfo>(p => started = p).Returns(true);

        new PinHelper(processes.Object).OpenNotificationSettings();

        started!.FileName.Should().Be("ms-settings:notifications");
        started.UseShellExecute.Should().BeTrue("a URI is opened by the shell");
    }

    [Fact]
    public void OpenNotificationSettings_DoesNotThrow_WhenTheProcessCannotStart()
    {
        var processes = new Mock<IProcessRunner>();
        processes.Setup(p => p.TryStart(It.IsAny<ProcessStartInfo>())).Returns(false);

        var act = () => new PinHelper(processes.Object).OpenNotificationSettings();

        act.Should().NotThrow();
    }
}
