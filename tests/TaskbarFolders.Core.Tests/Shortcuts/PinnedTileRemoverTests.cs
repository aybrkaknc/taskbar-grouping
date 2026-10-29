using System;
using System.IO;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Shared.Configuration;
using Xunit;

namespace TaskbarFolders.Core.Tests.Shortcuts;

/// <summary>
/// The taskbar pin folder and the groups folder are temp directories; the shell is a mock, so
/// these tests never touch the real taskbar.
/// </summary>
public sealed class PinnedTileRemoverTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "tf-pins-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IShortcutReader> _reader = new();
    private readonly Mock<ITaskbarUnpinner> _unpinner = new();
    private readonly Mock<IAppDataPathProvider> _paths = new();

    private string Pinned => Path.Combine(_temp, "Pinned");

    private string Groups => Path.Combine(_temp, "groups");

    public PinnedTileRemoverTests()
    {
        Directory.CreateDirectory(Pinned);
        Directory.CreateDirectory(Groups);
        _paths.SetupGet(p => p.PinnedTaskbarDirectory).Returns(Pinned);
        _paths.SetupGet(p => p.GroupsDirectory).Returns(Groups);
        _unpinner.Setup(u => u.TryUnpin(It.IsAny<string>())).Returns(true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private PinnedTileRemover CreateSut() => new(_paths.Object, _reader.Object, _unpinner.Object);

    private string Pin(string fileName, string? aumid)
    {
        var path = Path.Combine(Pinned, fileName);
        File.WriteAllText(path, "");
        _reader.Setup(r => r.TryReadAumid(path)).Returns(aumid);
        return path;
    }

    private void Group(string id) => File.WriteAllText(Path.Combine(Groups, id + ".json"), "{}");

    [Fact]
    public void Unpin_UnpinsOnlyTheGroupsOwnTiles()
    {
        // Windows names the pinned copy itself, so a tile is recognised by its AUMID, not its name.
        var ours = Pin("Work.lnk", GroupAumid.For("a"));
        var ourCopy = Pin("Work (2).lnk", "taskbarfolders.group.A");
        var otherGroup = Pin("Games.lnk", GroupAumid.For("b"));
        var foreign = Pin("Chrome.lnk", "Chrome");
        var unreadable = Pin("Broken.lnk", null);

        var result = CreateSut().Unpin("a");

        result.Should().Be(new PinnedTileRemoval(Removed: 2, Left: []));
        _unpinner.Verify(u => u.TryUnpin(ours), Times.Once);
        _unpinner.Verify(u => u.TryUnpin(ourCopy), Times.Once);
        _unpinner.Verify(u => u.TryUnpin(otherGroup), Times.Never);
        _unpinner.Verify(u => u.TryUnpin(foreign), Times.Never);
        _unpinner.Verify(u => u.TryUnpin(unreadable), Times.Never);
    }

    [Fact]
    public void Unpin_ReportsTheTilesTheShellRefused()
    {
        var refused = Pin("Work.lnk", GroupAumid.For("a"));
        _unpinner.Setup(u => u.TryUnpin(refused)).Returns(false);

        var result = CreateSut().Unpin("a");

        result.Removed.Should().Be(0);
        result.Left.Should().Equal(refused);
    }

    [Fact]
    public void Unpin_FindsNothing_WhenTheGroupWasNeverPinned()
    {
        Pin("Chrome.lnk", "Chrome");

        var result = CreateSut().Unpin("a");

        result.Should().Be(new PinnedTileRemoval(Removed: 0, Left: []));
        result.Found.Should().BeFalse();
    }

    [Fact]
    public void Unpin_FindsNothing_WhenThePinFolderDoesNotExist()
    {
        Directory.Delete(Pinned);

        CreateSut().Unpin("a").Found.Should().BeFalse();
    }

    [Fact]
    public void IsPinned_SaysWhetherTheGroupHasATileOnTheTaskbar()
    {
        Pin("Work.lnk", GroupAumid.For("a"));
        Pin("Chrome.lnk", "Chrome");
        var sut = CreateSut();

        sut.IsPinned("a").Should().BeTrue();
        sut.IsPinned("b").Should().BeFalse("a foreign pin is not a group's tile");
        _unpinner.Verify(u => u.TryUnpin(It.IsAny<string>()), Times.Never, "asking is not unpinning");
    }

    [Fact]
    public void FindOrphans_ListsGroupTilesWhoseGroupIsGone()
    {
        Group("a");
        Pin("Work.lnk", GroupAumid.For("a"));
        var orphan = Pin("Old.lnk", GroupAumid.For("gone"));
        Pin("Chrome.lnk", "Chrome");
        Pin("Broken.lnk", null);

        CreateSut().FindOrphans().Should().Equal(orphan);
    }

    [Fact]
    public void FindOrphans_ListsEveryGroupTile_WhenThereAreNoGroups()
    {
        var orphan = Pin("Work.lnk", GroupAumid.For("a"));
        Directory.Delete(Groups);

        CreateSut().FindOrphans().Should().Equal(orphan);
    }
}
