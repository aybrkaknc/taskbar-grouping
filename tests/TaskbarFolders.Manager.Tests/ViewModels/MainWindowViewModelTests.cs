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
            Mock.Of<IPinToTaskbarService>());
    }

    private static MainWindowViewModel CreateSut(Mock<IGroupConfigStore> store) =>
        CreateSut(store, alwaysConfirm: true);

    private static MainWindowViewModel CreateSut(
        Mock<IGroupConfigStore> store,
        bool alwaysConfirm,
        IGroupSyncService? syncService = null)
    {
        var confirmation = new Mock<IUserConfirmation>();
        confirmation.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(alwaysConfirm);
        return new MainWindowViewModel(
            store.Object,
            CreateEditor(store.Object),
            syncService ?? Mock.Of<IGroupSyncService>(),
            confirmation.Object);
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
    public async Task AddGroupAsync_PersistsAndAppendsAlphabetically()
    {
        var store = CreateStoreWith(
            new GroupConfig { Id = "a", GroupName = "Apps" },
            new GroupConfig { Id = "z", GroupName = "Zed" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();

        sut.NewGroupName = "Misc";
        await sut.AddGroupCommand.ExecuteAsync(null);

        store.Verify(s => s.SaveAsync(It.Is<GroupConfig>(c => c.GroupName == "Misc"), It.IsAny<CancellationToken>()), Times.Once);
        sut.Groups.Select(g => g.Name).Should().Equal("Apps", "Misc", "Zed");
        sut.NewGroupName.Should().BeEmpty("input is cleared after a successful add");
        sut.SelectedGroup.Should().NotBeNull();
        sut.SelectedGroup!.Name.Should().Be("Misc");
    }

    [Fact]
    public void AddGroupCommand_BecomesExecutable_AsSoonAsANameIsTyped()
    {
        // The button is bound straight to AddGroupCommand.CanExecute. If setting
        // NewGroupName does not raise CanExecuteChanged, the button stays greyed out
        // forever and no group can ever be created through the UI - the exact dead-button
        // failure v0.4.4 set out to remove, only permanent instead of silent.
        var sut = CreateSut(CreateStoreWith());

        sut.AddGroupCommand.CanExecute(null).Should().BeFalse("nothing has been typed yet");

        var raised = 0;
        sut.AddGroupCommand.CanExecuteChanged += (_, _) => raised++;

        sut.NewGroupName = "Dev Tools";

        raised.Should().BeGreaterThan(0, "the command must be told its executability changed");
        sut.AddGroupCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddGroupCommand_StaysDisabled_ForBlankName(string? name)
    {
        var sut = CreateSut(CreateStoreWith());

        sut.NewGroupName = name!;

        sut.AddGroupCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddGroupAsync_DoesNothing_ForBlankName(string? name)
    {
        var store = CreateStoreWith();
        var sut = CreateSut(store);
        sut.NewGroupName = name!;

        await sut.AddGroupCommand.ExecuteAsync(null);

        sut.Groups.Should().BeEmpty();
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddGroupAsync_TrimsWhitespaceAroundName()
    {
        var store = CreateStoreWith();
        var sut = CreateSut(store);
        sut.NewGroupName = "  Spaced  ";

        await sut.AddGroupCommand.ExecuteAsync(null);

        sut.Groups.Single().Name.Should().Be("Spaced");
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
    public async Task GroupListChanged_IsRaised_ForAddRenameAndDelete()
    {
        var store = CreateStoreWith(new GroupConfig { Id = "a", GroupName = "Apps" });
        var sut = CreateSut(store);
        await sut.LoadGroupsAsync();
        var raised = 0;
        sut.GroupListChanged += (_, _) => raised++;

        sut.NewGroupName = "Tools";
        await sut.AddGroupCommand.ExecuteAsync(null);
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
