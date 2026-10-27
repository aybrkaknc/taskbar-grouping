using System;
using System.IO;
using FluentAssertions;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Shared.Configuration;
using Xunit;

namespace TaskbarFolders.Core.Tests.Groups;

public sealed class GroupIconFilesTests : IDisposable
{
    private readonly string _tempBase;
    private readonly AppDataPathProvider _paths;

    public GroupIconFilesTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.IconFiles." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);
        _paths = new AppDataPathProvider(_tempBase);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempBase, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void FindCurrent_ReturnsNull_WhenTheIconsFolderDoesNotExist()
    {
        GroupIconFiles.FindCurrent(_paths, "g").Should().BeNull();
    }

    [Fact]
    public void FindCurrent_PicksTheNewestOfTheGroupsOwnFiles()
    {
        var older = Write(_paths.GetGroupIconFile("g", "00000001"), DateTime.UtcNow.AddMinutes(-5));
        var newer = Write(_paths.GetGroupIconFile("g", "00000002"), DateTime.UtcNow.AddMinutes(-1));
        // Newer still, but not the group's: another group whose id merely starts with "g-",
        // and a file whose suffix is not a version.
        Write(_paths.GetGroupIconFile("g-other", "00000003"), DateTime.UtcNow);
        Write(Path.Combine(_paths.IconsDirectory, "g-notes.ico"), DateTime.UtcNow);

        GroupIconFiles.FindCurrent(_paths, "g").Should().Be(newer);
        older.Should().NotBe(newer);
    }

    [Fact]
    public void FindCurrent_ReturnsNull_ForAGroupWithoutFiles()
    {
        Write(_paths.GetGroupIconFile("other", "00000001"), DateTime.UtcNow);

        GroupIconFiles.FindCurrent(_paths, "g").Should().BeNull();
    }

    private static string Write(string path, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
