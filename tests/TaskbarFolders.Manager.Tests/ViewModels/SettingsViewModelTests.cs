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
            storeMock.Object, autoMock.Object, themeMock.Object, Mock.Of<IShellIntegrationRemover>(), Mock.Of<IUserConfirmation>());
    }

    private static SettingsViewModel CreateRemovalSut(bool confirm, bool complete, out Mock<IShellIntegrationRemover> remover, out Mock<IUserConfirmation> confirmation)
    {
        remover = new Mock<IShellIntegrationRemover>();
        remover.Setup(r => r.RemoveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(complete);
        confirmation = new Mock<IUserConfirmation>();
        confirmation.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(confirm);

        return new SettingsViewModel(
            Mock.Of<IAppSettingsStore>(), Mock.Of<IAutoStartService>(), Mock.Of<IThemeService>(), remover.Object, confirmation.Object);
    }

    [Fact]
    public async Task RemoveFromThisPc_DoesNothing_WhenTheUserCancels()
    {
        var sut = CreateRemovalSut(confirm: false, complete: true, out var remover, out _);
        var closed = false;
        sut.RemovedFromThisPc += (_, _) => closed = true;

        await sut.RemoveFromThisPcCommand.ExecuteAsync(null);

        remover.Verify(r => r.RemoveAsync(It.IsAny<CancellationToken>()), Times.Never);
        closed.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "Done.")]
    [InlineData(false, "could not be removed")]
    public async Task RemoveFromThisPc_RemovesReportsAndClosesTheManager(bool complete, string expectedText)
    {
        var sut = CreateRemovalSut(confirm: true, complete, out var remover, out var confirmation);
        var closed = false;
        sut.RemovedFromThisPc += (_, _) => closed = true;

        await sut.RemoveFromThisPcCommand.ExecuteAsync(null);

        remover.Verify(r => r.RemoveAsync(It.IsAny<CancellationToken>()), Times.Once);
        confirmation.Verify(c => c.Notify(It.IsAny<string>(), It.Is<string>(m => m.Contains(expectedText))), Times.Once);
        closed.Should().BeTrue("left open, the Manager would write the Start menu entries back on the next sync");
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
        sut.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_DoesNotMarkDirty()
    {
        var sut = CreateSut(new AppSettings { Theme = ThemePreference.Dark }, autoStartEnabled: true, out _, out _, out _);

        await sut.LoadAsync();

        sut.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task MutatingAnyProperty_AfterLoad_SetsHasUnsavedChanges()
    {
        var sut = CreateSut(new AppSettings(), autoStartEnabled: false, out _, out _, out _);
        await sut.LoadAsync();

        sut.Theme = ThemePreference.Dark;

        sut.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_PersistsSettings_AndReconcilesAutoStart_AndAppliesTheme()
    {
        var sut = CreateSut(new AppSettings(), autoStartEnabled: false, out var store, out var autoStart, out var theme);
        await sut.LoadAsync();
        sut.AutoStart = true;
        sut.Theme = ThemePreference.Light;

        await sut.SaveCommand.ExecuteAsync(null);

        store.Verify(s => s.SaveAsync(
            It.Is<AppSettings>(x => x.Theme == ThemePreference.Light && x.AutoStart),
            It.IsAny<CancellationToken>()), Times.Once);
        autoStart.Verify(a => a.Enable(), Times.Once);
        autoStart.Verify(a => a.Disable(), Times.Never);
        theme.Verify(t => t.SetPreference(ThemePreference.Light), Times.Once);
        sut.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_DisablesAutoStart_WhenToggledOff()
    {
        var sut = CreateSut(
            new AppSettings { AutoStart = true },
            autoStartEnabled: true,
            out var store,
            out var autoStart,
            out _);
        await sut.LoadAsync();
        sut.AutoStart = false;

        await sut.SaveCommand.ExecuteAsync(null);

        store.Verify(s => s.SaveAsync(It.Is<AppSettings>(x => !x.AutoStart), It.IsAny<CancellationToken>()), Times.Once);
        autoStart.Verify(a => a.Disable(), Times.Once);
        autoStart.Verify(a => a.Enable(), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_ClearsHasUnsavedChanges()
    {
        var sut = CreateSut(new AppSettings(), autoStartEnabled: false, out _, out _, out _);
        await sut.LoadAsync();
        sut.EnableAnimations = false;
        sut.HasUnsavedChanges.Should().BeTrue();

        await sut.SaveCommand.ExecuteAsync(null);

        sut.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task ShowInExplorerMenu_IsLoaded_Saved_AndAnnounced()
    {
        var sut = CreateSut(new AppSettings { ShowInExplorerMenu = false }, autoStartEnabled: false, out var store, out _, out _);
        await sut.LoadAsync();
        sut.ShowInExplorerMenu.Should().BeFalse();
        var saved = 0;
        sut.Saved += (_, _) => saved++;

        sut.ShowInExplorerMenu = true;
        sut.HasUnsavedChanges.Should().BeTrue();
        await sut.SaveCommand.ExecuteAsync(null);

        store.Verify(s => s.SaveAsync(It.Is<AppSettings>(a => a.ShowInExplorerMenu), It.IsAny<CancellationToken>()), Times.Once);
        saved.Should().Be(1, "the Explorer menu is rewritten when the settings are saved");
    }
}
