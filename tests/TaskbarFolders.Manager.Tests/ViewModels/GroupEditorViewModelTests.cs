using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

public class GroupEditorViewModelTests
{
    private static BitmapSource StubIcon()
    {
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 32, 32));
        }
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static (GroupEditorViewModel sut,
                    Mock<IIconExtractor> extractor,
                    Mock<ICompositeIconGenerator> composer,
                    Mock<IIconCache> cache,
                    Mock<IGroupConfigStore> store) CreateSut()
    {
        var (sut, extractor, composer, cache, store, _, _, _, _) = CreateSutWithCollaborators();
        return (sut, extractor, composer, cache, store);
    }

    private static (GroupEditorViewModel sut,
                    Mock<IIconExtractor> extractor,
                    Mock<ICompositeIconGenerator> composer,
                    Mock<IIconCache> cache,
                    Mock<IGroupConfigStore> store,
                    Mock<IGroupSyncService> syncService,
                    Mock<IAppDataPathProvider> paths,
                    Mock<IUserConfirmation> userConfirmation,
                    Mock<IPinToTaskbarService> pinService) CreateSutWithCollaborators()
    {
        var extractor = new Mock<IIconExtractor>();
        extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns(StubIcon());

        var composer = new Mock<ICompositeIconGenerator>();
        composer.Setup(c => c.GenerateComposite(It.IsAny<System.Collections.Generic.IReadOnlyList<BitmapSource>>(), It.IsAny<int>()))
                .Returns(StubIcon());

        var cache = new Mock<IIconCache>();
        BitmapSource? unused;
        cache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out unused)).Returns(false);

        var store = new Mock<IGroupConfigStore>();
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var syncService = new Mock<IGroupSyncService>();
        syncService.Setup(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var paths = new Mock<IAppDataPathProvider>();
        var userConfirmation = new Mock<IUserConfirmation>();
        var pinService = new Mock<IPinToTaskbarService>();
        pinService.Setup(p => p.PinAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(PinResult.Success);

        var sut = new GroupEditorViewModel(
            extractor.Object,
            composer.Object,
            cache.Object,
            store.Object,
            syncService.Object,
            paths.Object,
            userConfirmation.Object,
            pinService.Object);
        return (sut, extractor, composer, cache, store, syncService, paths, userConfirmation, pinService);
    }

    [Fact]
    public void Bind_Null_ClearsAppsAndPreview()
    {
        var (sut, _, _, _, _) = CreateSut();
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        sut.Bind(new GroupListItemViewModel(config));
        sut.Apps.Should().NotBeEmpty();

        sut.Bind(null);

        sut.Apps.Should().BeEmpty();
        sut.BoundItem.Should().BeNull();
        sut.CompositeIconPreview.Should().BeNull();
    }

    [Fact]
    public void Bind_PopulatesAppsFromConfig_AndExtractsIcons()
    {
        var (sut, extractor, _, _, _) = CreateSut();
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps =
            {
                new AppEntry { Name = "A", Path = "a.exe" },
                new AppEntry { Name = "B", Path = "b.exe" },
            },
        };

        sut.Bind(new GroupListItemViewModel(config));

        sut.Apps.Should().HaveCount(2);
        sut.Apps.Select(a => a.Name).Should().Equal("A", "B");
        extractor.Verify(e => e.ExtractIcon("a.exe", GroupEditorViewModel.PreviewSize), Times.Once);
        extractor.Verify(e => e.ExtractIcon("b.exe", GroupEditorViewModel.PreviewSize), Times.Once);
    }

    [Fact]
    public async Task AddAppsAsync_AppendsValidExeAndLnk_AndPersists()
    {
        var (sut, _, _, _, store) = CreateSut();
        var config = new GroupConfig { Id = "g", GroupName = "g" };
        var item = new GroupListItemViewModel(config);
        sut.Bind(item);

        await sut.AddAppsCommand.ExecuteAsync(new[] { "C:/x/notepad.exe", "C:/y/link.lnk" });

        sut.Apps.Should().HaveCount(2);
        config.Apps.Should().HaveCount(2, "the wrapper writes through to the underlying config");
        sut.Apps[0].Name.Should().Be("notepad");
        sut.Apps[1].Name.Should().Be("link");
        store.Verify(s => s.SaveAsync(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("C:/some/document.pdf")]
    [InlineData("C:/some/image.png")]
    [InlineData("C:/some/binary.dll")]
    public async Task AddAppsAsync_SkipsNonExecutableExtensions(string nonExePath)
    {
        var (sut, _, _, _, store) = CreateSut();
        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        await sut.AddAppsCommand.ExecuteAsync(new[] { nonExePath });

        sut.Apps.Should().BeEmpty();
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddAppsAsync_NoOp_WhenNothingBound()
    {
        var (sut, _, _, _, store) = CreateSut();

        await sut.AddAppsCommand.ExecuteAsync(new[] { "x.exe" });

        sut.Apps.Should().BeEmpty();
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The file picker returning re-activates the Manager, whose reload can rebind the editor
    // while the save is still running; v0.4.10 then dereferenced the cleared binding and the
    // Manager crashed with the app already saved.
    [Fact]
    public async Task AddAppsAsync_FinishesForItsOwnGroup_WhenTheEditorIsReboundDuringTheSave()
    {
        var (sut, _, _, _, store, syncService, _, _, _) = CreateSutWithCollaborators();
        var saving = new TaskCompletionSource();
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>())).Returns(saving.Task);
        var config = new GroupConfig { Id = "g", GroupName = "g" };
        var item = new GroupListItemViewModel(config);
        sut.Bind(item);

        var add = sut.AddAppsCommand.ExecuteAsync(new[] { "x.exe" });
        sut.IsSaving.Should().BeTrue();
        sut.Bind(null);
        saving.SetResult();

        await add.Invoking(t => t).Should().NotThrowAsync();
        syncService.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once);
        item.AppCount.Should().Be(1);
        sut.IsSaving.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAppAsync_FinishesForItsOwnGroup_WhenTheEditorIsReboundDuringTheSave()
    {
        var (sut, _, _, _, store, syncService, _, _, _) = CreateSutWithCollaborators();
        var saving = new TaskCompletionSource();
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>())).Returns(saving.Task);
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        sut.Bind(new GroupListItemViewModel(config));

        var remove = sut.RemoveAppCommand.ExecuteAsync(sut.Apps.Single());
        sut.Bind(null);
        saving.SetResult();

        await remove.Invoking(t => t).Should().NotThrowAsync();
        syncService.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAppAsync_RemovesFromBothViewModelAndConfig_AndPersists()
    {
        var (sut, _, _, _, store) = CreateSut();
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps = { new AppEntry { Name = "a", Path = "a.exe" } },
        };
        sut.Bind(new GroupListItemViewModel(config));
        var target = sut.Apps.Single();

        await sut.RemoveAppCommand.ExecuteAsync(target);

        sut.Apps.Should().BeEmpty();
        config.Apps.Should().BeEmpty();
        store.Verify(s => s.SaveAsync(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviewRefresh_DebouncesAndCallsComposer()
    {
        var (sut, _, composer, _, _) = CreateSut();
        var config = new GroupConfig { Id = "g", GroupName = "g" };
        sut.Bind(new GroupListItemViewModel(config));

        await sut.AddAppsCommand.ExecuteAsync(new[] { "a.exe", "b.exe", "c.exe" });

        // Poll with a generous deadline instead of a fixed post-debounce sleep: a loaded
        // CI runner can delay the debounce callback past a small fixed slack (same slow-CI
        // pattern as the StartBackgroundPrune 10 s deadline).
        var deadline = DateTime.UtcNow + GroupEditorViewModel.PreviewDebounce + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && sut.CompositeIconPreview is null)
        {
            await Task.Delay(50);
        }

        composer.Verify(
            c => c.GenerateComposite(It.IsAny<System.Collections.Generic.IReadOnlyList<BitmapSource>>(), GroupEditorViewModel.PreviewSize),
            Times.AtLeastOnce);
        sut.CompositeIconPreview.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_StopsRespondingToFurtherCollectionChanges()
    {
        var (sut, _, composer, _, _) = CreateSut();
        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        sut.Dispose();
        sut.Apps.Add(new AppEntryViewModel(new AppEntry { Name = "x", Path = "x.exe" }));

        // No exception, and the composer must not be invoked for the post-dispose mutation.
        composer.Verify(
            c => c.GenerateComposite(It.IsAny<System.Collections.Generic.IReadOnlyList<BitmapSource>>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveAppAsync_NoOp_ForNullArgument()
    {
        var (sut, _, _, _, store) = CreateSut();
        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        await sut.RemoveAppCommand.ExecuteAsync(null);

        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PinToTaskbar_RunsSyncForMissingShortcut_AndPinsWhenServiceReturnsSuccess()
    {
        // Shortcut is missing when the user clicks → EnsureShortcutExistsAsync triggers
        // SyncAsync (which mock-recreates the .lnk) → pin service runs and returns Success
        // → user sees the "Pinned" notification.
        var tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.PinOk." + Guid.NewGuid().ToString("N"));
        var shortcutsDir = Path.Combine(tempBase, "shortcuts");
        var shortcutPath = Path.Combine(shortcutsDir, "g.lnk");
        Directory.CreateDirectory(shortcutsDir);

        try
        {
            var (sut, _, _, _, _, syncService, paths, userConfirmation, pinService) = CreateSutWithCollaborators();
            paths.Setup(p => p.GetGroupShortcutFile("g")).Returns(shortcutPath);
            paths.Setup(p => p.ShortcutsDirectory).Returns(shortcutsDir);
            syncService
                .Setup(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    File.WriteAllBytes(shortcutPath, [0x4C]);
                    return Task.CompletedTask;
                });
            pinService.Setup(p => p.PinAsync("g", It.IsAny<CancellationToken>())).ReturnsAsync(PinResult.Success);

            sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));
            await sut.PinToTaskbarCommand.ExecuteAsync(null);

            syncService.Verify(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Once);
            pinService.Verify(p => p.PinAsync("g", It.IsAny<CancellationToken>()), Times.Once);
            userConfirmation.Verify(u => u.Notify("Pinned", It.IsAny<string>()), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PinToTaskbar_Notifies_WhenShortcutCannotBeCreated()
    {
        // Sync runs but produces no .lnk → user must see EnsureShortcutExistsAsync's
        // "Shortcut not available" notify; pin service must never be called.
        var tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.PinFail." + Guid.NewGuid().ToString("N"));
        var shortcutsDir = Path.Combine(tempBase, "shortcuts");
        var shortcutPath = Path.Combine(shortcutsDir, "g.lnk");
        Directory.CreateDirectory(shortcutsDir);

        try
        {
            var (sut, _, _, _, _, syncService, paths, userConfirmation, pinService) = CreateSutWithCollaborators();
            paths.Setup(p => p.GetGroupShortcutFile("g")).Returns(shortcutPath);
            paths.Setup(p => p.ShortcutsDirectory).Returns(shortcutsDir);
            // Default sync mock returns Task.CompletedTask without touching the filesystem.

            sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));
            await sut.PinToTaskbarCommand.ExecuteAsync(null);

            syncService.Verify(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Once);
            userConfirmation.Verify(u => u.Notify("Shortcut not available", It.IsAny<string>()), Times.Once);
            pinService.Verify(p => p.PinAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PinToTaskbar_NoOp_WhenNothingBound()
    {
        var (sut, _, _, _, _, syncService, _, userConfirmation, pinService) = CreateSutWithCollaborators();

        await sut.PinToTaskbarCommand.ExecuteAsync(null);

        syncService.Verify(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        pinService.Verify(p => p.PinAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        userConfirmation.Verify(u => u.Notify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PinToTaskbar_UserDenied_DoesNotNotify()
    {
        // User clicked Cancel in the system pin dialog — they made the choice, no follow-up
        // Notify needed (would be annoying).
        var tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.PinDenied." + Guid.NewGuid().ToString("N"));
        var shortcutsDir = Path.Combine(tempBase, "shortcuts");
        var shortcutPath = Path.Combine(shortcutsDir, "g.lnk");
        Directory.CreateDirectory(shortcutsDir);
        File.WriteAllBytes(shortcutPath, [0x4C]); // shortcut exists upfront

        try
        {
            var (sut, _, _, _, _, _, paths, userConfirmation, pinService) = CreateSutWithCollaborators();
            paths.Setup(p => p.GetGroupShortcutFile("g")).Returns(shortcutPath);
            paths.Setup(p => p.ShortcutsDirectory).Returns(shortcutsDir);
            pinService.Setup(p => p.PinAsync("g", It.IsAny<CancellationToken>())).ReturnsAsync(PinResult.UserDenied);

            sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));
            await sut.PinToTaskbarCommand.ExecuteAsync(null);

            pinService.Verify(p => p.PinAsync("g", It.IsAny<CancellationToken>()), Times.Once);
            userConfirmation.Verify(u => u.Notify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PinToTaskbar_Error_NotifiesPinFailed()
    {
        // The branch the user hit on every attempt from v0.4.0 to v0.4.8 while the pin call
        // threw, and the only PinResult that had no test at all.
        var tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.PinErr." + Guid.NewGuid().ToString("N"));
        var shortcutsDir = Path.Combine(tempBase, "shortcuts");
        var shortcutPath = Path.Combine(shortcutsDir, "g.lnk");
        Directory.CreateDirectory(shortcutsDir);
        File.WriteAllBytes(shortcutPath, [0x4C]);

        try
        {
            var (sut, _, _, _, _, _, paths, userConfirmation, pinService) = CreateSutWithCollaborators();
            paths.Setup(p => p.GetGroupShortcutFile("g")).Returns(shortcutPath);
            paths.Setup(p => p.ShortcutsDirectory).Returns(shortcutsDir);
            pinService.Setup(p => p.PinAsync("g", It.IsAny<CancellationToken>())).ReturnsAsync(PinResult.Error);

            sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

            var act = async () => await sut.PinToTaskbarCommand.ExecuteAsync(null);
            await act.Should().NotThrowAsync();

            userConfirmation.Verify(u => u.Notify("Pin failed", It.IsAny<string>()), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PinToTaskbar_NotVerified_NotifiesPinNotConfirmed()
    {
        // Windows claimed success but no tile carrying the group's AUMID was found. The user
        // must not be told \"Pinned\" when nothing appeared.
        var tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.PinNotVer." + Guid.NewGuid().ToString("N"));
        var shortcutsDir = Path.Combine(tempBase, "shortcuts");
        var shortcutPath = Path.Combine(shortcutsDir, "g.lnk");
        Directory.CreateDirectory(shortcutsDir);
        File.WriteAllBytes(shortcutPath, [0x4C]);

        try
        {
            var (sut, _, _, _, _, _, paths, userConfirmation, pinService) = CreateSutWithCollaborators();
            paths.Setup(p => p.GetGroupShortcutFile("g")).Returns(shortcutPath);
            paths.Setup(p => p.ShortcutsDirectory).Returns(shortcutsDir);
            pinService.Setup(p => p.PinAsync("g", It.IsAny<CancellationToken>())).ReturnsAsync(PinResult.NotVerified);

            sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

            var act = async () => await sut.PinToTaskbarCommand.ExecuteAsync(null);
            await act.Should().NotThrowAsync();

            userConfirmation.Verify(u => u.Notify("Pin not confirmed", It.IsAny<string>()), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PinToTaskbar_Unsupported_NotifiesAndDoesNotThrow()
    {
        // TaskbarManager unsupported (restricted SKU / policy) → user gets a clear
        // "Pin not available" message with instructions to use the manual fallback.
        var tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.PinUnsupp." + Guid.NewGuid().ToString("N"));
        var shortcutsDir = Path.Combine(tempBase, "shortcuts");
        var shortcutPath = Path.Combine(shortcutsDir, "g.lnk");
        Directory.CreateDirectory(shortcutsDir);
        File.WriteAllBytes(shortcutPath, [0x4C]);

        try
        {
            var (sut, _, _, _, _, _, paths, userConfirmation, pinService) = CreateSutWithCollaborators();
            paths.Setup(p => p.GetGroupShortcutFile("g")).Returns(shortcutPath);
            paths.Setup(p => p.ShortcutsDirectory).Returns(shortcutsDir);
            pinService.Setup(p => p.PinAsync("g", It.IsAny<CancellationToken>())).ReturnsAsync(PinResult.Unsupported);

            sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

            // The Explorer fallback is best-effort: Process.Start("explorer.exe", ...) is
            // wrapped in `using var process = Process.Start(...)` which can return null on
            // failure but never throws. Test only the Notify contract here.
            var act = async () => await sut.PinToTaskbarCommand.ExecuteAsync(null);
            await act.Should().NotThrowAsync();

            userConfirmation.Verify(u => u.Notify("Pin not available", It.IsAny<string>()), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }
}
