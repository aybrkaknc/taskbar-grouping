using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Services;

/// <summary>
/// Files sent from Explorer or dropped on the taskbar tile. Uses the real JSON store in a temp
/// directory and the real named lock, because what has to hold is that concurrent launchers
/// do not lose each other's additions on disk.
/// </summary>
public sealed class CommandLineAppAdderTests : IDisposable
{
    private readonly string _tempBase;
    private readonly AppDataPathProvider _paths;
    private readonly JsonGroupConfigStore _store;
    private readonly Mock<IGroupSyncService> _sync = new();

    // A fresh id per test run: the lock is machine-wide and named after the group.
    private readonly string _groupId = "cli-" + Guid.NewGuid().ToString("N")[..12];

    public CommandLineAppAdderTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.CliAdd." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);
        _paths = new AppDataPathProvider(_tempBase);
        _store = new JsonGroupConfigStore(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBase))
        {
            Directory.Delete(_tempBase, recursive: true);
        }
    }

    private async Task SeedAsync(params string[] paths)
    {
        var config = new GroupConfig { Id = _groupId, GroupName = "Tools" };
        foreach (var path in paths)
        {
            config.Apps.Add(new AppEntry { Name = Path.GetFileNameWithoutExtension(path), Path = path });
        }

        await _store.SaveAsync(config);
    }

    private CommandLineAppAdder CreateSut() => new(_store, _sync.Object, new LauncherOptions(_groupId));

    [Fact]
    public async Task AddAsync_AddsNewApps_SkipsDuplicatesAndNonApps_ThenSavesAndSyncs()
    {
        await SeedAsync(@"C:\Tools\Existing.exe");

        var added = await CreateSut().AddAsync(
            [@"C:\Tools\New.lnk", @"C:\TOOLS\existing.EXE", @"C:\Docs\notes.txt"]);

        added.Should().Be(1, "the second path is already in the group (any case), the third is not an app");
        var saved = await _store.LoadAsync(_groupId);
        saved!.Apps.Select(a => a.Path).Should().Equal(@"C:\Tools\Existing.exe", @"C:\Tools\New.lnk");
        _sync.Verify(s => s.SyncAsync(It.Is<GroupConfig>(c => c.Apps.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_ChangesNothing_WhenNothingIsNew()
    {
        await SeedAsync(@"C:\Tools\Existing.exe");

        var added = await CreateSut().AddAsync([@"C:\Tools\Existing.exe"]);

        added.Should().Be(0);
        _sync.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddAsync_ReturnsZero_ForAGroupThatDoesNotExist()
    {
        var added = await CreateSut().AddAsync([@"C:\Tools\New.exe"]);

        added.Should().Be(0);
        _sync.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddAsync_FromConcurrentLaunchers_KeepsEveryAddition()
    {
        // A multi-selection in Explorer starts one launcher per file, all at once.
        await SeedAsync();
        var files = Enumerable.Range(1, 8).Select(i => $@"C:\Tools\App{i}.exe").ToArray();

        await Task.WhenAll(files.Select(file => Task.Run(() => CreateSut().AddAsync([file]))));

        var saved = await _store.LoadAsync(_groupId);
        saved!.Apps.Select(a => a.Path).Should().BeEquivalentTo(files, "no launcher may overwrite another's addition");
    }
}
