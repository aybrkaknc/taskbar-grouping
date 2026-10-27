using System;
using System.Collections.Generic;
using System.Linq;
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
/// The registry layout behind Explorer's "Taskbar Groups" menu. A recording fake stands in for
/// the registry, so these tests never touch the real HKCU.
/// </summary>
public sealed class ExplorerContextMenuServiceTests
{
    private const string Launcher = @"C:\Apps\TaskbarFolders\Launcher\TaskbarFolders.Launcher.exe";

    private sealed class RecordingRegistry : IRegistryWriter
    {
        public List<string> Deleted { get; } = [];

        public Dictionary<(string Key, string? Name), string> Values { get; } = [];

        public List<string> Order { get; } = [];

        public Exception? ThrowOnWrite { get; set; }

        public void DeleteTree(string subKey)
        {
            Deleted.Add(subKey);
            Order.Add("delete " + subKey);
            foreach (var key in Values.Keys.Where(k => k.Key.StartsWith(subKey, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                Values.Remove(key);
            }
        }

        public void SetString(string subKey, string? name, string value)
        {
            if (ThrowOnWrite is not null)
            {
                throw ThrowOnWrite;
            }

            Values[(subKey, name)] = value;
            Order.Add("set " + subKey);
        }

        public IReadOnlyList<string> GetValueNames(string subKey) =>
            Values.Keys.Where(k => k.Key == subKey && k.Name is not null).Select(k => k.Name!).ToList();

        public void DeleteValue(string subKey, string name) => Values.Remove((subKey, name));
    }

    private readonly RecordingRegistry _registry = new();
    private readonly Mock<IGroupConfigStore> _groups = new();
    private readonly Mock<IAppSettingsStore> _settings = new();
    private readonly Mock<ILauncherPathResolver> _launcher = new();

    public ExplorerContextMenuServiceTests()
    {
        _settings.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings());
        _launcher.Setup(l => l.TryResolve()).Returns(Launcher);
    }

    private ExplorerContextMenuService CreateSut() =>
        new(_registry, _groups.Object, _settings.Object, _launcher.Object);

    private static List<GroupConfig> Groups(params (string Id, string Name)[] groups) =>
        groups.Select(g => new GroupConfig { Id = g.Id, GroupName = g.Name }).ToList();

    [Fact]
    public void Write_ListsEveryGroupAlphabetically_UnderBothFileClasses()
    {
        CreateSut().Write(Groups(("b1", "Tools"), ("a1", "browsers"), ("c1", "Dev & Test")), Launcher);

        foreach (var fileClass in new[] { "exefile", "lnkfile" })
        {
            var root = $@"Software\Classes\{fileClass}\shell\TaskbarFolders.Groups";
            _registry.Values[(root, "MUIVerb")].Should().Be("Taskbar Groups");
            _registry.Values[(root, "SubCommands")].Should().BeEmpty("an empty SubCommands makes the verb a cascading menu");
            _registry.Values[(root, "Icon")].Should().Be(Launcher + ",0");

            _registry.Values[($@"{root}\shell\01-a1", "MUIVerb")].Should().Be("browsers");
            _registry.Values[($@"{root}\shell\02-c1", "MUIVerb")].Should().Be("Dev && Test", "a lone & would turn into an accelerator");
            _registry.Values[($@"{root}\shell\03-b1", "MUIVerb")].Should().Be("Tools");
            _registry.Values[($@"{root}\shell\03-b1\command", null)]
                .Should().Be($"\"{Launcher}\" --group-id \"b1\" \"%1\"");
        }
    }

    [Fact]
    public void Write_RemovesTheOldMenuBeforeWriting_SoDeletedGroupsDisappear()
    {
        var sut = CreateSut();
        sut.Write(Groups(("old", "Gone soon"), ("keep", "Kept")), Launcher);

        sut.Write(Groups(("keep", "Kept")), Launcher);

        // "old" alone would match "TaskbarFolders"; the entry key ends in "-<id>".
        _registry.Values.Keys.Should().NotContain(k => k.Key.EndsWith("-old", StringComparison.Ordinal) || k.Key.Contains("-old\\", StringComparison.Ordinal));
        _registry.Values.Keys.Should().Contain(k => k.Key.EndsWith("01-keep", StringComparison.Ordinal));
        _registry.Order.FindLastIndex(o => o.StartsWith("delete", StringComparison.Ordinal))
            .Should().BeLessThan(_registry.Order.FindLastIndex(o => o.StartsWith("set", StringComparison.Ordinal)));
    }

    [Fact]
    public void Write_RemovesTheMenu_WhenThereAreNoGroups()
    {
        var sut = CreateSut();
        sut.Write(Groups(("a", "A")), Launcher);

        sut.Write([], Launcher);

        _registry.Values.Should().BeEmpty();
        _registry.Deleted.Should().Contain(@"Software\Classes\exefile\shell\TaskbarFolders.Groups")
            .And.Contain(@"Software\Classes\lnkfile\shell\TaskbarFolders.Groups");
    }

    [Fact]
    public void Write_RemovesTheMenu_WhenTheLauncherCannotBeFound()
    {
        CreateSut().Write(Groups(("a", "A")), launcherPath: null);

        _registry.Values.Should().BeEmpty("an entry that starts nothing must not be offered");
    }

    [Fact]
    public async Task RefreshAsync_RemovesTheMenu_WhenTheSettingIsOff()
    {
        _settings.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings { ShowInExplorerMenu = false });
        _groups.Setup(g => g.LoadAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Groups(("a", "A")));

        await CreateSut().RefreshAsync();

        _registry.Values.Should().BeEmpty();
        _registry.Deleted.Should().HaveCount(2);
    }

    [Fact]
    public async Task RefreshAsync_WritesTheStoredGroups_WhenTheSettingIsOn()
    {
        _groups.Setup(g => g.LoadAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Groups(("a", "Apps")));

        await CreateSut().RefreshAsync();

        _registry.Values[(@"Software\Classes\lnkfile\shell\TaskbarFolders.Groups\shell\01-a", "MUIVerb")].Should().Be("Apps");
    }

    [Fact]
    public async Task RefreshAsync_DoesNotThrow_WhenTheRegistryRefuses()
    {
        _groups.Setup(g => g.LoadAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Groups(("a", "Apps")));
        _registry.ThrowOnWrite = new UnauthorizedAccessException("policy");

        var act = () => CreateSut().RefreshAsync();

        await act.Should().NotThrowAsync("the menu is a convenience and must not break the Manager");
    }
}
