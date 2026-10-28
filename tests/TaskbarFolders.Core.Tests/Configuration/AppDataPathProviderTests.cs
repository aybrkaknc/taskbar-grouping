using System.IO;
using FluentAssertions;
using TaskbarFolders.Shared.Configuration;
using Xunit;

namespace TaskbarFolders.Core.Tests.Configuration;

public class AppDataPathProviderTests
{
    [Fact]
    public void AppDataRoot_AppendsTaskbarFoldersToBaseDirectory()
    {
        var sut = new AppDataPathProvider("C:/some/base");

        sut.AppDataRoot.Should().Be(Path.Combine("C:/some/base", "TaskbarFolders"));
    }

    [Fact]
    public void GroupsDirectory_IsRootSlashGroups()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.GroupsDirectory.Should().Be(Path.Combine("C:/base", "TaskbarFolders", "groups"));
    }

    [Fact]
    public void IconsDirectory_IsRootSlashIcons()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.IconsDirectory.Should().Be(Path.Combine("C:/base", "TaskbarFolders", "icons"));
    }

    [Fact]
    public void LogsDirectory_IsRootSlashLogs()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.LogsDirectory.Should().Be(Path.Combine("C:/base", "TaskbarFolders", "logs"));
    }

    [Fact]
    public void ShortcutsDirectory_IsRootSlashShortcuts()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.ShortcutsDirectory.Should().Be(Path.Combine("C:/base", "TaskbarFolders", "shortcuts"));
    }

    [Fact]
    public void SettingsFile_IsSettingsJsonAtRoot()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.SettingsFile.Should().Be(Path.Combine("C:/base", "TaskbarFolders", "settings.json"));
    }

    [Fact]
    public void GetGroupFile_AppendsIdJsonToGroupsDirectory()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.GetGroupFile("abc")
            .Should().Be(Path.Combine("C:/base", "TaskbarFolders", "groups", "abc.json"));
    }

    [Fact]
    public void GetGroupIconFile_AppendsIdIcoToIconsDirectory()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.GetGroupIconFile("abc")
            .Should().Be(Path.Combine("C:/base", "TaskbarFolders", "icons", "abc.ico"));
    }

    [Fact]
    public void GetGroupShortcutFile_AppendsIdLnkToShortcutsDirectory()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.GetGroupShortcutFile("abc")
            .Should().Be(Path.Combine("C:/base", "TaskbarFolders", "shortcuts", "abc.lnk"));
    }

    [Fact]
    public void GetGroupCustomIconFile_IsAPngInTheCustomSubfolderOfIcons()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.GetGroupCustomIconFile("abc")
            .Should().Be(Path.Combine("C:/base", "TaskbarFolders", "icons", "custom", "abc.png"));
    }

    [Fact]
    public void GetAppCustomIconFile_IsAVersionedPngInTheAppsSubfolder()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.GetAppCustomIconFile("abc", "0123abcd")
            .Should().Be(Path.Combine("C:/base", "TaskbarFolders", "icons", "custom", "apps", "abc-0123abcd.png"));
        FluentActions.Invoking(() => sut.GetAppCustomIconFile("abc", "nothex!!")).Should().Throw<System.ArgumentException>();
        FluentActions.Invoking(() => sut.GetAppCustomIconFile("../x", "0123abcd")).Should().Throw<System.ArgumentException>();
    }

    [Theory]
    [InlineData("abc-0123abcd.png", true)]
    [InlineData("ABC-0123ABCD.PNG", true)]
    [InlineData("abc-other-0123abcd.png", false)]
    [InlineData("abc-0123abcd.ico", false)]
    [InlineData("abc.png", false)]
    [InlineData("abcd-0123abcd.png", false)]
    public void IsAppCustomIconFile_MatchesOnlyTheGroupsOwnImages(string name, bool expected)
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.IsAppCustomIconFile("abc", Path.Combine(sut.AppCustomIconsDirectory, name)).Should().Be(expected);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("with/slash")]
    [InlineData("with\\backslash")]
    [InlineData("with space")]
    [InlineData("name:colon")]
    [InlineData("name*star")]
    [InlineData("")]
    [InlineData("  ")]
    public void GetGroup_RejectsIdsThatCouldEscapeTheAppDataRoot(string badId)
    {
        var sut = new AppDataPathProvider("C:/base");

        FluentActions.Invoking(() => sut.GetGroupFile(badId)).Should().Throw<System.ArgumentException>();
        FluentActions.Invoking(() => sut.GetGroupIconFile(badId)).Should().Throw<System.ArgumentException>();
        FluentActions.Invoking(() => sut.GetGroupShortcutFile(badId)).Should().Throw<System.ArgumentException>();
        FluentActions.Invoking(() => sut.GetGroupCustomIconFile(badId)).Should().Throw<System.ArgumentException>();
    }

    [Theory]
    [InlineData("guid-like_id.1")]
    [InlineData("550e8400e29b41d4a716446655440000")]
    [InlineData("with-hyphen")]
    [InlineData("with.dot")]
    [InlineData("with_underscore")]
    public void GetGroup_AcceptsValidIdShapes(string okId)
    {
        var sut = new AppDataPathProvider("C:/base");

        // Should not throw for any of these.
        sut.GetGroupFile(okId);
        sut.GetGroupIconFile(okId);
        sut.GetGroupShortcutFile(okId);
    }

    [Fact]
    public void PinnedTaskbarDirectory_StaysUnderTheBase_InTheTestConstructors()
    {
        // A test must never be able to write into the real taskbar pins.
        new AppDataPathProvider("C:/base").PinnedTaskbarDirectory
            .Should().StartWith(Path.Combine("C:/base"));
        new AppDataPathProvider("C:/base", "C:/menu").PinnedTaskbarDirectory
            .Should().StartWith(Path.Combine("C:/base"));
    }

    [Fact]
    public void GetGroupIconFile_WithVersion_AppendsItToTheId()
    {
        var sut = new AppDataPathProvider("C:/base");

        sut.GetGroupIconFile("g1", "abcdef12")
            .Should().Be(Path.Combine("C:/base", "TaskbarFolders", "icons", "g1-abcdef12.ico"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abcdef123")]
    [InlineData("ghijklmn")]
    [InlineData("../../xx")]
    public void GetGroupIconFile_RejectsAnythingButEightHexDigits(string version)
    {
        var sut = new AppDataPathProvider("C:/base");

        var act = () => sut.GetGroupIconFile("g1", version);

        act.Should().Throw<System.ArgumentException>();
    }

    [Theory]
    [InlineData("g1.ico", true)]
    [InlineData("g1-abcdef12.ico", true)]
    [InlineData("G1-ABCDEF12.ICO", true)]
    [InlineData(@"C:\base\TaskbarFolders\icons\g1-00000000.ico", true)]
    [InlineData("g1-x-abcdef12.ico", false)]
    [InlineData("g1-notes.ico", false)]
    [InlineData("g1-abcdef12.png", false)]
    [InlineData("g10.ico", false)]
    [InlineData("g1-abcdef123.ico", false)]
    public void IsGroupIconFile_MatchesOnlyTheGroupsOwnIcons(string path, bool expected)
    {
        new AppDataPathProvider("C:/base").IsGroupIconFile("g1", path).Should().Be(expected);
    }
}
