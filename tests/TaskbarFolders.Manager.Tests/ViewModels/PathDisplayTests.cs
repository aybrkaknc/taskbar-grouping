using System;
using System.IO;
using FluentAssertions;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

/// <summary>
/// The app list shows a path under every entry, so the account name would otherwise appear in
/// every screenshot, screen share and bug report of the Manager.
/// </summary>
public sealed class PathDisplayTests
{
    private static readonly string _profile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar);

    [Fact]
    public void ForDisplay_ReplacesTheProfileDirectory()
    {
        var path = Path.Combine(_profile, "Desktop", "Arduino IDE.lnk");

        var display = PathDisplay.ForDisplay(path);

        display.Should().Be(@"%USERPROFILE%\Desktop\Arduino IDE.lnk");
        display.Should().NotContain(Path.GetFileName(_profile), "the account name is the whole point of this");
    }

    [Fact]
    public void ForDisplay_MatchesRegardlessOfCase()
    {
        // Windows paths are case-insensitive, and the casing a shortcut carries is whatever
        // the shell happened to write.
        var path = Path.Combine(_profile.ToUpperInvariant(), "Desktop", "app.lnk");

        PathDisplay.ForDisplay(path).Should().StartWith("%USERPROFILE%");
    }

    [Fact]
    public void ForDisplay_ReturnsTheTokenAlone_ForTheProfileItself()
    {
        PathDisplay.ForDisplay(_profile).Should().Be("%USERPROFILE%");
    }

    [Theory]
    [InlineData(@"C:\Program Files\Obsidian\Obsidian.exe")]
    [InlineData(@"C:\Users\Public\Desktop\WinSCP.lnk")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    public void ForDisplay_LeavesPathsOutsideTheProfileAlone(string path)
    {
        PathDisplay.ForDisplay(path).Should().Be(path);
    }

    [Fact]
    public void ForDisplay_DoesNotRewriteASiblingDirectoryWithTheSamePrefix()
    {
        // C:\Users\alice-backup must not become %USERPROFILE%-backup: the prefix has to end
        // on a separator or the result points somewhere that does not exist.
        var sibling = _profile + "-backup";
        var path = Path.Combine(sibling, "app.lnk");

        PathDisplay.ForDisplay(path).Should().Be(path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForDisplay_PassesBlankInputThrough(string? path)
    {
        PathDisplay.ForDisplay(path).Should().Be(path ?? string.Empty);
    }

    [Fact]
    public void AppEntryViewModel_ExposesTheShortenedPath_AndKeepsTheRealOne()
    {
        var real = Path.Combine(_profile, "Desktop", "Postman.lnk");
        var sut = new AppEntryViewModel(new AppEntry { Name = "Postman", Path = real });

        sut.DisplayPath.Should().Be(@"%USERPROFILE%\Desktop\Postman.lnk");
        sut.Path.Should().Be(real, "the entry must still launch and persist the real path");
        sut.Entry.Path.Should().Be(real);
    }

    [Fact]
    public void AppEntryViewModel_RaisesDisplayPath_WhenPathChanges()
    {
        var sut = new AppEntryViewModel(new AppEntry { Name = "x", Path = @"C:\Windows\System32\cmd.exe" });
        var raised = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppEntryViewModel.DisplayPath))
            {
                raised++;
            }
        };

        sut.Path = Path.Combine(_profile, "Desktop", "other.lnk");

        raised.Should().BeGreaterThan(0, "the bound label would otherwise keep showing the old path");
        sut.DisplayPath.Should().Be(@"%USERPROFILE%\Desktop\other.lnk");
    }

    [Fact]
    public void ForDisplay_ShowsAnInstalledAppByItsId_NotAsAPath()
    {
        PathDisplay.ForDisplay(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")
            .Should().Be("Installed app · Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
    }
}
