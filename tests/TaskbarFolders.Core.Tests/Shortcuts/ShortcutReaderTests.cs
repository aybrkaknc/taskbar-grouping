using System.IO;
using FluentAssertions;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Core.Tests.Shortcuts;

/// <summary>
/// Round-trips the AppUserModelID through a real <c>.lnk</c> on disk.
/// </summary>
/// <remarks>
/// The reader exists so a pin attempt can be verified instead of believed, and a verifier
/// that silently returns null would quietly turn every pin into "not confirmed". Writing with
/// <see cref="ShortcutGenerator"/> and reading back with <see cref="ShortcutReader"/> pins the
/// two halves to the same property key and string encoding.
/// </remarks>
public sealed class ShortcutReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _targetExe;
    private readonly string _iconFile;

    public ShortcutReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TaskbarFolders.ShortcutRead." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _iconFile = Path.Combine(RepoRoot.Path, "assets", "icons", "manager.ico");
        _targetExe = Path.Combine(_tempDir, "FakeLauncher.exe");
        File.WriteAllBytes(_targetExe, [0x4D, 0x5A]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryReadAumid_ReturnsTheAumidStampedByTheGenerator()
    {
        var shortcutPath = Path.Combine(_tempDir, "group.lnk");
        var expected = GroupAumid.For("abc123");

        new ShortcutGenerator().Generate(new GroupShortcutRequest(
            GroupId: "abc123",
            DisplayName: "Dev Tools",
            TargetExePath: _targetExe,
            IconPath: _iconFile,
            ShortcutPath: shortcutPath));

        new ShortcutReader().TryReadAumid(shortcutPath).Should().Be(expected);
    }

    [Fact]
    public void TryReadAumid_ReturnsNull_ForAShortcutWithoutOne()
    {
        // A .lnk that nothing stamped — the reader must say "no AUMID", not throw.
        var shortcutPath = Path.Combine(_tempDir, "plain.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        new ShortcutReader().TryReadAumid(shortcutPath).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryReadAumid_ReturnsNull_ForABlankPath(string path)
    {
        new ShortcutReader().TryReadAumid(path).Should().BeNull();
    }

    [Fact]
    public void TryReadAumid_ReturnsNull_ForAMissingFile()
    {
        new ShortcutReader().TryReadAumid(Path.Combine(_tempDir, "nope.lnk")).Should().BeNull();
    }
}
