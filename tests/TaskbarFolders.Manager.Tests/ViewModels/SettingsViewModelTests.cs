using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static SettingsViewModel CreateSut(
        AppSettings stored,
        bool autoStartEnabled,
        out Mock<IAppSettingsStore> storeMock,
        out Mock<IAutoStartService> autoMock,
        out Mock<IThemeService> themeMock)
    {
        storeMock = new Mock<IAppSettingsStore>();
        storeMock.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(stored);
        storeMock.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        autoMock = new Mock<IAutoStartService>();
        autoMock.SetupGet(a => a.IsEnabled).Returns(autoStartEnabled);

        themeMock = new Mock<IThemeService>();

        return new SettingsViewModel(
            storeMock.Object, autoMock.Object, themeMock.Object, new InstallLocation(Path.GetTempPath()), Mock.Of<IProcessRunner>(), Mock.Of<IUserConfirmation>());
    }

    private static SettingsViewModel CreateUninstallSut(
        string installRoot, out Mock<IProcessRunner> processes, out Mock<IUserConfirmation> confirmation)
    {
        processes = new Mock<IProcessRunner>();
        processes.Setup(p => p.TryStart(It.IsAny<ProcessStartInfo>())).Returns(true);
        confirmation = new Mock<IUserConfirmation>();

        return new SettingsViewModel(
            Mock.Of<IAppSettingsStore>(), Mock.Of<IAutoStartService>(), Mock.Of<IThemeService>(),
            new InstallLocation(installRoot), processes.Object, confirmation.Object);
    }

    private static string NewInstallRoot(string? withFile)
    {
        var root = Path.Combine(Path.GetTempPath(), "tf-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (withFile is not null)
        {
            File.WriteAllText(Path.Combine(root, withFile), "");
        }
        return root;
    }

    [Fact]
    public void Uninstall_StartsTheInstallersUninstaller_AndClosesTheManager()
    {
        var root = NewInstallRoot(InstallLocation.InstallerUninstallerName);
        try
        {
            var sut = CreateUninstallSut(root, out var processes, out _);
            var closing = false;
            sut.UninstallStarted += (_, _) => closing = true;

            sut.UninstallCommand.Execute(null);

            var expected = Path.Combine(root, InstallLocation.InstallerUninstallerName);
            processes.Verify(p => p.TryStart(It.Is<ProcessStartInfo>(psi => psi.FileName == expected)), Times.Once);
            closing.Should().BeTrue("the Manager's own files cannot be deleted while it runs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Uninstall_RunsTheManagerWithTheUninstallSwitch_ForAPortableCopy()
    {
        var root = NewInstallRoot(withFile: null);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Manager"));
            Directory.CreateDirectory(Path.Combine(root, "Launcher"));
            File.WriteAllText(Path.Combine(root, "Manager", "TaskbarFolders.Manager.exe"), "");
            File.WriteAllText(Path.Combine(root, "Launcher", "TaskbarFolders.Launcher.exe"), "");
            var sut = CreateUninstallSut(root, out var processes, out _);
            var closing = false;
            sut.UninstallStarted += (_, _) => closing = true;

            sut.UninstallCommand.Execute(null);

            var manager = Path.Combine(root, "Manager", "TaskbarFolders.Manager.exe");
            processes.Verify(p => p.TryStart(It.Is<ProcessStartInfo>(psi =>
                psi.FileName == manager && psi.Arguments == UninstallArguments.Uninstall)), Times.Once);
            closing.Should().BeTrue("the running Manager is one of the files to delete");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Uninstall_ExplainsAndStaysOpen_WhenThisCopyHasNoUninstaller()
    {
        var root = NewInstallRoot(withFile: null);
        try
        {
            var sut = CreateUninstallSut(root, out var processes, out var confirmation);
            var closing = false;
            sut.UninstallStarted += (_, _) => closing = true;

            sut.UninstallCommand.Execute(null);

            processes.Verify(p => p.TryStart(It.IsAny<ProcessStartInfo>()), Times.Never);
            confirmation.Verify(c => c.Notify(It.IsAny<string>(), It.Is<string>(m => m.Contains("--uninstall"))), Times.Once);
            closing.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ProjectsStoreFields_AndReadsAutoStartFromRegistry()
    {
        var stored = new AppSettings
        {
            Theme = ThemePreference.Dark,
            AutoStart = false, // store says off
            EnableAnimations = false,
            PopupPosition = PopupPositionPreference.Above,
        };
        var sut = CreateSut(stored, autoStartEnabled: true, out _, out _, out _);

        await sut.LoadAsync();

        sut.Theme.Should().Be(ThemePreference.Dark);
        sut.EnableAnimations.Should().BeFalse();
        sut.PopupPosition.Should().Be(PopupPositionPreference.Above);
        sut.AutoStart.Should().BeTrue("the registry is the source of truth — wins over the stored AppSettings.AutoStart");
    }

    [Fact]
    public async Task LoadAsync_AppliesNothing()
    {
        var sut = CreateSut(new AppSettings { Theme = ThemePreference.Dark }, autoStartEnabled: true, out var store, out var autoStart, out var theme);
        var saved = 0;
        sut.Saved += (_, _) => saved++;

        await sut.LoadAsync();
        await sut.WhenIdleAsync();

        store.Verify(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()), Times.Never,
            "showing the stored settings is not a change");
        autoStart.Verify(a => a.Enable(), Times.Never);
        autoStart.Verify(a => a.Disable(), Times.Never);
        theme.Verify(t => t.SetPreference(It.IsAny<ThemePreference>()), Times.Never);
        saved.Should().Be(0);
    }

    [Fact]
    public async Task ChangingTheTheme_SavesAndAppliesItAtOnce_WithoutASaveStep()
    {
        var sut = CreateSut(new AppSettings(), autoStartEnabled: false, out var store, out var autoStart, out var theme);
        await sut.LoadAsync();

        sut.Theme = ThemePreference.Light;
        await sut.WhenIdleAsync();

        store.Verify(s => s.SaveAsync(It.Is<AppSettings>(x => x.Theme == ThemePreference.Light), It.IsAny<CancellationToken>()), Times.Once);
        theme.Verify(t => t.SetPreference(ThemePreference.Light), Times.Once);
        autoStart.Verify(a => a.Enable(), Times.Never, "only the setting that changed has its side effect");
        autoStart.Verify(a => a.Disable(), Times.Never);
    }

    [Fact]
    public async Task ThePopupBackdrop_IsLoaded_AndSavedAsItChanges()
    {
        var sut = CreateSut(new AppSettings { PopupBackdrop = PopupBackdrop.Acrylic }, autoStartEnabled: false, out var store, out _, out _);
        await sut.LoadAsync();
        sut.PopupBackdrop.Should().Be(PopupBackdrop.Acrylic);

        sut.PopupBackdrop = PopupBackdrop.Off;
        await sut.WhenIdleAsync();

        store.Verify(s => s.SaveAsync(It.Is<AppSettings>(x => x.PopupBackdrop == PopupBackdrop.Off), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AChange_KeepsWhatTheDialogDoesNotShow()
    {
        // The Manager stores more in settings.json than this dialog edits; a change made here
        // must not reset the notice about old taskbar tiles to unseen.
        var sut = CreateSut(new AppSettings { OrphanedTilesNoticeShown = true }, autoStartEnabled: false, out var store, out _, out _);
        await sut.LoadAsync();

        sut.Theme = ThemePreference.Light;
        await sut.WhenIdleAsync();

        store.Verify(s => s.SaveAsync(It.Is<AppSettings>(x => x.Theme == ThemePreference.Light && x.OrphanedTilesNoticeShown), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TurningAutoStartOnAndOff_TouchesTheRegistryEachTime()
    {
        var sut = CreateSut(new AppSettings(), autoStartEnabled: false, out var store, out var autoStart, out _);
        await sut.LoadAsync();

        sut.AutoStart = true;
        await sut.WhenIdleAsync();
        sut.AutoStart = false;
        await sut.WhenIdleAsync();

        autoStart.Verify(a => a.Enable(), Times.Once);
        autoStart.Verify(a => a.Disable(), Times.Once);
        store.Verify(s => s.SaveAsync(It.Is<AppSettings>(x => x.AutoStart), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.SaveAsync(It.Is<AppSettings>(x => !x.AutoStart), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AutoStartThatCannotBeWritten_IsPutBack_ExplainedAndNotSaved()
    {
        var confirmation = new Mock<IUserConfirmation>();
        var store = new Mock<IAppSettingsStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings());
        var autoStart = new Mock<IAutoStartService>();
        autoStart.SetupGet(a => a.IsEnabled).Returns(false);
        autoStart.Setup(a => a.Enable()).Throws(new UnauthorizedAccessException("Run key is read-only."));
        var sut = new SettingsViewModel(
            store.Object, autoStart.Object, Mock.Of<IThemeService>(), new InstallLocation(Path.GetTempPath()),
            Mock.Of<IProcessRunner>(), confirmation.Object);
        await sut.LoadAsync();

        sut.AutoStart = true;
        await sut.WhenIdleAsync();

        sut.AutoStart.Should().BeFalse("a box that says on while the registry says off would lie");
        confirmation.Verify(c => c.Notify(It.IsAny<string>(), It.Is<string>(m => m.Contains("read-only"))), Times.Once);
        store.Verify(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        autoStart.Verify(a => a.Disable(), Times.Never, "putting the box back must not apply again");
    }

    [Fact]
    public async Task TwoChangesInARow_KeepBoth()
    {
        // The store writes the whole document: a second write built from stale values would
        // undo the first change.
        var sut = CreateSut(new AppSettings(), autoStartEnabled: false, out var store, out _, out _);
        var written = new List<AppSettings>();
        store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
             .Callback<AppSettings, CancellationToken>((a, _) => written.Add(a))
             .Returns(Task.CompletedTask);
        await sut.LoadAsync();

        sut.Theme = ThemePreference.Dark;
        sut.EnableAnimations = false;
        await sut.WhenIdleAsync();

        written.Last().Theme.Should().Be(ThemePreference.Dark);
        written.Last().EnableAnimations.Should().BeFalse();
    }

    [Fact]
    public async Task Saved_IsRaisedOnlyAfterTheStoreHasBeenWritten()
    {
        // ExplorerContextMenuService reads the setting back from the store when Saved fires.
        var sut = CreateSut(new AppSettings { ShowInExplorerMenu = false }, autoStartEnabled: false, out var store, out _, out _);
        var order = new List<string>();
        store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
             .Callback(() => order.Add("store"))
             .Returns(Task.CompletedTask);
        await sut.LoadAsync();
        sut.Saved += (_, _) => order.Add("saved");

        sut.ShowInExplorerMenu = true;
        await sut.WhenIdleAsync();

        order.Should().Equal("store", "saved");
    }

    [Fact]
    public async Task ASettingThatCannotBeStored_IsExplained_AndTheManagerKeepsRunning()
    {
        var confirmation = new Mock<IUserConfirmation>();
        var store = new Mock<IAppSettingsStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings());
        store.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new IOException("disk full"));
        var sut = new SettingsViewModel(
            store.Object, Mock.Of<IAutoStartService>(), Mock.Of<IThemeService>(), new InstallLocation(Path.GetTempPath()),
            Mock.Of<IProcessRunner>(), confirmation.Object);
        await sut.LoadAsync();

        sut.EnableAnimations = false;
        var act = () => sut.WhenIdleAsync();

        await act.Should().NotThrowAsync("nothing awaits a property setter that could catch it");
        confirmation.Verify(c => c.Notify(It.IsAny<string>(), It.Is<string>(m => m.Contains("disk full"))), Times.Once);
    }
}
