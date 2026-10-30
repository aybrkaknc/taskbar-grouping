using FluentAssertions;
using TaskbarFolders.Core.Apps;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Core.Tests.Apps;

/// <summary>
/// The two rules ADR-009 settles: which entries of Windows' installed-apps list the picker shows,
/// and what a picked one is stored as — a plain program by its path, everything else as its
/// <c>shell:AppsFolder</c> entry.
/// </summary>
public class InstalledAppTests
{
    private static InstalledApp App(string? target = null, string? arguments = null, string? package = null) =>
        new("Some App", "Some.App!Id", target, arguments, package);

    [Theory]
    [InlineData(@"C:\Program Files\Git\git-bash.exe")]
    [InlineData(@"C:\Users\me\Desktop\Tool.lnk")]
    [InlineData(@"C:\Windows\system32\services.msc")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\WsaClient.exe")]
    public void IsListed_ForAProgramOrAConsole(string target) =>
        App(target).IsListed.Should().BeTrue();

    [Fact]
    public void IsListed_ForAPackagedApp_WhichHasNoTarget() =>
        App(target: null, package: "Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe").IsListed.Should().BeTrue();

    [Theory]
    [InlineData("http://support.steampowered.com/")]
    [InlineData("https://docs.mp3tag.de")]
    [InlineData(@"C:\Program Files\7-Zip\7-zip.chm")]
    [InlineData(@"C:\Program Files\App\Website.url")]
    [InlineData(@"C:\Program Files\App\readme.txt")]
    [InlineData(@"C:\Program Files\App\Manual.pdf")]
    [InlineData(@"C:\Program Files\App\help.html")]
    public void IsNotListed_ForAWebLinkOrADocument(string target) =>
        App(target).IsListed.Should().BeFalse();

    [Theory]
    [InlineData(@"C:\Program Files\Git\git-bash.exe")]
    [InlineData(@"C:\Users\me\Desktop\Tool.lnk")]
    public void APlainProgram_IsStoredByItsPath_UnderTheListsName(string target)
    {
        var entry = App(target).ToEntry();

        entry.Path.Should().Be(target, "as if it had been browsed for");
        entry.Name.Should().Be("Some App", "the name Start shows, not the file name");
        entry.Arguments.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]                                                // packaged
    [InlineData(@"C:\Program Files\Git\git-bash.exe", "--cd-to-home")]     // a program Start runs with arguments
    [InlineData(@"C:\Windows\system32\services.msc", null)]                // a console
    public void EverythingElse_IsStoredAsItsAppsFolderEntry(string? target, string? arguments)
    {
        var entry = App(target, arguments).ToEntry();

        entry.Path.Should().Be(@"shell:AppsFolder\Some.App!Id");
        entry.Name.Should().Be("Some App");
        AppEntry.IsShellApp(entry.Path).Should().BeTrue();
    }
}
