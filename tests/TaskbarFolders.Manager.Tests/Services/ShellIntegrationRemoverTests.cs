using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Services;

/// <summary>
/// "Remove from this PC". The registry is a mock, so these tests never touch the real HKCU;
/// the Start menu folder is a temp directory.
/// </summary>
public sealed class ShellIntegrationRemoverTests : IDisposable
{
    private readonly Mock<IAppSettingsStore> _settings = new();
    private readonly Mock<IAutoStartService> _autoStart = new();
    private readonly Mock<IRegistryWriter> _registry = new();
    private readonly Mock<IGroupConfigStore> _groups = new();
    private readonly Mock<IGroupSyncService> _sync = new();
    private readonly Mock<IAppDataPathProvider> _paths = new();
    private readonly string _startMenu = Path.Combine(Path.GetTempPath(), "tf-remover-" + Guid.NewGuid().ToString("N"));
    private AppSettings? _saved;

    public ShellIntegrationRemoverTests()
    {
        _settings.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new AppSettings { ShowInExplorerMenu = true, AutoStart = true });
        _settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
                 .Callback<AppSettings, CancellationToken>((s, _) => _saved = s)
                 .Returns(Task.CompletedTask);
        _groups.Setup(g => g.LoadAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<GroupConfig>)
               [
                   new GroupConfig { Id = "a", GroupName = "Apps" },
                   new GroupConfig { Id = "b", GroupName = "Browsers" },
               ]);
        _paths.SetupGet(p => p.StartMenuDirectory).Returns(_startMenu);
    }

    public void Dispose()
    {
        if (Directory.Exists(_startMenu))
        {
            Directory.Delete(_startMenu, recursive: true);
        }
    }

    private ShellIntegrationRemover CreateSut() => new(
        _settings.Object,
        _autoStart.Object,
        new ExplorerContextMenuService(_registry.Object, _groups.Object, _settings.Object, Mock.Of<ILauncherPathResolver>()),
        _groups.Object,
        _sync.Object,
        _paths.Object);

    [Fact]
    public async Task RemoveAsync_RemovesEverythingOutsideTheFolder_AndKeepsTheGroups()
    {
        var complete = await CreateSut().RemoveAsync();

        complete.Should().BeTrue();
        _saved.Should().NotBeNull();
        _saved!.ShowInExplorerMenu.Should().BeFalse("the next Manager start must not write the menu back");
        _saved.AutoStart.Should().BeFalse();
        _autoStart.Verify(a => a.Disable(), Times.Once);
        _registry.Verify(r => r.DeleteTree(ExplorerContextMenuService.MenuKey("exefile")), Times.Once);
        _registry.Verify(r => r.DeleteTree(ExplorerContextMenuService.MenuKey("lnkfile")), Times.Once);
        _registry.Verify(r => r.SetString(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
        _sync.Verify(s => s.RemoveArtifacts("a", "Apps"), Times.Once);
        _sync.Verify(s => s.RemoveArtifacts("b", "Browsers"), Times.Once);
        _groups.Verify(g => g.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_CarriesOn_WhenAStepFails()
    {
        _autoStart.Setup(a => a.Disable()).Throws(new UnauthorizedAccessException("policy"));

        var complete = await CreateSut().RemoveAsync();

        complete.Should().BeFalse();
        _registry.Verify(r => r.DeleteTree(It.IsAny<string>()), Times.Exactly(2));
        _sync.Verify(s => s.RemoveArtifacts(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheStartMenuFolder_OnlyWhenNothingElseIsInIt()
    {
        Directory.CreateDirectory(_startMenu);
        await CreateSut().RemoveAsync();
        Directory.Exists(_startMenu).Should().BeFalse();

        Directory.CreateDirectory(_startMenu);
        File.WriteAllText(Path.Combine(_startMenu, "someone else's.lnk"), "");
        await CreateSut().RemoveAsync();
        Directory.Exists(_startMenu).Should().BeTrue("files that are not ours stay, and so does their folder");
    }
}
