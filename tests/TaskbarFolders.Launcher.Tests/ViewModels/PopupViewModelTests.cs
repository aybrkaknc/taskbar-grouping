using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.ViewModels;

public class PopupViewModelTests
{
    private static BitmapSource StubIcon()
    {
        // RenderTargetBitmap is freezable and can be created on a non-UI thread for tests.
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

    private static (PopupViewModel sut,
                    Mock<IGroupConfigStore> store,
                    Mock<IIconExtractor> extractor,
                    Mock<IIconCache> cache,
                    Mock<IProcessLauncher> launcher) CreateSut(string groupId = "g1", GroupConfig? config = null, Mock<IGroupSyncService>? sync = null)
    {
        var store = new Mock<IGroupConfigStore>();
        store.Setup(s => s.LoadAsync(groupId, It.IsAny<CancellationToken>())).ReturnsAsync(config);

        var extractor = new Mock<IIconExtractor>();
        extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>())).Returns((BitmapSource?)null);

        var cache = new Mock<IIconCache>();
        BitmapSource? unused;
        cache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out unused)).Returns(false);

        var launcher = new Mock<IProcessLauncher>();
        launcher.Setup(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>())).Returns(true);

        var sut = new PopupViewModel(
            store.Object,
            extractor.Object,
            cache.Object,
            launcher.Object,
            (sync ?? new Mock<IGroupSyncService>()).Object,
            new LauncherOptions(groupId));

        return (sut, store, extractor, cache, launcher);
    }

    [Fact]
    public async Task LoadAsync_PopulatesNameColumnsAndApps_FromStoredGroup()
    {
        var config = new GroupConfig
        {
            Id = "g1",
            GroupName = "Tools",
            Columns = 4,
            Apps =
            {
                new AppEntry { Name = "VS Code", Path = "C:/code.exe", Arguments = "--no-sandbox" },
                new AppEntry { Name = "Notepad", Path = "C:/notepad.exe" },
            },
        };
        var (sut, _, _, _, _) = CreateSut("g1", config);

        await sut.LoadAsync();

        sut.GroupName.Should().Be("Tools");
        sut.Columns.Should().Be(4);
        sut.Apps.Should().HaveCount(2);
        sut.Apps[0].Name.Should().Be("VS Code");
        sut.Apps[0].Arguments.Should().Be("--no-sandbox");
        sut.IsUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_SetsIsUnavailable_WhenGroupNotFound()
    {
        var (sut, _, _, _, _) = CreateSut("missing", config: null);

        await sut.LoadAsync();

        sut.IsUnavailable.Should().BeTrue();
        sut.Apps.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_SetsIsUnavailable_WhenGroupHasNoApps()
    {
        var config = new GroupConfig { Id = "empty", GroupName = "Empty" };
        var (sut, _, _, _, _) = CreateSut("empty", config);

        await sut.LoadAsync();

        sut.IsUnavailable.Should().BeTrue();
        sut.GroupName.Should().Be("Empty");
    }

    [Fact]
    public async Task LoadAsync_DoesNotTouchCacheOrExtractor_AndLeavesAppIconsNull()
    {
        // v0.3 contract: LoadAsync is metadata-only. Icon work happens in StartIconLoad.
        // This test pins the contract so any future regression that re-introduces sync
        // extraction in LoadAsync is caught immediately.
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, extractor, cache, _) = CreateSut("g", config);

        await sut.LoadAsync();

        sut.Apps.Should().HaveCount(1);
        sut.Apps[0].Icon.Should().BeNull("LoadAsync does not extract icons in v0.3+");
        cache.Verify(c => c.TryGet(It.IsAny<string>(), It.IsAny<int>(), out It.Ref<BitmapSource?>.IsAny), Times.Never);
        extractor.Verify(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task StartIconLoad_AfterLoadAsync_InvokesExtractorForEachAppOnCacheMiss()
    {
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps =
            {
                new AppEntry { Name = "a", Path = "a.exe" },
                new AppEntry { Name = "b", Path = "b.exe" },
            },
        };
        var (sut, _, extractor, _, _) = CreateSut("g", config);

        // TCS gate: signal when both per-app extractor calls have landed so the assertion
        // is deterministic instead of timing-dependent.
        var calls = 0;
        var allDone = new TaskCompletionSource();
        extractor.Setup(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>()))
                 .Returns(() =>
                 {
                     if (Interlocked.Increment(ref calls) == 2)
                     {
                         allDone.SetResult();
                     }
                     return null;
                 });

        await sut.LoadAsync();
        sut.StartIconLoad();
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        extractor.Verify(e => e.ExtractIcon("a.exe", PopupViewModel.IconSize), Times.Once);
        extractor.Verify(e => e.ExtractIcon("b.exe", PopupViewModel.IconSize), Times.Once);
    }

    [Fact]
    public async Task StartIconLoad_ServesFromCache_WithoutInvokingExtractor()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, extractor, cache, _) = CreateSut("g", config);

        var stub = StubIcon();
        BitmapSource? hit = stub;
        cache.Setup(c => c.TryGet("a.exe", PopupViewModel.IconSize, out hit)).Returns(true);

        await sut.LoadAsync();
        sut.StartIconLoad();

        // No TCS gate needed — cache hits are synchronous; one tiny yield lets the per-app
        // task run and assign Icon, but the extractor must never have been called.
        await Task.Delay(50);

        sut.Apps[0].Icon.Should().BeSameAs(stub);
        extractor.Verify(e => e.ExtractIcon(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task StartIconLoad_StoresExtractedIcon_InCache()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, extractor, cache, _) = CreateSut("g", config);

        var stub = StubIcon();
        extractor.Setup(e => e.ExtractIcon("a.exe", PopupViewModel.IconSize)).Returns(stub);

        await sut.LoadAsync();
        sut.StartIconLoad();
        await Task.Delay(100);

        cache.Verify(c => c.Set("a.exe", PopupViewModel.IconSize, stub), Times.Once);
        sut.Apps[0].Icon.Should().BeSameAs(stub);
    }

    [Fact]
    public async Task CancelIconLoad_AfterStart_PreventsFurtherExtractorAssignment()
    {
        // Hard-gate the extractor on a manually completed TCS so the test controls timing.
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, extractor, _, _) = CreateSut("g", config);

        var release = new TaskCompletionSource();
        extractor.Setup(e => e.ExtractIcon("a.exe", PopupViewModel.IconSize))
                 .Returns(() =>
                 {
                     release.Task.GetAwaiter().GetResult();
                     return StubIcon();
                 });

        await sut.LoadAsync();
        sut.StartIconLoad();

        // Cancel BEFORE the extractor returns. The per-app task should observe cancellation
        // and skip the Icon = assignment even though ExtractIcon eventually produces a value.
        sut.CancelIconLoad();
        release.SetResult();

        // Give the canceled task time to finish its lifecycle.
        await Task.Delay(100);

        sut.Apps[0].Icon.Should().BeNull("cancellation must prevent the post-extract assignment");
    }

    [Fact]
    public void LaunchAppCommand_DoesNothing_ForNullArgument()
    {
        var (sut, _, _, _, launcher) = CreateSut();

        sut.LaunchAppCommand.Execute(null);

        launcher.Verify(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task LaunchAppCommand_InvokesLauncher_AndRaisesLaunchSucceeded()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe", Arguments = "-x" } } };
        var (sut, _, _, _, launcher) = CreateSut("g", config);
        await sut.LoadAsync();

        var succeeded = false;
        sut.LaunchSucceeded += (_, _) => succeeded = true;

        sut.LaunchAppCommand.Execute(sut.Apps[0]);

        launcher.Verify(l => l.Launch("a.exe", "-x"), Times.Once);
        succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AddAppsAsync_AddsAcceptedFiles_SavesAndResyncsTheGroup()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var sync = new Mock<IGroupSyncService>();
        var (sut, store, _, _, _) = CreateSut("g", config, sync);
        await sut.LoadAsync();

        var added = await sut.AddAppsAsync([@"C:\Tools\New.exe", @"C:\Docs\notes.txt", @"C:\Links\Other.lnk"]);

        added.Should().Be(2, "the .txt is not an app and is skipped");
        sut.Apps.Select(a => a.Name).Should().Equal("a", "New", "Other");
        store.Verify(s => s.SaveAsync(
            It.Is<GroupConfig>(c => c.Apps.Select(a => a.Path).SequenceEqual(new[] { "a.exe", @"C:\Tools\New.exe", @"C:\Links\Other.lnk" })),
            It.IsAny<CancellationToken>()), Times.Once);
        sync.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once,
            "the taskbar icon and shortcuts have to follow the new app list");
        sut.LastError.Should().BeNull();
    }

    [Fact]
    public async Task AddAppsAsync_DoesNothing_WhenNoPickedFileIsAnApp()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var sync = new Mock<IGroupSyncService>();
        var (sut, store, _, _, _) = CreateSut("g", config, sync);
        await sut.LoadAsync();

        var added = await sut.AddAppsAsync([@"C:\Docs\notes.txt"]);

        added.Should().Be(0);
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        sync.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddAppsAsync_MakesAnEmptyGroupAvailable()
    {
        var config = new GroupConfig { Id = "empty", GroupName = "Empty" };
        var (sut, _, _, _, _) = CreateSut("empty", config);
        await sut.LoadAsync();
        sut.IsUnavailable.Should().BeTrue();
        sut.CanEditGroup.Should().BeTrue("an empty group exists and can be filled from the popup");

        await sut.AddAppsAsync([@"C:\Tools\First.exe"]);

        sut.IsUnavailable.Should().BeFalse();
        sut.Apps.Should().ContainSingle();
    }

    [Fact]
    public async Task AddAppsAsync_IsRefused_ForAGroupThatDoesNotExist()
    {
        var (sut, store, _, _, _) = CreateSut("missing", config: null);
        await sut.LoadAsync();

        sut.CanEditGroup.Should().BeFalse();
        (await sut.AddAppsAsync([@"C:\Tools\New.exe"])).Should().Be(0);
        store.Verify(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddAppsAsync_ShowsAnError_WhenTheGroupCannotBeSaved()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var sync = new Mock<IGroupSyncService>();
        var (sut, store, _, _, _) = CreateSut("g", config, sync);
        store.Setup(s => s.SaveAsync(It.IsAny<GroupConfig>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new System.IO.IOException("locked"));
        await sut.LoadAsync();

        var act = () => sut.AddAppsAsync([@"C:\Tools\New.exe"]);

        await act.Should().NotThrowAsync("the popup has nobody above it to catch an exception");
        sut.LastError.Should().NotBeNullOrEmpty();
        sync.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ToggleEditCommand_SwitchesEditModeOnAndOff()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, _, _, _) = CreateSut("g", config);
        await sut.LoadAsync();

        sut.ToggleEditCommand.Execute(null);
        sut.IsEditing.Should().BeTrue();

        sut.ToggleEditCommand.Execute(null);
        sut.IsEditing.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleEditCommand_StaysOff_WhenThereIsNothingToEdit()
    {
        var (missing, _, _, _, _) = CreateSut("missing", config: null);
        await missing.LoadAsync();
        missing.ToggleEditCommand.Execute(null);
        missing.IsEditing.Should().BeFalse("a group that could not be loaded cannot be edited");

        var (empty, _, _, _, _) = CreateSut("empty", new GroupConfig { Id = "empty", GroupName = "Empty" });
        await empty.LoadAsync();
        empty.ToggleEditCommand.Execute(null);
        empty.IsEditing.Should().BeFalse("an empty group has no tile to remove");
    }

    [Fact]
    public async Task RemoveAppCommand_RemovesTheEntry_SavesAndResyncs()
    {
        var config = new GroupConfig
        {
            Id = "g",
            GroupName = "g",
            Apps = { new AppEntry { Name = "a", Path = "a.exe" }, new AppEntry { Name = "b", Path = "b.exe" } },
        };
        var sync = new Mock<IGroupSyncService>();
        var (sut, store, _, _, _) = CreateSut("g", config, sync);
        await sut.LoadAsync();
        sut.ToggleEditCommand.Execute(null);

        await sut.RemoveAppCommand.ExecuteAsync(sut.Apps[0]);

        sut.Apps.Select(a => a.Name).Should().Equal("b");
        store.Verify(s => s.SaveAsync(
            It.Is<GroupConfig>(c => c.Apps.Count == 1 && c.Apps[0].Path == "b.exe"),
            It.IsAny<CancellationToken>()), Times.Once);
        sync.Verify(s => s.SyncAsync(config, It.IsAny<CancellationToken>()), Times.Once);
        sut.IsEditing.Should().BeTrue("removing one of several apps keeps the user in edit mode");
        sut.IsUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAppCommand_LeavesEditMode_WhenTheLastAppGoes()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, _, _, _) = CreateSut("g", config);
        await sut.LoadAsync();
        sut.ToggleEditCommand.Execute(null);

        await sut.RemoveAppCommand.ExecuteAsync(sut.Apps[0]);

        sut.Apps.Should().BeEmpty();
        sut.IsUnavailable.Should().BeTrue("the popup shows the empty state, with + still available");
        sut.IsEditing.Should().BeFalse();
        config.Apps.Should().BeEmpty();
    }

    [Fact]
    public async Task LaunchAppCommand_DoesNothing_InEditMode()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, _, _, launcher) = CreateSut("g", config);
        await sut.LoadAsync();
        sut.ToggleEditCommand.Execute(null);
        var succeeded = false;
        sut.LaunchSucceeded += (_, _) => succeeded = true;

        sut.LaunchAppCommand.Execute(sut.Apps[0]);

        launcher.Verify(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        succeeded.Should().BeFalse("a click on a tile being edited must not start the app");
    }

    [Fact]
    public void CloseCommand_RaisesCloseRequested_WithoutLaunchingAnything()
    {
        var (sut, _, _, _, launcher) = CreateSut();
        var requested = 0;
        sut.CloseRequested += (_, _) => requested++;

        sut.CloseCommand.Execute(null);

        requested.Should().Be(1);
        launcher.Verify(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task LaunchAppCommand_DoesNotRaiseLaunchSucceeded_OnFailure()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "bad.exe" } } };
        var (sut, _, _, _, launcher) = CreateSut("g", config);
        launcher.Setup(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>())).Returns(false);
        await sut.LoadAsync();

        var succeeded = false;
        sut.LaunchSucceeded += (_, _) => succeeded = true;

        sut.LaunchAppCommand.Execute(sut.Apps[0]);

        succeeded.Should().BeFalse("the popup must stay open so the user can pick a different app or close manually");
    }

    [Fact]
    public async Task LaunchAppCommand_SetsLastError_OnFailure_WithAppName()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "Notepad", Path = "notepad.exe" } } };
        var (sut, _, _, _, launcher) = CreateSut("g", config);
        launcher.Setup(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>())).Returns(false);
        await sut.LoadAsync();

        sut.LaunchAppCommand.Execute(sut.Apps[0]);

        sut.LastError.Should().NotBeNull();
        sut.LastError.Should().Contain("Notepad");
    }

    [Fact]
    public async Task LaunchAppCommand_ClearsLastError_OnSuccess()
    {
        var config = new GroupConfig { Id = "g", GroupName = "g", Apps = { new AppEntry { Name = "a", Path = "a.exe" } } };
        var (sut, _, _, _, launcher) = CreateSut("g", config);
        await sut.LoadAsync();
        // Pre-set a sticky error from a previous failed click.
        sut.LastError = "Old error";
        launcher.Setup(l => l.Launch(It.IsAny<string>(), It.IsAny<string?>())).Returns(true);

        sut.LaunchAppCommand.Execute(sut.Apps[0]);

        sut.LastError.Should().BeNull();
    }
}
