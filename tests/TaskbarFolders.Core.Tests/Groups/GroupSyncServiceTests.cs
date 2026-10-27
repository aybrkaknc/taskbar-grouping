using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Core.Tests.Groups;

public sealed class GroupSyncServiceTests : IDisposable
{
    private readonly string _tempBase;
    private readonly AppDataPathProvider _paths;
    private readonly Mock<IIconExtractor> _extractor;
    private readonly Mock<ICompositeIconGenerator> _composer;
    private readonly Mock<IIcoFileWriter> _icoWriter;
    private readonly Mock<IIconCache> _cache;
    private readonly Mock<IShortcutGenerator> _shortcutGenerator;
    private readonly Mock<ILauncherPathResolver> _launcherResolver;
    private readonly Mock<IShellChangeNotifier> _shellChangeNotifier;
    private readonly Mock<IShortcutReader> _shortcutReader;

    public GroupSyncServiceTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.Sync." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);
        _paths = new AppDataPathProvider(_tempBase);

        _extractor = new Mock<IIconExtractor>();
        _extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns((BitmapSource?)null);

        _composer = new Mock<ICompositeIconGenerator>();
        _composer.Setup(c => c.GenerateComposite(It.IsAny<System.Collections.Generic.IReadOnlyList<BitmapSource>>(), It.IsAny<int>()))
                 .Returns(StubIcon());

        _icoWriter = new Mock<IIcoFileWriter>();
        _icoWriter.Setup(w => w.WriteAsync(It.IsAny<BitmapSource>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _cache = new Mock<IIconCache>();
        BitmapSource? unused;
        _cache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out unused)).Returns(false);

