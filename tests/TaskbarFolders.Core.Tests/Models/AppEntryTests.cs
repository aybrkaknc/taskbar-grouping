using FluentAssertions;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Core.Tests.Models;

public class AppEntryTests
{
    [Theory]
    [InlineData(@"C:\Tools\Code.exe", "Code")]
    [InlineData(@"C:\Users\me\Desktop\My App.lnk", "My App")]
    [InlineData(@"C:\Tools\UPPER.EXE", "UPPER")]
    [InlineData(@"C:\Tools\Mixed.Lnk", "Mixed")]
    [InlineData(@"C:\Tools\with.dots.in.name.exe", "with.dots.in.name")]
    public void TryCreateFromPath_AcceptsExecutablesAndShortcuts(string path, string expectedName)
    {
        AppEntry.TryCreateFromPath(path, out var entry).Should().BeTrue();

        entry!.Name.Should().Be(expectedName);
        entry.Path.Should().Be(path, "the path is stored exactly as picked");
        entry.Arguments.Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\Docs\readme.txt")]
    [InlineData(@"C:\Tools\setup.msi")]
    [InlineData(@"C:\Tools\script.bat")]
    [InlineData(@"C:\Tools\noextension")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryCreateFromPath_RejectsEverythingElse(string? path)
    {
        AppEntry.TryCreateFromPath(path, out var entry).Should().BeFalse();

        entry.Should().BeNull();
    }

    [Theory]
    [InlineData("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("Play Store", "com.android.vending")]
    [InlineData("Services", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\services.msc")]
    public void TryCreateFromAumid_StoresTheAppAsItsAppsFolderEntry(string name, string aumid)
    {
        AppEntry.TryCreateFromAumid(name, aumid, out var entry).Should().BeTrue();

        entry!.Name.Should().Be(name);
        entry.Path.Should().Be(@"shell:AppsFolder\" + aumid, "the shell starts it from here as Start does");
        entry.Arguments.Should().BeNull();
        AppEntry.IsShellApp(entry.Path).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "App!Id")]
    [InlineData("  ", "App!Id")]
    [InlineData("Name", null)]
    [InlineData("Name", "  ")]
    public void TryCreateFromAumid_RefusesABlankNameOrId(string? name, string? aumid)
    {
        AppEntry.TryCreateFromAumid(name, aumid, out var entry).Should().BeFalse();
        entry.Should().BeNull();
    }

    [Theory]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData(@"SHELL:appsfolder\Chrome", true)]
    [InlineData(@"C:\Windows\System32\calc.exe", false)]
    [InlineData(@"shell:AppsFolder", false)]
    [InlineData(@"shell:Desktop\x.lnk", false)]
    [InlineData(null, false)]
    public void IsShellApp_RecognisesAppsFolderEntriesOnly(string? path, bool expected) =>
        AppEntry.IsShellApp(path).Should().Be(expected);
}
