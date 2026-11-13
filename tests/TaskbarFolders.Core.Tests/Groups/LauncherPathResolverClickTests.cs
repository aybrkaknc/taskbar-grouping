using System;
using System.IO;
using FluentAssertions;
using TaskbarFolders.Core.Groups;
using Xunit;

namespace TaskbarFolders.Core.Tests.Groups;

public sealed class LauncherPathResolverClickTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TaskbarFolders.Click." + Guid.NewGuid().ToString("N"));

    public LauncherPathResolverClickTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    // Only TryResolve is implemented: TryResolveClick is the interface's own default.
    private sealed class FixedResolver(string? launcher) : ILauncherPathResolver
    {
        public string? TryResolve() => launcher;
    }

    private string Launcher() => Path.Combine(_folder, "TaskbarFolders.Launcher.exe");

    [Fact]
    public void TryResolveClick_IsTheClientBesideTheLauncher()
    {
        File.WriteAllText(Launcher(), string.Empty);
        var click = Path.Combine(_folder, "TaskbarFolders.Click.exe");
        File.WriteAllText(click, string.Empty);

        ILauncherPathResolver resolver = new FixedResolver(Launcher());

        resolver.TryResolveClick().Should().Be(click);
    }

    [Fact]
    public void TryResolveClick_IsNull_WhenThereIsNoClientBesideTheLauncher()
    {
        File.WriteAllText(Launcher(), string.Empty);

        ILauncherPathResolver resolver = new FixedResolver(Launcher());

        resolver.TryResolveClick().Should().BeNull("shortcuts then start the launcher, which hands the click over itself");
    }

    [Fact]
    public void TryResolveClick_IsNull_WhenTheLauncherCannotBeResolved()
    {
        ILauncherPathResolver resolver = new FixedResolver(null);

        resolver.TryResolveClick().Should().BeNull();
    }
}
