using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Manager.Services;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Services;

public class LauncherProcessPinServiceTests
{
    private static (LauncherProcessPinService sut, Mock<ILauncherPathResolver> resolver, Mock<IProcessRunner> runner)
        CreateSut(string? launcherPath = @"C:\fake\Launcher.exe")
    {
        var resolver = new Mock<ILauncherPathResolver>();
        resolver.Setup(r => r.TryResolve()).Returns(launcherPath);

        var runner = new Mock<IProcessRunner>();

        var sut = new LauncherProcessPinService(resolver.Object, runner.Object);
        return (sut, resolver, runner);
    }

    [Fact]
    public async Task PinAsync_ReturnsError_WhenLauncherCannotBeResolved()
    {
        var (sut, _, runner) = CreateSut(launcherPath: null);

        var result = await sut.PinAsync("g");

        result.Should().Be(PinResult.Error);
        runner.Verify(r => r.RunAndWaitAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, PinResult.Success)]
    [InlineData(1, PinResult.UserDenied)]
    [InlineData(2, PinResult.Unsupported)]
    [InlineData(3, PinResult.Error)]
    [InlineData(5, PinResult.NotVerified)]
    [InlineData(42, PinResult.Error)]
    public async Task PinAsync_MapsLauncherExitCodeToPinResult(int exitCode, PinResult expected)
    {
        var (sut, _, runner) = CreateSut();
        runner.Setup(r => r.RunAndWaitAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(exitCode);

        var result = await sut.PinAsync("g");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task PinAsync_SpawnsLauncherWith_PinModeAndGroupIdArgs()
    {
        var (sut, _, runner) = CreateSut();
        ProcessStartInfo? captured = null;
        runner.Setup(r => r.RunAndWaitAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .Callback<ProcessStartInfo, TimeSpan, CancellationToken>((psi, _, _) => captured = psi)
              .ReturnsAsync(0);

        await sut.PinAsync("group-xyz");

        captured.Should().NotBeNull();
        captured!.FileName.Should().Be(@"C:\fake\Launcher.exe");
        captured.ArgumentList.Should().Contain("--pin-mode");
        captured.ArgumentList.Should().Contain("--group-id");
        captured.ArgumentList.Should().Contain("group-xyz");
        captured.UseShellExecute.Should().BeFalse("UseShellExecute must be false so ExitCode is observable");
    }

    [Fact]
    public async Task PinAsync_ReturnsError_WhenRunnerTimesOut()
    {
        var (sut, _, runner) = CreateSut();
        runner.Setup(r => r.RunAndWaitAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new TimeoutException("simulated timeout"));

        var result = await sut.PinAsync("g");

        result.Should().Be(PinResult.Error);
    }

    [Fact]
    public async Task PinAsync_ReturnsError_OnUnexpectedException()
    {
        var (sut, _, runner) = CreateSut();
        runner.Setup(r => r.RunAndWaitAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("simulated process start failure"));

        var result = await sut.PinAsync("g");

        result.Should().Be(PinResult.Error);
    }

    [Fact]
    public async Task PinAsync_RejectsBlankGroupId()
    {
        var (sut, _, _) = CreateSut();

        var act = async () => await sut.PinAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
