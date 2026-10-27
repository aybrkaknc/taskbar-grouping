using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Services;

/// <summary>
/// The uninstall procedure. Registry, processes and the shell are mocks, so these tests never
/// touch the real HKCU, taskbar or running copies; the user-data and Start menu folders are
/// temp directories.
/// </summary>
public sealed class UninstallerTests : IDisposable
{
    private const string InstallRoot = @"C:\Users\u\AppData\Local\Programs\TaskbarFolders";

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "tf-uninstall-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IInstanceCloser> _instances = new();
    private readonly Mock<IShortcutReader> _reader = new();
    private readonly Mock<ITaskbarUnpinner> _unpinner = new();
    private readonly Mock<IAppSettingsStore> _settings = new();
    private readonly Mock<IAutoStartService> _autoStart = new();
    private readonly Mock<IRegistryWriter> _registry = new();
    private readonly Mock<IGroupConfigStore> _groups = new();
    private readonly Mock<IGroupSyncService> _sync = new();
    private readonly Mock<IAppDataPathProvider> _paths = new();
    private readonly List<string> _deletedValues = [];

    private string AppData => Path.Combine(_temp, "AppData");

    private string StartMenu => Path.Combine(_temp, "StartMenu");

    private string Pinned => Path.Combine(_temp, "Pinned");