        _shortcutGenerator = new Mock<IShortcutGenerator>();
        _launcherResolver = new Mock<ILauncherPathResolver>();
        _launcherResolver.Setup(r => r.TryResolve()).Returns("C:/install/Launcher.exe");
        _shellChangeNotifier = new Mock<IShellChangeNotifier>();
        _shortcutReader = new Mock<IShortcutReader>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBase))
        {
            Directory.Delete(_tempBase, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static BitmapSource StubIcon()
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(System.Windows.Media.Brushes.Magenta, null, new System.Windows.Rect(0, 0, 32, 32));
        }
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private GroupSyncService CreateSut() => new(
        _paths,
        _extractor.Object,
        _composer.Object,
        _icoWriter.Object,
        _cache.Object,
        _shortcutGenerator.Object,
        _launcherResolver.Object,
        _shellChangeNotifier.Object,
        _shortcutReader.Object);

    /// <summary>
    /// Puts a placeholder anchor in the Start Menu folder and makes the reader report
    /// <paramref name="groupId"/>'s AUMID for it (none when <see langword="null"/>).
    /// </summary>
    private string StampAnchor(string fileStem, string? groupId)
    {
        Directory.CreateDirectory(_paths.StartMenuDirectory);
        var path = _paths.GetStartMenuShortcutFile(fileStem);
        File.WriteAllBytes(path, [0]);
        _shortcutReader
            .Setup(r => r.TryReadAumid(It.Is<string>(p =>
                string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))))
            .Returns(groupId is null ? null : GroupAumid.For(groupId));
        return path;
    }

    private GroupConfig SyncableGroup(string id, string name)
    {
        _extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns(StubIcon());
        return new GroupConfig
        {
            Id = id,
            GroupName = name,
            Apps = { new AppEntry { Name = "a", Path = "a.exe" } },
        };
    }

    [Fact]
    public async Task SyncAsync_NoOp_ForEmptyGroup()
    {
        var sut = CreateSut();
        var config = new GroupConfig { Id = "g", GroupName = "g" };

        await sut.SyncAsync(config);

        _icoWriter.VerifyNoOtherCalls();
        _shortcutGenerator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncAsync_NoOp_WhenLauncherCannotBeResolved()
    {
        _launcherResolver.Setup(r => r.TryResolve()).Returns((string?)null);
        var sut = CreateSut();
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps = { new AppEntry { Name = "a", Path = "a.exe" } },
        };

        await sut.SyncAsync(config);

        _shortcutGenerator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncAsync_NoOp_WhenNoIconsCouldBeExtracted()
    {
        _extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns((BitmapSource?)null);
        var sut = CreateSut();
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps = { new AppEntry { Name = "a", Path = "missing.exe" } },
        };

        await sut.SyncAsync(config);

        // Composer must not be called with an empty icon list
        _composer.VerifyNoOtherCalls();
        _shortcutGenerator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncAsync_WritesIcoAndShortcut_WhenIconsExtractSuccessfully()
    {
        _extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns(StubIcon());
        var sut = CreateSut();
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "Tools",
            Apps =
            {
                new AppEntry { Name = "a", Path = "a.exe" },
                new AppEntry { Name = "b", Path = "b.exe" },
            },
        };

        await sut.SyncAsync(config);

        _icoWriter.Verify(w => w.WriteAsync(
            It.IsAny<BitmapSource>(),
            _paths.GetGroupIconFile("g"),
            It.IsAny<CancellationToken>()), Times.Once);

        _shortcutGenerator.Verify(g => g.Generate(It.Is<GroupShortcutRequest>(r =>
            r.GroupId == "g" &&
            r.DisplayName == "Tools" &&
            r.TargetExePath == "C:/install/Launcher.exe" &&
            r.IconPath == _paths.GetGroupIconFile("g") &&
            r.ShortcutPath == _paths.GetGroupShortcutFile("g"))), Times.Once);

        // v0.4.1: also writes Start Menu anchor required by TaskbarManager.RequestPinCurrentAppAsync.
        _shortcutGenerator.Verify(g => g.Generate(It.Is<GroupShortcutRequest>(r =>
            r.GroupId == "g" &&
            r.DisplayName == "Tools" &&
            r.ShortcutPath == _paths.GetStartMenuShortcutFile("Tools"))), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_NotifiesShellOfStartMenuShortcutCreation()
    {
        // v0.4.2: SHChangeNotify must fire for the Start Menu anchor so the AppsFolder
        // index picks it up before the user's first pin-attempt races the indexer.
        _extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns(StubIcon());
        var sut = CreateSut();
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "Tools",
            Apps = { new AppEntry { Name = "a", Path = "a.exe" } },
        };

        await sut.SyncAsync(config);

        _shellChangeNotifier.Verify(
            n => n.NotifyCreate(_paths.GetStartMenuShortcutFile("Tools")),
            Times.Once);
    }

    [Fact]
    public void EnsureStartMenuShortcut_NotifiesShellWhenAnchorWritten()
    {
        // Heal-up path also has to flush the AppsFolder index; otherwise an upgraded
        // v0.4.0 user's first pin click still races even after the heal-up.
        Directory.CreateDirectory(_paths.IconsDirectory);
        File.WriteAllBytes(_paths.GetGroupIconFile("g"), [0]);

        var sut = CreateSut();
        var wrote = sut.EnsureStartMenuShortcut(new GroupConfig { Id = "g", GroupName = "Tools" });

        wrote.Should().BeTrue();
        _shellChangeNotifier.Verify(
            n => n.NotifyCreate(_paths.GetStartMenuShortcutFile("Tools")),
            Times.Once);
    }

    [Theory]
    [InlineData("Tools", "Tools")]
    [InlineData("My/Group:Name", "My-Group-Name")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("trailing-dots...", "trailing-dots")]
    [InlineData("", "fallback-id")]
    [InlineData("   ", "fallback-id")]
    public void SanitizeForFilename_ProducesSafeFilename(string input, string expected)
    {
        GroupSyncService.SanitizeForFilename(input, "fallback-id").Should().Be(expected);
    }

    [Fact]
    public void SanitizeForFilename_ClampsToSixtyChars()
    {
        var input = new string('a', 120);
        var result = GroupSyncService.SanitizeForFilename(input, "fallback");

        result.Length.Should().BeLessOrEqualTo(60);
    }

    [Fact]
    public void EnsureStartMenuShortcut_NoOp_WhenFileAlreadyExists()
    {
        Directory.CreateDirectory(_paths.StartMenuDirectory);
        var startMenuPath = _paths.GetStartMenuShortcutFile("Tools");
        File.WriteAllBytes(startMenuPath, [0]);

        var sut = CreateSut();
        var wrote = sut.EnsureStartMenuShortcut(new GroupConfig { Id = "g", GroupName = "Tools" });

        wrote.Should().BeFalse("file already present");
        _shortcutGenerator.VerifyNoOtherCalls();
    }

    [Fact]
    public void EnsureStartMenuShortcut_NoOp_WhenIconMissing()
    {
        // Heal-up case: Start Menu .lnk missing AND .ico missing (group never synced).
        // Reconciler defers to full Sync rather than writing a broken Start Menu entry.
        var sut = CreateSut();
        var wrote = sut.EnsureStartMenuShortcut(new GroupConfig { Id = "g", GroupName = "Tools" });

        wrote.Should().BeFalse("per-group icon absent — cannot anchor Start Menu .lnk");
        _shortcutGenerator.VerifyNoOtherCalls();
    }

    [Fact]
    public void EnsureStartMenuShortcut_WritesNewEntry_WhenIconPresentAndFileMissing()
    {
        // Heal-up case: v0.4.0 user upgraded to v0.4.1; the group's .ico already exists
        // from an earlier sync but the Start Menu anchor has never been written.
        Directory.CreateDirectory(_paths.IconsDirectory);
        File.WriteAllBytes(_paths.GetGroupIconFile("g"), [0]);

        var sut = CreateSut();
        var wrote = sut.EnsureStartMenuShortcut(new GroupConfig { Id = "g", GroupName = "Tools" });

        wrote.Should().BeTrue();
        _shortcutGenerator.Verify(g => g.Generate(It.Is<GroupShortcutRequest>(r =>
            r.GroupId == "g" &&
            r.ShortcutPath == _paths.GetStartMenuShortcutFile("Tools"))), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_CapsCompositeAtMaxTiles()
    {
        _extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns(StubIcon());
        var sut = CreateSut();
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps =
            {
                new AppEntry { Name = "1", Path = "1.exe" },
                new AppEntry { Name = "2", Path = "2.exe" },
                new AppEntry { Name = "3", Path = "3.exe" },
                new AppEntry { Name = "4", Path = "4.exe" },
                new AppEntry { Name = "5", Path = "5.exe" },
                new AppEntry { Name = "6", Path = "6.exe" },
            },
        };

        await sut.SyncAsync(config);

        // Only the first four are passed to the extractor (cap = CompositeIconGenerator.MaxTiles = 4)
        _extractor.Verify(e => e.ExtractIcon(It.IsAny<string>(), GroupSyncService.CompositeSourceIconSize), Times.Exactly(4));
    }

    [Fact]
    public void RemoveArtifacts_DeletesIconAndShortcut_IfPresent()
    {
        Directory.CreateDirectory(_paths.IconsDirectory);
        Directory.CreateDirectory(_paths.ShortcutsDirectory);
        Directory.CreateDirectory(_paths.StartMenuDirectory);
        var iconPath = _paths.GetGroupIconFile("g");
        var lnkPath = _paths.GetGroupShortcutFile("g");
        var startMenuPath = _paths.GetStartMenuShortcutFile("My Group");
        File.WriteAllBytes(iconPath, [0]);
        File.WriteAllBytes(lnkPath, [0]);
        File.WriteAllBytes(startMenuPath, [0]);

        var sut = CreateSut();
        sut.RemoveArtifacts("g", "My Group");

        File.Exists(iconPath).Should().BeFalse();
        File.Exists(lnkPath).Should().BeFalse();
        File.Exists(startMenuPath).Should().BeFalse("Start Menu anchor must be cleaned up too");
    }

    [Fact]
    public void RemoveArtifacts_IsNoOp_WhenFilesMissing()
    {
        var sut = CreateSut();

        var act = () => sut.RemoveArtifacts("never-existed", "Never Existed");

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SyncAsync_ReplacesAnchorWrittenUnderPreviousName()
    {
        // Issue #28: the anchor is named after the display name, so a rename used to leave
        // a second entry carrying the same AUMID beside the new one.
        var oldAnchor = StampAnchor("Old Name", "g");
        var sut = CreateSut();

        await sut.SyncAsync(SyncableGroup("g", "New Name"));

        _shortcutGenerator.Verify(g => g.Generate(It.Is<GroupShortcutRequest>(r =>
            r.ShortcutPath == _paths.GetStartMenuShortcutFile("New Name"))), Times.Once);
        File.Exists(oldAnchor).Should().BeFalse("the anchor under the old name carries the same AUMID");
    }

    [Fact]
    public async Task SyncAsync_LeavesAnchorsOfOtherGroupsAndForeignFiles()
    {
        var otherGroup = StampAnchor("Other", "h");
        var foreign = StampAnchor("Not ours", groupId: null);
        var sut = CreateSut();

        await sut.SyncAsync(SyncableGroup("g", "Tools"));

        File.Exists(otherGroup).Should().BeTrue();
        File.Exists(foreign).Should().BeTrue();
    }

    [Fact]
    public async Task SyncAsync_KeepsPreviousAnchor_WhenWritingTheNewOneFails()
    {
        // Until a replacement exists, the old anchor is what keeps a programmatic pin alive.
        var oldAnchor = StampAnchor("Old Name", "g");
        _shortcutGenerator
            .Setup(g => g.Generate(It.Is<GroupShortcutRequest>(r =>
                r.ShortcutPath == _paths.GetStartMenuShortcutFile("New Name"))))
            .Throws(new IOException("disk full"));
        var sut = CreateSut();

        await sut.SyncAsync(SyncableGroup("g", "New Name"));

        File.Exists(oldAnchor).Should().BeTrue();
    }

    [Fact]
    public void RemoveArtifacts_RemovesAnchorWrittenUnderEarlierName()
    {
        // Deleting a group must not leave its anchor behind just because the name it was
        // written under is no longer the name the Manager passes in.
        var earlier = StampAnchor("Earlier Name", "g");
        var sut = CreateSut();

        sut.RemoveArtifacts("g", "Current Name");

        File.Exists(earlier).Should().BeFalse();
    }

    [Fact]
    public void RemoveOrphanedStartMenuAnchors_RemovesOnlyAnchorsOfMissingGroups()
    {
        Directory.CreateDirectory(_paths.GroupsDirectory);
        File.WriteAllText(_paths.GetGroupFile("g"), "{}");
        // A config that no longer parses still owns its group: LoadAllAsync skips it, but
        // its tile may be pinned and the user may repair the file.
        File.WriteAllText(_paths.GetGroupFile("broken"), "not json");

        var live = StampAnchor("Live", "g");
        var unparsable = StampAnchor("Broken", "broken");
        var orphan = StampAnchor("Deleted", "gone");
        var foreign = StampAnchor("Not ours", groupId: null);
        var sut = CreateSut();

        var removed = sut.RemoveOrphanedStartMenuAnchors();

        removed.Should().Be(1);
        File.Exists(orphan).Should().BeFalse();
        File.Exists(live).Should().BeTrue();
        File.Exists(unparsable).Should().BeTrue();
        File.Exists(foreign).Should().BeTrue("files without a TaskbarFolders AUMID are not ours to delete");
    }

    [Fact]
    public async Task RenameThenDelete_WithRealShortcuts_LeavesNoAnchorBehind()
    {
        // The tests above stub the reader. This one writes real .lnk files through the shell
        // and reads their AUMIDs back, so the matching is proven against what
        // ShortcutGenerator actually stamps rather than against what a mock was told.
        var fakeLauncher = Path.Combine(_tempBase, "FakeLauncher.exe");
        File.WriteAllBytes(fakeLauncher, [0x4D, 0x5A]);
        _launcherResolver.Setup(r => r.TryResolve()).Returns(fakeLauncher);
        var sut = new GroupSyncService(
            _paths,
            _extractor.Object,
            _composer.Object,
            _icoWriter.Object,
            _cache.Object,
            new ShortcutGenerator(),
            _launcherResolver.Object,
            _shellChangeNotifier.Object,
            new ShortcutReader());
        var config = SyncableGroup("g", "Before");
        var unrelated = SyncableGroup("h", "Unrelated");

        await sut.SyncAsync(config);
        await sut.SyncAsync(unrelated);
        config.GroupName = "After";
        await sut.SyncAsync(config);

        Directory.EnumerateFiles(_paths.StartMenuDirectory, "*.lnk")
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo("After.lnk", "Unrelated.lnk");

        sut.RemoveArtifacts("g", "After");

        Directory.EnumerateFiles(_paths.StartMenuDirectory, "*.lnk")
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo("Unrelated.lnk");
    }

    [Fact]
    public void RemoveOrphanedStartMenuAnchors_IsNoOp_WhenStartMenuFolderMissing()
    {
        var sut = CreateSut();

        sut.RemoveOrphanedStartMenuAnchors().Should().Be(0);
    }
}
