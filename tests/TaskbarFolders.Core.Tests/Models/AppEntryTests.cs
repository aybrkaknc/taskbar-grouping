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
}
