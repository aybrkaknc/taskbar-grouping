using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Core.Tests.Configuration;

public sealed class JsonGroupConfigStoreTests : IDisposable
{
    private readonly string _tempBase;
    private readonly AppDataPathProvider _paths;
    private readonly JsonGroupConfigStore _sut;

    public JsonGroupConfigStoreTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.Test." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);

        _paths = new AppDataPathProvider(_tempBase);
        _sut = new JsonGroupConfigStore(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBase))
        {
            Directory.Delete(_tempBase, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LoadAllAsync_ReturnsEmpty_WhenDirectoryMissing()
    {
        var result = await _sut.LoadAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var input = new GroupConfig
        {
            Id = "grp-1",
            GroupName = "Dev Tools",
            Columns = 4,
            Theme = ThemePreference.Dark,
            Apps =
            {
                new AppEntry { Name = "VS Code", Path = "C:/code/code.exe", IconPath = "C:/code/code.ico", Arguments = "--no-sandbox" },
                new AppEntry { Name = "Notepad", Path = "C:/Windows/notepad.exe" },
            },
        };

        await _sut.SaveAsync(input);
        var loaded = await _sut.LoadAsync("grp-1");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("grp-1");
        loaded.GroupName.Should().Be("Dev Tools");
        loaded.Columns.Should().Be(4);
        loaded.Theme.Should().Be(ThemePreference.Dark);
        loaded.Apps.Should().HaveCount(2);
        loaded.Apps[0].Name.Should().Be("VS Code");
        loaded.Apps[0].Path.Should().Be("C:/code/code.exe");
        loaded.Apps[0].IconPath.Should().Be("C:/code/code.ico");
        loaded.Apps[0].Arguments.Should().Be("--no-sandbox");
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenGroupDoesNotExist()
    {
        var result = await _sut.LoadAsync("never-saved");

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAllAsync_ReturnsAllSavedGroups()
    {
        await _sut.SaveAsync(new GroupConfig { Id = "a", GroupName = "A" });
        await _sut.SaveAsync(new GroupConfig { Id = "b", GroupName = "B" });
        await _sut.SaveAsync(new GroupConfig { Id = "c", GroupName = "C" });

        var all = await _sut.LoadAllAsync();

        all.Select(g => g.Id).Should().BeEquivalentTo("a", "b", "c");
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        await _sut.SaveAsync(new GroupConfig { Id = "to-delete", GroupName = "Bye" });
        File.Exists(_paths.GetGroupFile("to-delete")).Should().BeTrue();

        await _sut.DeleteAsync("to-delete");

        File.Exists(_paths.GetGroupFile("to-delete")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_IsNoOp_WhenFileMissing()
    {
        // Should not throw
        await _sut.DeleteAsync("ghost");
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        await _sut.SaveAsync(new GroupConfig { Id = "x", GroupName = "Old" });
        await _sut.SaveAsync(new GroupConfig { Id = "x", GroupName = "New" });

        var loaded = await _sut.LoadAsync("x");

        loaded!.GroupName.Should().Be("New");
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFileBehindOnSuccess()
    {
        await _sut.SaveAsync(new GroupConfig { Id = "atomic", GroupName = "T" });

        var tempFiles = Directory.EnumerateFiles(_paths.GroupsDirectory, "*.tmp");
        tempFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllAsync_IgnoresStrayTempFiles()
    {
        await _sut.SaveAsync(new GroupConfig { Id = "real", GroupName = "Real" });
        // Simulate a crashed write leaving a leftover .tmp file
        await File.WriteAllTextAsync(Path.Combine(_paths.GroupsDirectory, "leftover.json.tmp"), "{}");

        var all = await _sut.LoadAllAsync();

        all.Should().ContainSingle(g => g.Id == "real");
    }

    // A hand edit with a stray backslash crashed the Manager on start, and with it the
    // uninstaller, which loads the groups the same way.
    [Fact]
    public async Task LoadAllAsync_SkipsADamagedFile_AndReturnsTheRest()
    {
        await _sut.SaveAsync(new GroupConfig { Id = "real", GroupName = "Real" });
        await File.WriteAllTextAsync(
            Path.Combine(_paths.GroupsDirectory, "damaged.json"),
            """{ "groupName": "Broken", "apps": [ { "path": "C:\Windows" } ] }""");

        var all = await _sut.LoadAllAsync();

        all.Select(g => g.Id).Should().Equal("real");
        File.Exists(Path.Combine(_paths.GroupsDirectory, "damaged.json")).Should().BeTrue("the user may still repair it");
    }

    [Fact]
    public async Task LoadAsync_ReconstructsIdFromFilename_WhenJsonOmitsIt()
    {
        // Simulate a hand-edited config without an "id" property — the store should
        // recover the identifier from the file name (M1.4 plan note).
        Directory.CreateDirectory(_paths.GroupsDirectory);
        var file = _paths.GetGroupFile("hand-written");
        await File.WriteAllTextAsync(file, "{\"groupName\":\"Manual\"}");

        var loaded = await _sut.LoadAsync("hand-written");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("hand-written");
        loaded.GroupName.Should().Be("Manual");
    }
}
