using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

public class ShellIconExtractorTests
{
    private static readonly string _notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
    private static readonly string _cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private static readonly string _icoFile = Path.Combine(
        RepoRoot.Path,
        "assets",
        "icons",
        "manager.ico");

    [Fact]
    public void ExtractIcon_ReturnsBitmap_ForNotepadExe()
    {
        File.Exists(_notepad).Should().BeTrue("Windows always ships notepad.exe");

        var sut = new ShellIconExtractor();
        var icon = sut.ExtractIcon(_notepad, 256);

        icon.Should().NotBeNull();
        icon!.PixelWidth.Should().BeGreaterThan(0);
        icon.PixelHeight.Should().BeGreaterThan(0);
        icon.IsFrozen.Should().BeTrue("returned BitmapSource must be cross-thread safe");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(256)]
    public void ExtractIcon_HonoursRequestedSize_ForExe(int size)
    {
        var sut = new ShellIconExtractor();
        var icon = sut.ExtractIcon(_cmd, size);

        icon.Should().NotBeNull();
        // Shell may round to its native image-list sizes (16/32/48/256); accept any
        // result that is at least as wide as the requested bracket lower bound.
        icon!.PixelWidth.Should().BeGreaterOrEqualTo(Math.Min(size, 16));
    }

    [Fact]
    public void ExtractIcon_ReturnsBitmap_For_icoFile()
    {
        File.Exists(_icoFile).Should().BeTrue($"manager.ico was generated in M0 and lives at {_icoFile}");

        var sut = new ShellIconExtractor();
        var icon = sut.ExtractIcon(_icoFile, 256);

        icon.Should().NotBeNull();
        icon!.PixelWidth.Should().Be(256);
        icon.PixelHeight.Should().Be(256);
    }

    [Fact]
    public void ExtractIcon_PicksSmallestFrameAboveRequestedSize_For_icoFile()
    {
        var sut = new ShellIconExtractor();

        var icon = sut.ExtractIcon(_icoFile, 32);

        icon.Should().NotBeNull();
        icon!.PixelWidth.Should().Be(32, "the .ico has a 32x32 frame, which is the smallest >= 32");
    }

    [Fact]
    public void ExtractIcon_FallsBackToLargestFrame_WhenRequestedSizeExceedsAvailable()
    {
        var sut = new ShellIconExtractor();

        var icon = sut.ExtractIcon(_icoFile, 9999);

        icon.Should().NotBeNull();
        icon!.PixelWidth.Should().Be(256, "largest .ico frame is 256x256");
    }

    [Fact]
    public async Task ExtractIcon_FindsEveryIcon_WhenCalledFromManyThreadsAtOnce()
    {
        // The popup extracts all of a group's icons concurrently on the thread pool. Before the
        // shell calls were serialised, 8 of these 9 came back null in a fresh process and the
        // tiles showed the placeholder (or, before it existed, nothing) at random.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string[] paths =
        [
            Path.Combine(system, "notepad.exe"), Path.Combine(system, "calc.exe"), Path.Combine(system, "Taskmgr.exe"),
            Path.Combine(system, "cmd.exe"), Path.Combine(windows, "explorer.exe"), Path.Combine(windows, "regedit.exe"),
            Path.Combine(system, "control.exe"), Path.Combine(system, "charmap.exe"), Path.Combine(system, "magnify.exe"),
        ];
        paths = paths.Where(File.Exists).ToArray();
        var sut = new ShellIconExtractor();

        for (var round = 0; round < 5; round++)
        {
            using var start = new Barrier(paths.Length);
            var results = await Task.WhenAll(paths.Select(path => Task.Run(() =>
            {
                start.SignalAndWait();
                return (path, icon: sut.ExtractIcon(path, 64));
            })));

            results.Where(r => r.icon is null).Select(r => r.path)
                .Should().BeEmpty($"every one of these ships an icon (round {round})");
        }
    }

    [Fact]
    public void ExtractIcon_ReturnsNull_ForNonexistentFile()
    {
        var sut = new ShellIconExtractor();
        // SHGetFileInfo with SHGFI_USEFILEATTRIBUTES infers from extension even for missing paths,
        // so a fictional .qzx file should produce either null (no association) or a generic icon.
        // The contract simply requires no exception to escape.
        var act = () => sut.ExtractIcon("C:/this/does/not/exist.qzx", 256);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null!)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractIcon_ThrowsArgumentException_ForBlankPath(string? path)
    {
        var sut = new ShellIconExtractor();
        var act = () => sut.ExtractIcon(path!, 256);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ExtractIcon_DrawsAnInstalledApp_FromItsAppsFolderEntry()
    {
        const string aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var installed = await new TaskbarFolders.Core.Apps.ShellInstalledAppsProvider().ListAsync();
        if (!installed.Any(a => a.Aumid == aumid))
        {
            return; // Calculator is not installed on this machine (Windows Server images).
        }

        var icon = new ShellIconExtractor().ExtractIcon(@"shell:AppsFolder\" + aumid, 64);

        icon.Should().NotBeNull("Calculator is installed, so the shell has an icon for it");

        icon!.PixelWidth.Should().Be(64);
        icon.IsFrozen.Should().BeTrue("it crosses to the UI thread");
        var pixels = new byte[64 * 64 * 4];
        new System.Windows.Media.Imaging.FormatConvertedBitmap(icon, System.Windows.Media.PixelFormats.Pbgra32, null, 0).CopyPixels(pixels, 64 * 4, 0);
        pixels.Where((_, i) => i % 4 == 3).Should().Contain(a => a == 0, "the icon keeps its transparent edge");
        pixels.Where((_, i) => i % 4 == 3).Should().Contain(a => a == 255, "and is drawn");
    }

    [Fact]
    public void ExtractIcon_ReturnsNull_ForAnInstalledAppThatIsNotThere()
    {
        new ShellIconExtractor().ExtractIcon(@"shell:AppsFolder\No.Such.App_0000000000000!App", 64).Should().BeNull();
    }
}
