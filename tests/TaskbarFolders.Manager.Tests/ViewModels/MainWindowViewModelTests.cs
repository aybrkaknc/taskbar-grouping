using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

public class MainWindowViewModelTests
{
    private static Mock<IGroupConfigStore> CreateStoreWith(params GroupConfig[] groups)
    {
        var mock = new Mock<IGroupConfigStore>(MockBehavior.Strict);
        mock.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<GroupConfig>)groups);
        mock.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static GroupEditorViewModel CreateEditor(IGroupConfigStore store)
    {
        return new GroupEditorViewModel(
            Mock.Of<IIconExtractor>(),
            Mock.Of<ICompositeIconGenerator>(),
            Mock.Of<IIconCache>(),
            store,
            Mock.Of<IGroupSyncService>(),
            Mock.Of<IAppDataPathProvider>(),
            Mock.Of<IUserConfirmation>(),
            Mock.Of<IPinToTaskbarService>(),
            Mock.Of<ICustomGroupIconStore>());
    }

    private static MainWindowViewModel CreateSut(Mock<IGroupConfigStore> store) =>
        CreateSut(store, alwaysConfirm: true);

    private static MainWindowViewModel CreateSut(
        Mock<IGroupConfigStore> store,
        bool alwaysConfirm,
        IGroupSyncService? syncService = null,
        IGroupIconReader? iconReader = null)
    {
        var confirmation = new Mock<IUserConfirmation>();
        confirmation.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(alwaysConfirm);
        return new MainWindowViewModel(
            store.Object,
            CreateEditor(store.Object),
            syncService ?? Mock.Of<IGroupSyncService>(),
            confirmation.Object,
            iconReader ?? Mock.Of<IGroupIconReader>());
    }

    private static System.Windows.Media.Imaging.BitmapSource StubIcon()
    {
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void Title_HasDefaultValue()
    {
        var store = CreateStoreWith();
        var sut = CreateSut(store);
        sut.Title.Should().Be("TaskbarFolders Manager");
    }

    [Fact]
    public async Task LoadGroupsAsync_PopulatesGroupsInAlphabeticalOrder()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "b", GroupName = "Browsers" },
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "c", GroupName = "code" });

        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();

        sut.Groups.Select(g => g.Name).Should().Equal("Apps", "Browsers", "code");
    }

    [Fact]
    public async Task LoadGroupsAsync_ReplacesPreviousContents()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "x", GroupName = "X" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.Groups.Should().HaveCount(1);

        // Second load with fresh data — old items must not linger.
        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync((IReadOnlyList<GroupConfig>)
                 [new GroupConfig { Id = "y", GroupName = "Y" }, new GroupConfig { Id = "z", GroupName = "Z" }]);

        await sut.LoadGroupsAsync();

        sut.Groups.Select(g => g.Id).Should().BeEquivalentTo("y", "z");
    }

    [Fact]
    public async Task NewGroup_CreatesSelectsAndAsksForRename()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "z", GroupName = "Zed" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        var renameRequests = 0;
        sut.RenameRequested += (_, _) => renameRequests++;

        await sut.NewGroupCommand.ExecuteAsync(null);

        store.Verify(s => s.SaveAsync(It.Is<GroupConfig>(c => c.GroupName == "New group"), It.IsAny<CancellationToken>()), Times.Once);
        sut.Groups.Select(g => g.Name).Should().Equal("Apps", "New group", "Zed");
        sut.SelectedGroup!.Name.Should().Be("New group");
        renameRequests.Should().Be(1, "the user names the group right after creating it");
    }

    [Fact]
    public async Task NewGroup_PicksTheNextFreeName()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "New group" },
            new GroupConfig { Id = "b", GroupName = "new group 2" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();

        await sut.NewGroupCommand.ExecuteAsync(null);

        sut.SelectedGroup!.Name.Should().Be("New group 3", "names compare without case, as the sidebar sorts them");
    }

    [Fact]
    public void NewGroupCommand_IsAlwaysExecutable()
    {
        // The old "+ Add" was disabled until a name was typed, and that state needed a tooltip
        // that could not show on a disabled button (v0.4.6). Nothing has to be typed now.
        var sut = CreateSut(CreateStoreWith());

        sut.NewGroupCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task BeginRename_SelectsTheGivenGroup_AndAsksForRename()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "b", GroupName = "Browsers" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];
        var renameRequests = 0;
        sut.RenameRequested += (_, _) => renameRequests++;

        sut.BeginRenameCommand.Execute(sut.Groups[1]);

        sut.SelectedGroup.Should().BeSameAs(sut.Groups[1], "the context menu acts on the row it opened on");
        sut.EditedGroupName.Should().Be("Browsers");
        renameRequests.Should().Be(1);
    }

    [Fact]
    public void BeginRename_DoesNothing_WithoutAGroup()
    {
        var sut = CreateSut(CreateStoreWith());
        var renameRequests = 0;
        sut.RenameRequested += (_, _) => renameRequests++;

        sut.BeginRenameCommand.Execute(null);

        renameRequests.Should().Be(0);
    }

    [Fact]
    public async Task LoadGroups_ReadsEachGroupsIcon()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "b", GroupName = "Browsers" });
        var icon = StubIcon();
        var reader = new Mock<IGroupIconReader>();
        reader.Setup(r => r.TryRead("a")).Returns(icon);
        var sut = CreateSut(store, alwaysConfirm: true, iconReader: reader.Object);

        await sut.LoadGroupsAsync();

        sut.Groups[0].Icon.Should().BeSameAs(icon);
        sut.Groups[1].Icon.Should().BeNull("a group without an icon file shows the folder glyph");
    }

    [Fact]
    public async Task AGroupsIcon_IsReread_AfterTheEditorSyncsIt()
    {
        var config = new GroupConfig { Id = "a", GroupName = "Apps" };
        var store = CreateStoreWith(config);
        var reader = new Mock<IGroupIconReader>();
        var sut = CreateSut(store, alwaysConfirm: true, iconReader: reader.Object);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];
        var updated = StubIcon();
        reader.Setup(r => r.TryRead("a")).Returns(updated);

        await sut.Editor.RemoveAppCommand.ExecuteAsync(null);
        sut.Groups[0].Icon.Should().BeNull("nothing was synced");

        config.Apps.Add(new AppEntry { Name = "x", Path = "x.exe" });
        sut.Editor.Bind(null);
        sut.Editor.Bind(sut.Groups[0]);
        await sut.Editor.RemoveAppCommand.ExecuteAsync(sut.Editor.Apps[0]);

        sut.Groups[0].Icon.Should().BeSameAs(updated);
    }

    [Fact]
    public async Task DeleteGroupAsync_RemovesAndCallsStore()
    {
        var configs = new[]
        {
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "b", GroupName = "Browsers" },
        };
        var store = CreateStoreWith(configs);
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();

        var toDelete = sut.Groups.Single(g => g.Id == "a");
        await sut.DeleteGroupCommand.ExecuteAsync(toDelete);

        store.Verify(s => s.DeleteAsync("a", It.IsAny<CancellationToken>()), Times.Once);
        sut.Groups.Select(g => g.Id).Should().ContainSingle().Which.Should().Be("b");
    }

    [Fact]
    public async Task DeleteGroupAsync_AdvancesSelection_ToNeighbourAtSameIndex()
    {
        var configs = new[]
        {
            new GroupConfig { Id = "a", GroupName = "A" },
            new GroupConfig { Id = "b", GroupName = "B" },
            new GroupConfig { Id = "c", GroupName = "C" },
        };
        var store = CreateStoreWith(configs);
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[1]; // B

        await sut.DeleteGroupCommand.ExecuteAsync(sut.Groups[1]);

        // B was at index 1 → selection moves to whatever is now at index 1 (the old C).
        sut.SelectedGroup.Should().NotBeNull();
        sut.SelectedGroup!.Id.Should().Be("c");
    }

    [Fact]
    public async Task DeleteGroupAsync_ClearsSelection_WhenLastGroupRemoved()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "only", GroupName = "Only" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];

        await sut.DeleteGroupCommand.ExecuteAsync(sut.Groups[0]);

        sut.Groups.Should().BeEmpty();
        sut.SelectedGroup.Should().BeNull();
    }

    [Fact]
    public async Task DeleteGroupAsync_NoOps_ForNullArgument()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "x", GroupName = "X" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();

        await sut.DeleteGroupCommand.ExecuteAsync(null);

        sut.Groups.Should().HaveCount(1);
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteGroupAsync_DoesNothing_WhenUserDeclinesConfirmation()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "x", GroupName = "X" });
        var sut = CreateSut(store, alwaysConfirm: false);
        await sut.LoadGroupsAsync();

        await sut.DeleteGroupCommand.ExecuteAsync(sut.Groups[0]);

        sut.Groups.Should().HaveCount(1, "user declined the confirmation prompt");
        store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommitRename_SavesAChangedName()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];

        sut.EditedGroupName = "Tools";
        await sut.CommitRenameCommand.ExecuteAsync(null);

        sut.Groups[0].Name.Should().Be("Tools");
        store.Verify(s => s.SaveAsync(It.Is<GroupConfig>(c => c.GroupName == "Tools"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Apps")]
    public async Task CommitRename_PutsTheCurrentNameBack_ForABlankOrUnchangedName(string typed)
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];

        sut.EditedGroupName = typed;
        await sut.CommitRenameCommand.ExecuteAsync(null);

        sut.EditedGroupName.Should().Be("Apps", "leaving the box blank is not a way to rename a group");
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelRename_DiscardsTheEdit()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];
        sut.EditedGroupName = "Half-typed";

        sut.CancelRenameCommand.Execute(null);

        sut.EditedGroupName.Should().Be("Apps");
        sut.Groups[0].Name.Should().Be("Apps");
    }

    [Fact]
    public async Task Rename_DoesNotWriteItsNameIntoTheBox_WhenTheSelectionMovedDuringTheSave()
    {
        // The box commits on focus loss; a click on another group moves the focus and the
        // selection while the save is still awaited.
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "b", GroupName = "Browsers" });
        var saving = new TaskCompletionSource();
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>())).Returns(saving.Task);
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        var apps = sut.Groups[0];
        var browsers = sut.Groups[1];
        sut.SelectedGroup = apps;

        sut.EditedGroupName = "Tools";
        var commit = sut.CommitRenameCommand.ExecuteAsync(null);
        sut.SelectedGroup = browsers;
        saving.SetResult();
        await commit;

        apps.Name.Should().Be("Tools");
        sut.EditedGroupName.Should().Be("Browsers", "the box now belongs to the group that was clicked");
        browsers.Name.Should().Be("Browsers");
    }

    [Fact]
    public async Task GroupListChanged_IsRaised_ForAddRenameAndDelete()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        var raised = 0;
        sut.GroupListChanged += (_, _) => raised++;

        await sut.NewGroupCommand.ExecuteAsync(null);
        sut.EditedGroupName = "Tools 2";
        await sut.RenameGroupCommand.ExecuteAsync(null);
        await sut.DeleteGroupCommand.ExecuteAsync(sut.SelectedGroup);

        raised.Should().Be(3, "Explorer's menu lists the groups by name, so each of these has to update it");
    }

    [Fact]
    public async Task RefreshFromStoreAsync_LeavesEverythingAlone_WhenNothingChangedOnDisk()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps", Apps = { new AppEntry { Name = "x", Path = "x.exe" } } });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        var item = sut.Groups[0];
        sut.SelectedGroup = item;
        sut.EditedGroupName = "Half-typed new na";

        // Same content, fresh objects — what a re-read of unchanged files produces.
        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync((IReadOnlyList<GroupConfig>)
                 [new GroupConfig { Id = "a", GroupName = "Apps", Apps = { new AppEntry { Name = "x", Path = "x.exe" } } }]);

        var rebuilt = await sut.RefreshFromStoreAsync();

        rebuilt.Should().BeFalse();
        sut.Groups.Single().Should().BeSameAs(item);
        sut.SelectedGroup.Should().BeSameAs(item);
        sut.EditedGroupName.Should().Be("Half-typed new na", "switching windows must not discard a rename in progress");
    }

    [Fact]
    public async Task RefreshFromStoreAsync_PicksUpAnAppAddedByThePopup_AndKeepsTheSelection()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps", Apps = { new AppEntry { Name = "x", Path = "x.exe" } } },
            new GroupConfig { Id = "b", GroupName = "Browsers" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups.Single(g => g.Id == "a");

        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync((IReadOnlyList<GroupConfig>)
             [
                 new GroupConfig
                 {
                     Id = "a",
                     GroupName = "Apps",
                     Apps = { new AppEntry { Name = "x", Path = "x.exe" }, new AppEntry { Name = "y", Path = "y.lnk" } },
                 },
                 new GroupConfig { Id = "b", GroupName = "Browsers" },
             ]);

        var rebuilt = await sut.RefreshFromStoreAsync();

        rebuilt.Should().BeTrue();
        sut.SelectedGroup.Should().NotBeNull();
        sut.SelectedGroup!.Id.Should().Be("a", "the selection follows the group, not the old object");
        sut.SelectedGroup.Config.Apps.Select(a => a.Path).Should().Equal("x.exe", "y.lnk");
        sut.Editor.Apps.Should().HaveCount(2, "the editor rebinds to the reloaded group");
    }

    // Closing the add-apps picker activates the window while the editor is still saving: the
    // re-read sees the file from before the save and would replace the group being edited.
    [Fact]
    public async Task RefreshFromStoreAsync_StandsDown_WhileTheEditorIsSaving()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps", Apps = { new AppEntry { Name = "x", Path = "x.exe" } } });
        var saving = new TaskCompletionSource();
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>())).Returns(saving.Task);
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        var item = sut.Groups[0];
        sut.SelectedGroup = item;

        // The file as it is before the save lands.
        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync((IReadOnlyList<GroupConfig>)
                 [new GroupConfig { Id = "a", GroupName = "Apps", Apps = { new AppEntry { Name = "x", Path = "x.exe" } } }]);

        var add = sut.Editor.AddAppsCommand.ExecuteAsync(new[] { "y.exe" });
        var rebuilt = await sut.RefreshFromStoreAsync();
        saving.SetResult();
        await add;

        rebuilt.Should().BeFalse();
        sut.SelectedGroup.Should().BeSameAs(item);
        sut.Editor.Apps.Should().HaveCount(2);
    }

    [Fact]
    public async Task RefreshFromStoreAsync_StandsDown_WhenASaveStartsDuringTheRead()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps", Apps = { new AppEntry { Name = "x", Path = "x.exe" } } });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        var item = sut.Groups[0];
        sut.SelectedGroup = item;

        var saving = new TaskCompletionSource();
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>())).Returns(saving.Task);
        var reading = new TaskCompletionSource<IReadOnlyList<GroupConfig>>();
        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(reading.Task);

        var refresh = sut.RefreshFromStoreAsync();
        var add = sut.Editor.AddAppsCommand.ExecuteAsync(new[] { "y.exe" });
        reading.SetResult([new GroupConfig { Id = "a", GroupName = "Apps", Apps = { new AppEntry { Name = "x", Path = "x.exe" } } }]);
        var rebuilt = await refresh;
        saving.SetResult();
        await add;

        rebuilt.Should().BeFalse("the file read predates the add");
        sut.SelectedGroup.Should().BeSameAs(item);
    }

    [Fact]
    public async Task RefreshFromStoreAsync_ClearsTheSelection_WhenTheSelectedGroupIsGone()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "b", GroupName = "Browsers" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups.Single(g => g.Id == "a");

        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync((IReadOnlyList<GroupConfig>)[new GroupConfig { Id = "b", GroupName = "Browsers" }]);

        await sut.RefreshFromStoreAsync();

        sut.Groups.Select(g => g.Id).Should().Equal("b");
        sut.SelectedGroup.Should().BeNull();
    }

    [Fact]
    public async Task RefreshFromStoreAsync_KeepsTheListInMemory_WhenTheStoreCannotBeRead()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();

        store.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new System.IO.IOException("share violation"));

        var rebuilt = await sut.RefreshFromStoreAsync();

        rebuilt.Should().BeFalse();
        sut.Groups.Select(g => g.Id).Should().Equal("a");
    }

    [Fact]
    public async Task SelectingAGroup_SeedsTheEditableName()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();

        sut.SelectedGroup = sut.Groups[0];

        sut.EditedGroupName.Should().Be("Apps");
        sut.RenameGroupCommand.CanExecute(null).Should().BeFalse("nothing has been changed yet");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Apps")]
    [InlineData("  Apps  ")]
    public async Task RenameGroupCommand_StaysDisabled_ForBlankOrUnchangedName(string name)
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];

        sut.EditedGroupName = name;

        sut.RenameGroupCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task RenameGroupAsync_PersistsResortsAndResyncs_KeepingIdentity()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "b", GroupName = "Browsers" },
            new GroupConfig { Id = "c", GroupName = "code" });
        var sync = new Mock<IGroupSyncService>();
        var sut = CreateSut(store, alwaysConfirm: true, sync.Object);
        await sut.LoadGroupsAsync();
        var renamed = sut.Groups[0];
        sut.SelectedGroup = renamed;

        sut.EditedGroupName = "  Zebra  ";
        sut.RenameGroupCommand.CanExecute(null).Should().BeTrue();
        await sut.RenameGroupCommand.ExecuteAsync(null);

        renamed.Name.Should().Be("Zebra");
        renamed.Config.GroupName.Should().Be("Zebra");
        renamed.Id.Should().Be("a", "the id carries the AUMID an existing pin depends on");
        store.Verify(s => s.SaveAsync(
            It.Is<GroupConfig>(c => c.Id == "a" && c.GroupName == "Zebra"),
            It.IsAny<CancellationToken>()), Times.Once);
        sync.Verify(s => s.SyncAsync(renamed.Config, It.IsAny<CancellationToken>()), Times.Once);
        sut.Groups.Select(g => g.Name).Should().Equal("Browsers", "code", "Zebra");
        sut.SelectedGroup.Should().BeSameAs(renamed, "a rename must not drop the selection");
        sut.RenameGroupCommand.CanExecute(null).Should().BeFalse("the saved name is now the current one");
    }

    [Fact]
    public async Task RenameGroupAsync_RestoresName_WhenSaveFails()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new System.IO.IOException("locked"));
        var sync = new Mock<IGroupSyncService>();
        var sut = CreateSut(store, alwaysConfirm: true, sync.Object);
        await sut.LoadGroupsAsync();
        sut.SelectedGroup = sut.Groups[0];
        sut.EditedGroupName = "Renamed";

        var act = () => sut.RenameGroupCommand.ExecuteAsync(null);

        await act.Should().ThrowAsync<System.IO.IOException>();
        sut.Groups[0].Name.Should().Be("Apps");
        sut.Groups[0].Config.GroupName.Should().Be("Apps");
        sync.Verify(s => s.SyncAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
