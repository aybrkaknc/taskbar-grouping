using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using TaskbarFolders.Core.Interop;
using TaskbarFolders.Core.Shortcuts;
using Xunit;

namespace TaskbarFolders.Core.Tests.Shortcuts;

public sealed class ShortcutGeneratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _targetExe;
    private readonly string _iconFile;

    public ShortcutGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TaskbarFolders.Shortcuts." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Use the M0 assets/icons/manager.ico as a real icon file — its existence is
        // verified by the existing icon-extractor tests.
        var repoRoot = FindRepoRoot();
        _iconFile = Path.Combine(repoRoot, "assets", "icons", "manager.ico");
        File.Exists(_iconFile).Should().BeTrue($"the M0 manager.ico must live at {_iconFile} for shortcut-icon tests");

        // Fake target — does not need to exist for shortcut creation; Windows resolves at click time.
        _targetExe = Path.Combine(_tempDir, "FakeLauncher.exe");
        File.WriteAllBytes(_targetExe, [0x4D, 0x5A]); // tiny placeholder so the path is real
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskbarFolders.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate TaskbarFolders.sln above the test assembly.");
    }

    [Theory]
    [InlineData("abc", "TaskbarFolders.Group.abc")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", "TaskbarFolders.Group.550e8400-e29b-41d4-a716-446655440000")]
    public void BuildAumid_FormatsWithPrefix(string groupId, string expected)
    {
        var sut = new ShortcutGenerator();
        sut.BuildAumid(groupId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BuildAumid_ThrowsForBlankGroupId(string? groupId)
    {
        var sut = new ShortcutGenerator();
        var act = () => sut.BuildAumid(groupId!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_CreatesLnkFile_AtRequestedPath()
    {
        var sut = new ShortcutGenerator();
        var shortcutPath = Path.Combine(_tempDir, "out.lnk");

        sut.Generate(new GroupShortcutRequest(
            GroupId: "g1",
            DisplayName: "Tools",
            TargetExePath: _targetExe,
            IconPath: _iconFile,
            ShortcutPath: shortcutPath));

        File.Exists(shortcutPath).Should().BeTrue();
        new FileInfo(shortcutPath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Generate_LeavesNoTempFileBehindOnSuccess()
    {
        var sut = new ShortcutGenerator();
        var shortcutPath = Path.Combine(_tempDir, "atomic.lnk");

        sut.Generate(new GroupShortcutRequest(
            "g", "n", _targetExe, _iconFile, shortcutPath));

        Directory.EnumerateFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Generate_CreatesMissingDestinationDirectory()
    {
        var sut = new ShortcutGenerator();
        var nestedPath = Path.Combine(_tempDir, "newsub", "deeper", "x.lnk");

        sut.Generate(new GroupShortcutRequest("g", "n", _targetExe, _iconFile, nestedPath));

        File.Exists(nestedPath).Should().BeTrue();
    }

    [Fact]
    public void Generate_OverwritesExistingFile()
    {
        var sut = new ShortcutGenerator();
        var shortcutPath = Path.Combine(_tempDir, "twice.lnk");

        sut.Generate(new GroupShortcutRequest("g", "First", _targetExe, _iconFile, shortcutPath));
        sut.Generate(new GroupShortcutRequest("g", "Second", _targetExe, _iconFile, shortcutPath));

        File.Exists(shortcutPath).Should().BeTrue();
    }

    [Fact]
    public void Generate_WritesShortcutThatRoundTrips_ViaIShellLinkW()
    {
        var sut = new ShortcutGenerator();
        var shortcutPath = Path.Combine(_tempDir, "roundtrip.lnk");

        sut.Generate(new GroupShortcutRequest(
            GroupId: "roundtrip-id",
            DisplayName: "Round Trip",
            TargetExePath: _targetExe,
            IconPath: _iconFile,
            ShortcutPath: shortcutPath));

        // Read the file back via IShellLinkW + IPersistFile.Load and assert the persisted properties.
        var link = new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);

            var pathBuffer = new StringBuilder(260);
            ((IShellLinkW)link).GetPath(pathBuffer, 260, IntPtr.Zero, 0);
            pathBuffer.ToString().Should().Be(_targetExe);

            var argsBuffer = new StringBuilder(260);
            ((IShellLinkW)link).GetArguments(argsBuffer, 260);
            argsBuffer.ToString().Should().Contain("roundtrip-id");

            var iconBuffer = new StringBuilder(260);
            ((IShellLinkW)link).GetIconLocation(iconBuffer, 260, out _);
            iconBuffer.ToString().Should().Be(_iconFile);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [Fact]
    public void TryUpdateIcon_ChangesOnlyTheIcon_OfARealShortcut()
    {
        // The taskbar's pinned copy is rewritten through this; anything besides the icon
        // changing — target, arguments, and above all the AUMID — would break the pin.
        var sut = new ShortcutGenerator();
        var shortcutPath = Path.Combine(_tempDir, "pinned.lnk");
        sut.Generate(new GroupShortcutRequest("pin-id", "Pinned", _targetExe, _iconFile, shortcutPath));
        var newIcon = Path.Combine(_tempDir, "pin-id-abcdef12.ico");
        File.Copy(_iconFile, newIcon);

        sut.TryUpdateIcon(shortcutPath, newIcon).Should().BeTrue();

        var link = new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);

            var iconBuffer = new StringBuilder(260);
            ((IShellLinkW)link).GetIconLocation(iconBuffer, 260, out _);
            iconBuffer.ToString().Should().Be(newIcon);

            var pathBuffer = new StringBuilder(260);
            ((IShellLinkW)link).GetPath(pathBuffer, 260, IntPtr.Zero, 0);
            pathBuffer.ToString().Should().Be(_targetExe);

            var argsBuffer = new StringBuilder(260);
            ((IShellLinkW)link).GetArguments(argsBuffer, 260);
            argsBuffer.ToString().Should().Be("--group-id \"pin-id\"");
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }

        new ShortcutReader().TryReadAumid(shortcutPath).Should().Be(GroupAumid.For("pin-id"));
    }

    [Fact]
    public void TryUpdateIcon_ReturnsFalse_ForAMissingShortcut()
    {
        new ShortcutGenerator().TryUpdateIcon(Path.Combine(_tempDir, "nope.lnk"), _iconFile).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Generate_ThrowsForBlankRequiredFields(string? blank)
    {
        var sut = new ShortcutGenerator();

        // Generate validates each required field; sample one to keep the test list small.
        var bad = new GroupShortcutRequest(
            GroupId: blank!,
            DisplayName: "n",
            TargetExePath: _targetExe,
            IconPath: _iconFile,
            ShortcutPath: Path.Combine(_tempDir, "x.lnk"));

        var act = () => sut.Generate(bad);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_ThrowsForNullRequest()
    {
        var sut = new ShortcutGenerator();
        var act = () => sut.Generate(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
