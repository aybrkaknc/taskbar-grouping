using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using TaskbarFolders.Launcher.Services;
using Xunit;

namespace TaskbarFolders.Launcher.Tests;

/// <summary>
/// Windows refused a pin request two seconds after the group's shortcut was written and allowed
/// it twenty-three seconds later. The wait asks again before giving up; it never asks around
/// Windows' consent.
/// </summary>
public sealed class PinPermissionWaitTests
{
    private readonly List<TimeSpan> _delays = [];

    private Task Record(TimeSpan delay)
    {
        _delays.Add(delay);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WaitUntilAllowedAsync_ReturnsAtOnce_WhenPinningIsAllowed()
    {
        var allowed = await PinPermissionWait.WaitUntilAllowedAsync(() => true, attempts: 6, TimeSpan.FromSeconds(1), Record);

        allowed.Should().BeTrue();
        _delays.Should().BeEmpty("a machine that allows pinning must not wait");
    }

    [Fact]
    public async Task WaitUntilAllowedAsync_AsksAgain_UntilWindowsAllows()
    {
        var calls = 0;

        var allowed = await PinPermissionWait.WaitUntilAllowedAsync(() => ++calls == 3, attempts: 6, TimeSpan.FromSeconds(1), Record);

        allowed.Should().BeTrue();
        calls.Should().Be(3);
        _delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitUntilAllowedAsync_GivesUp_AfterTheLastAttempt_WithoutAFinalDelay()
    {
        var calls = 0;

        var allowed = await PinPermissionWait.WaitUntilAllowedAsync(() => { calls++; return false; }, attempts: 4, TimeSpan.FromMilliseconds(500), Record);

        allowed.Should().BeFalse();
        calls.Should().Be(4);
        _delays.Should().HaveCount(3, "there is nothing to wait for after the last answer");
    }

    [Fact]
    public async Task WaitUntilAllowedAsync_AsksOnce_WhenGivenOneAttempt()
    {
        var calls = 0;

        var allowed = await PinPermissionWait.WaitUntilAllowedAsync(() => { calls++; return false; }, attempts: 1, TimeSpan.FromSeconds(1), Record);

        allowed.Should().BeFalse();
        calls.Should().Be(1);
        _delays.Should().BeEmpty();
    }

    [Fact]
    public async Task WaitUntilAllowedAsync_RefusesNoAttempts()
    {
        Func<Task> act = async () => await PinPermissionWait.WaitUntilAllowedAsync(() => true, attempts: 0, TimeSpan.FromSeconds(1), Record);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