    public UninstallerTests()
    {
        Directory.CreateDirectory(Path.Combine(AppData, "groups"));
        File.WriteAllText(Path.Combine(AppData, "groups", "a.json"), "{}");
        Directory.CreateDirectory(StartMenu);
        Directory.CreateDirectory(Pinned);

        _paths.SetupGet(p => p.AppDataRoot).Returns(AppData);
        _paths.SetupGet(p => p.StartMenuDirectory).Returns(StartMenu);
        _paths.SetupGet(p => p.PinnedTaskbarDirectory).Returns(Pinned);
        _settings.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings());
        _groups.Setup(g => g.LoadAllAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync((IReadOnlyList<GroupConfig>)
               [
                   new GroupConfig { Id = "a", GroupName = "Apps" },
                   new GroupConfig { Id = "b", GroupName = "Browsers" },
               ]);
        _unpinner.Setup(u => u.TryUnpin(It.IsAny<string>())).Returns(true);
        _registry.Setup(r => r.GetValueNames(It.IsAny<string>())).Returns([]);
        _registry.Setup(r => r.DeleteValue(It.IsAny<string>(), It.IsAny<string>()))
                 .Callback<string, string>((_, name) => _deletedValues.Add(name));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private Uninstaller CreateSut() => new(
        _instances.Object,
        _reader.Object,
        _unpinner.Object,
        _settings.Object,
        _autoStart.Object,
        new ExplorerContextMenuService(_registry.Object, _groups.Object, _settings.Object, Mock.Of<ILauncherPathResolver>()),
        _groups.Object,
        _sync.Object,
        _registry.Object,
        _paths.Object,
        new InstallLocation(InstallRoot));

    private string Pin(string fileName, string? aumid)
    {
        var path = Path.Combine(Pinned, fileName);
        File.WriteAllText(path, "");
        _reader.Setup(r => r.TryReadAumid(path)).Returns(aumid);
        return path;
    }

    [Fact]
    public async Task RunAsync_RemovesEveryTrace_AndReportsComplete()
    {
        var report = await CreateSut().RunAsync();

        report.Should().Be(new UninstallReport(Complete: true, TilesLeftPinned: 0));
        _instances.Verify(i => i.CloseOthers(), Times.Once);
        _autoStart.Verify(a => a.Disable(), Times.Once);
        _registry.Verify(r => r.DeleteTree(ExplorerContextMenuService.MenuKey("exefile")), Times.Once);
        _registry.Verify(r => r.DeleteTree(ExplorerContextMenuService.MenuKey("lnkfile")), Times.Once);
        _sync.Verify(s => s.RemoveArtifacts("a", "Apps"), Times.Once);
        _sync.Verify(s => s.RemoveArtifacts("b", "Browsers"), Times.Once);
        _sync.Verify(s => s.RemoveOrphanedStartMenuAnchors(), Times.Once);
        Directory.Exists(AppData).Should().BeFalse("groups, settings, icons and logs all go");
        Directory.Exists(StartMenu).Should().BeFalse("an empty Start menu folder of ours goes too");
    }

    [Fact]
    public async Task RunAsync_UnpinsGroupTilesOnly()
    {
        var ours = Pin("Apps.lnk", GroupAumid.For("a"));
        var orphan = Pin("Old group.lnk", GroupAumid.For("deleted-long-ago"));
        var foreign = Pin("Chrome.lnk", "Chrome");
        var unreadable = Pin("Broken.lnk", null);

        await CreateSut().RunAsync();

        _unpinner.Verify(u => u.TryUnpin(ours), Times.Once);
        _unpinner.Verify(u => u.TryUnpin(orphan), Times.Once);
        _unpinner.Verify(u => u.TryUnpin(foreign), Times.Never);
        _unpinner.Verify(u => u.TryUnpin(unreadable), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ReportsTilesTheShellWouldNotUnpin()
    {
        var stuck = Pin("Apps.lnk", GroupAumid.For("a"));
        _unpinner.Setup(u => u.TryUnpin(stuck)).Returns(false);

        var report = await CreateSut().RunAsync();

        report.Should().Be(new UninstallReport(Complete: false, TilesLeftPinned: 1));
    }

    [Fact]
    public async Task RunAsync_RemovesOnlyWindowsRecordsAboutThisCopy()
    {
        var muiCache = Uninstaller.WindowsRecordKeys[0];
        _registry.Setup(r => r.GetValueNames(muiCache)).Returns(
        [
            InstallRoot + @"\Manager\TaskbarFolders.Manager.exe.FriendlyAppName",
            InstallRoot + "Other\\app.exe.FriendlyAppName",
            @"C:\Windows\notepad.exe.FriendlyAppName",
            "TaskbarFolders.Group.a",
        ]);

        await CreateSut().RunAsync();

        _deletedValues.Should().BeEquivalentTo(
            InstallRoot + @"\Manager\TaskbarFolders.Manager.exe.FriendlyAppName",
            "TaskbarFolders.Group.a");
    }

    [Fact]
    public async Task RunAsync_CarriesOn_WhenAStepFails()
    {
        _instances.Setup(i => i.CloseOthers()).Returns(1);
        _autoStart.Setup(a => a.Disable()).Throws(new UnauthorizedAccessException("policy"));

        var report = await CreateSut().RunAsync();

        report.Complete.Should().BeFalse();
        _sync.Verify(s => s.RemoveArtifacts(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        Directory.Exists(AppData).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_LeavesAStartMenuFolderThatHoldsSomethingElse()
    {
        File.WriteAllText(Path.Combine(StartMenu, "someone else's.lnk"), "");

        await CreateSut().RunAsync();

        Directory.Exists(StartMenu).Should().BeTrue();
    }

    [Theory]
    [InlineData(InstallRoot + @"\Manager\TaskbarFolders.Manager.exe", true)]
    [InlineData(InstallRoot + @"\Launcher\TaskbarFolders.Launcher.exe.ApplicationCompany", true)]
    [InlineData(InstallRoot + "2\\Manager\\x.exe", false)]   // a sibling folder that merely starts alike
    [InlineData("taskbarfolders.group.abc", true)]
    [InlineData("TaskbarFolders.Groupie", false)]
    [InlineData(@"C:\Program Files\Other\app.exe", false)]
    public void IsOurRecord_MatchesThisCopyAndGroupIdentitiesOnly(string valueName, bool expected)
    {
        Uninstaller.IsOurRecord(valueName, InstallRoot).Should().Be(expected);
    }
}
