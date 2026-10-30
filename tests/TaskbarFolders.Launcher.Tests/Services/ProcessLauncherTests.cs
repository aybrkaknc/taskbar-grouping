using FluentAssertions;
using TaskbarFolders.Launcher.Services;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Services;

public class ProcessLauncherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Launch_ReturnsFalse_ForBlankPath(string? path)
    {
        var sut = new ProcessLauncher();

        var ok = sut.Launch(path!, arguments: null);

        ok.Should().BeFalse();
    }

    [Fact]
    public void Launch_ReturnsFalse_ForNonexistentExecutable()
    {
        var sut = new ProcessLauncher();

        var ok = sut.Launch("C:/this/does/not/exist-xyz.exe", arguments: null);

        ok.Should().BeFalse("Process.Start with UseShellExecute throws Win32Exception for missing files; ProcessLauncher catches it");
    }

    [Fact]
    public void Launch_CountsAShellActivation_AsStarted_AndGivesItNoWorkingDirectory()
    {
        // The shell starts an installed app without handing back a process, so Process.Start
        // returns null; and "shell:AppsFolder" is no directory to start in.
        System.Diagnostics.ProcessStartInfo? seen = null;
        var sut = new ProcessLauncher(start: info => { seen = info; return null; });

        var ok = sut.Launch(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", arguments: null);

        ok.Should().BeTrue();
        seen!.UseShellExecute.Should().BeTrue();
        seen.WorkingDirectory.Should().BeEmpty();
    }

    [Fact]
    public void Launch_StillCountsANullProcess_AsAFailure_ForAFile()
    {
        var sut = new ProcessLauncher(start: _ => null);

        sut.Launch(@"C:\Tools\app.exe", arguments: null).Should().BeFalse();
    }
}
