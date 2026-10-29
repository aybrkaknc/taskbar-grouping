using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

public class GroupEditorViewModelTests
{
    private static System.Windows.Media.Imaging.BitmapSource StubIcon() => TestImages.Solid(System.Windows.Media.Colors.Red);

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
                    Mock<IPinToTaskbarService> pinService) CreateSutWithCollaborators(
        Mock<ICustomGroupIconStore>? customIcons = null,
        Mock<IPinnedTileRemover>? pins = null)
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
            pinService.Object,
            (customIcons ?? new Mock<ICustomGroupIconStore>()).Object,
            (pins ?? new Mock<IPinnedTileRemover>()).Object);
        return (sut, extractor, composer, cache, store, syncService, paths, userConfirmation, pinService);
    }

    [Fact]
    public void Bind_ToAPinnedGroup_ShowsItPinned_AndTurnsThePinButtonOff()
    {
        // Pinning a pinned group again made Windows redraw its tile with a blank icon.
        var pins = new Mock<IPinnedTileRemover>();
        pins.Setup(p => p.IsPinned("g")).Returns(true);
        var (sut, _, _, _, _, _, _, _, _) = CreateSutWithCollaborators(pins: pins);

        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        sut.IsPinned.Should().BeTrue();
        sut.PinToTaskbarCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Bind_ToAnUnpinnedGroup_LeavesThePinButtonOn()
    {
        var (sut, _, _, _, _, _, _, _, _) = CreateSutWithCollaborators();

        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        sut.IsPinned.Should().BeFalse();
        sut.PinToTaskbarCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RefreshPinState_TurnsThePinButtonBackOn_AfterTheTileWasRemovedByHand()
    {
        var pins = new Mock<IPinnedTileRemover>();
        pins.SetupSequence(p => p.IsPinned("g")).Returns(true).Returns(false);
        var (sut, _, _, _, _, _, _, _, _) = CreateSutWithCollaborators(pins: pins);
        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        sut.RefreshPinState();

        sut.IsPinned.Should().BeFalse();
        sut.PinToTaskbarCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void PinButton_IsOff_WithNoGroupSelected()
    {
        var (sut, _, _, _, _, _, _, _, _) = CreateSutWithCollaborators();

        sut.Bind(null);

        sut.PinToTaskbarCommand.CanExecute(null).Should().BeFalse();
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
    public async Task PreviewRefresh_ComposesAPlaceholder_ForAnAppWithoutAnIcon()
    {
        var (sut, extractor, composer, _, _) = CreateSut();
        extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns((BitmapSource?)null);
        System.Collections.Generic.IReadOnlyList<BitmapSource>? composed = null;
        composer.Setup(c => c.GenerateComposite(It.IsAny<System.Collections.Generic.IReadOnlyList<BitmapSource>>(), It.IsAny<int>()))
                .Callback<System.Collections.Generic.IReadOnlyList<BitmapSource>, int>((icons, _) => composed = icons)
                .Returns(StubIcon());
        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        await sut.AddAppsCommand.ExecuteAsync(new[] { "a.exe" });

        var deadline = DateTime.UtcNow + GroupEditorViewModel.PreviewDebounce + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && sut.CompositeIconPreview is null)
        {
            await Task.Delay(50);
        }

        sut.Apps[0].Icon.Should().BeNull("the row draws its own themed placeholder");
        composed.Should().ContainSingle().Which.PixelWidth.Should().Be(GroupEditorViewModel.PreviewSize);
        sut.CompositeIconPreview.Should().NotBeNull();
    }

    [Fact]
    public async Task ChooseGroupIcon_StoresTheImage_ShowsIt_AndSyncs()
    {
        var customIcons = new Mock<ICustomGroupIconStore>();
        var chosen = StubIcon();
        customIcons.Setup(c => c.TryImportAsync("g", "pic.png", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        customIcons.Setup(c => c.TryLoad("g")).Returns(chosen);
        var (sut, _, _, _, _, syncService, _, _, _) = CreateSutWithCollaborators(customIcons);
        var config = new GroupConfig { Id = "g", GroupName = "g" };
        sut.Bind(new GroupListItemViewModel(config));

        await sut.ChooseGroupIconCommand.ExecuteAsync("pic.png");

        sut.HasCustomIcon.Should().BeTrue();
        sut.CompositeIconPreview.Should().BeSameAs(chosen);
        sut.ResetGroupIconCommand.CanExecute(null).Should().BeTrue();
        syncService.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChooseGroupIcon_ExplainsAnUnreadableFile_AndChangesNothing()
    {
        var customIcons = new Mock<ICustomGroupIconStore>();
        customIcons.Setup(c => c.TryImportAsync("g", "bad.png", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var (sut, _, _, _, _, syncService, _, userConfirmation, _) = CreateSutWithCollaborators(customIcons);
        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        await sut.ChooseGroupIconCommand.ExecuteAsync("bad.png");

        sut.HasCustomIcon.Should().BeFalse();
        userConfirmation.Verify(u => u.Notify(It.IsAny<string>(), It.Is<string>(m => m.Contains("bad.png"))), Times.Once);
        syncService.Verify(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetGroupIcon_DeletesTheChoice_RecomposesThePreview_AndSyncs()
    {
        var customIcons = new Mock<ICustomGroupIconStore>();
        customIcons.Setup(c => c.Exists("g")).Returns(true);
        var (sut, _, composer, _, _, syncService, _, _, _) = CreateSutWithCollaborators(customIcons);
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        sut.Bind(new GroupListItemViewModel(config));
        sut.HasCustomIcon.Should().BeTrue("the editor reads the choice when a group is bound");

        await sut.ResetGroupIconCommand.ExecuteAsync(null);

        customIcons.Verify(c => c.Delete("g"), Times.Once);
        sut.HasCustomIcon.Should().BeFalse();
        sut.ResetGroupIconCommand.CanExecute(null).Should().BeFalse();
        composer.Verify(c => c.GenerateComposite(It.IsAny<System.Collections.Generic.IReadOnlyList<BitmapSource>>(), GroupEditorViewModel.PreviewSize), Times.AtLeastOnce);
        syncService.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Bind_ReportsNoCustomIcon_ForAGroupWithoutOne()
    {
        var (sut, _, _, _, _) = CreateSut();

        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g" }));

        sut.HasCustomIcon.Should().BeFalse();
        sut.ResetGroupIconCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ToggleAppDetails_OpensOneRowAtATime_AndClosesItOnASecondToggle()
    {
        var (sut, _, _, _, _) = CreateSut();
        sut.Bind(new GroupListItemViewModel(new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps = { new AppEntry { Name = "a", Path = "a.exe" }, new AppEntry { Name = "b", Path = "b.exe" } },
        }));
        var (a, b) = (sut.Apps[0], sut.Apps[1]);

        sut.ToggleAppDetailsCommand.Execute(a);
        a.IsExpanded.Should().BeTrue();

        sut.ToggleAppDetailsCommand.Execute(b);
        a.IsExpanded.Should().BeFalse("opening another row closes this one");
        b.IsExpanded.Should().BeTrue();

        sut.ToggleAppDetailsCommand.Execute(b);
        b.IsExpanded.Should().BeFalse("a second toggle closes the open row");
    }

    [Fact]
    public void CollapseAppDetails_ClosesTheOpenRow()
    {
        var (sut, _, _, _, _) = CreateSut();
        sut.Bind(new GroupListItemViewModel(new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } }));
        sut.ToggleAppDetailsCommand.Execute(sut.Apps[0]);

        sut.CollapseAppDetailsCommand.Execute(null);

        sut.Apps[0].IsExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task EditingAnAppsNameOrArguments_SavesTheGroup_WithoutSyncing()
    {
        var (sut, _, _, _, store, syncService, _, _, _) = CreateSutWithCollaborators();
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        sut.Bind(new GroupListItemViewModel(config));

        sut.Apps[0].Name = "Alpha";
        sut.Apps[0].Arguments = "--x";
        await Task.Delay(50);

        config.Apps[0].Name.Should().Be("Alpha");
        config.Apps[0].Arguments.Should().Be("--x");
        store.Verify(s => s.SaveAsync(config, It.IsAny<CancellationToken>()), Times.Exactly(2));
        syncService.Verify(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never,
            "neither the name nor the arguments are part of the group's icon");
    }

    [Fact]
    public void Bind_ShowsTheGroupsGridSettings_WithoutSavingThem()
    {
        var (sut, _, _, _, store, _, _, _, _) = CreateSutWithCollaborators();
        var config = new GroupConfig { Id = "g", GroupName = "g", Columns = 5, IconSize = 64, ShowNames = false };

        sut.Bind(new GroupListItemViewModel(config));

        sut.Columns.Should().Be(5);
        sut.IconSize.Should().Be(64);
        sut.ShowNames.Should().BeFalse();
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never,
            "selecting a group is not an edit");
    }

    [Fact]
    public async Task ChangingAGridSetting_SavesTheGroup_WithoutSyncing()
    {
        var (sut, _, _, _, store, syncService, _, _, _) = CreateSutWithCollaborators();
        var config = new GroupConfig { Id = "g", GroupName = "g" };
        sut.Bind(new GroupListItemViewModel(config));

        sut.Columns = 4;
        sut.IconSize = 80;
        sut.ShowNames = false;
        await Task.Delay(50);

        config.Columns.Should().Be(4);
        config.IconSize.Should().Be(80);
        config.ShowNames.Should().BeFalse();
        store.Verify(s => s.SaveAsync(config, It.IsAny<CancellationToken>()), Times.Exactly(3));
        syncService.Verify(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never,
            "the popup reads these when it opens; the taskbar icon does not show them");
    }

    [Fact]
    public async Task ChangingAGridSetting_AfterRebinding_SavesOnlyTheNewGroup()
    {
        var (sut, _, _, _, store, _, _, _, _) = CreateSutWithCollaborators();
        var first = new GroupConfig { Id = "a", GroupName = "a", Columns = 2 };
        var second = new GroupConfig { Id = "b", GroupName = "b", Columns = 6 };
        sut.Bind(new GroupListItemViewModel(first));
        sut.Bind(new GroupListItemViewModel(second));

        sut.Columns = 3;
        await Task.Delay(50);

        first.Columns.Should().Be(2, "the group shown before keeps its own setting");
        second.Columns.Should().Be(3);
        store.Verify(s => s.SaveAsync(first, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GridOptions_OfferEveryAllowedValue()
    {
        GroupEditorViewModel.IconSizeOptions.Select(o => o.Value).Should().Equal(GroupConfig.IconSizes);
        GroupEditorViewModel.ColumnOptions.Select(o => o.Value)
            .Should().Equal(Enumerable.Range(GroupConfig.MinColumns, GroupConfig.MaxColumns - GroupConfig.MinColumns + 1));
    }

    [Fact]
    public async Task ChooseAppIcon_StoresTheImage_UsesIt_AndSyncs()
    {
        var customIcons = new Mock<ICustomGroupIconStore>();
        customIcons.Setup(c => c.TryImportAppIconAsync("g", "pic.png", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(@"C:\stored\g-0000000a.png");
        var (sut, _, _, _, store, syncService, _, _, _) = CreateSutWithCollaborators(customIcons);
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        sut.Bind(new GroupListItemViewModel(config));
        var app = sut.Apps[0];

        await sut.ChooseAppIconCommand.ExecuteAsync(new AppIconChoice(app, "pic.png"));

        config.Apps[0].IconPath.Should().Be(@"C:\stored\g-0000000a.png");
        app.HasCustomIcon.Should().BeTrue();
        store.Verify(s => s.SaveAsync(config, It.IsAny<CancellationToken>()), Times.Once);
        syncService.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once, "the group's icon shows the app's icon too");
        customIcons.Verify(c => c.PruneAppIcons("g", It.Is<System.Collections.Generic.IEnumerable<string?>>(k => k.Contains(@"C:\stored\g-0000000a.png"))), Times.Once);
    }

    [Fact]
    public async Task ChooseAppIcon_ExplainsAnUnreadableFile_AndChangesNothing()
    {
        var customIcons = new Mock<ICustomGroupIconStore>();
        customIcons.Setup(c => c.TryImportAppIconAsync("g", "bad.png", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var (sut, _, _, _, store, _, _, userConfirmation, _) = CreateSutWithCollaborators(customIcons);
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        sut.Bind(new GroupListItemViewModel(config));

        await sut.ChooseAppIconCommand.ExecuteAsync(new AppIconChoice(sut.Apps[0], "bad.png"));

        config.Apps[0].IconPath.Should().BeNull();
        userConfirmation.Verify(u => u.Notify(It.IsAny<string>(), It.Is<string>(m => m.Contains("bad.png"))), Times.Once);
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetAppIcon_GoesBackToTheProgramsIcon_AndPrunesTheImage()
    {
        var customIcons = new Mock<ICustomGroupIconStore>();
        var (sut, extractor, _, _, _, syncService, _, _, _) = CreateSutWithCollaborators(customIcons);
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe", IconPath = @"C:\gone.png" } } };
        sut.Bind(new GroupListItemViewModel(config));
        var app = sut.Apps[0];

        await sut.ResetAppIconCommand.ExecuteAsync(app);

        config.Apps[0].IconPath.Should().BeNull();
        app.HasCustomIcon.Should().BeFalse();
        app.Icon.Should().NotBeNull("the program's own icon is extracted again");
        syncService.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once);
        customIcons.Verify(c => c.PruneAppIcons("g", It.Is<System.Collections.Generic.IEnumerable<string?>>(k => k.All(p => p == null))), Times.Once);
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
            var pins = new Mock<IPinnedTileRemover>();
            pins.SetupSequence(p => p.IsPinned("g")).Returns(false).Returns(true);
            var (sut, _, _, _, _, syncService, paths, userConfirmation, pinService) = CreateSutWithCollaborators(pins: pins);
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
            sut.IsPinned.Should().BeTrue("the tile just landed");
            sut.PinToTaskbarCommand.CanExecute(null).Should().BeFalse("a second press made Windows blank the tile's icon");
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
