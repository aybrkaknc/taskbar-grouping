using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Interop;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

/// <summary>
/// A shortcut carries its own icon, which is not its target's: Explorer shows it, and so must
/// the Manager and the popup. UniFi's shortcut runs Java but shows UniFi's icon.
/// </summary>
public sealed class ShortcutIconTests : IDisposable
{
    private static readonly string _notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tf-lnk-icon-" + Guid.NewGuid().ToString("N"));

    public ShortcutIconTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeIco(Color color)
    {
        var path = Path.Combine(_dir, $"{color.R}-{color.G}-{color.B}.ico");
        new IcoFileWriter().WriteAsync(TestImages.Solid(color, 256, 256), path).GetAwaiter().GetResult();
        return path;
    }

    /// <summary>A shortcut to notepad with the given icon location, or none.</summary>
    private string MakeLink(string? iconPath, int iconIndex = 0, string name = "link.lnk")
    {
        var path = Path.Combine(_dir, name);
        var link = new ShellLink();
        try
        {
            var shellLink = (IShellLinkW)link;
            shellLink.SetPath(_notepad);
            if (iconPath is not null)
            {
                shellLink.SetIconLocation(iconPath, iconIndex);
            }

            ((IPersistFile)link).Save(path, fRemember: true);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        }

        return path;
    }

    private static Color Centre(System.Windows.Media.Imaging.BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(converted.PixelWidth / 2, converted.PixelHeight / 2, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    [Fact]
    public void AShortcutWithItsOwnIcoFile_ShowsThatIcon_NotTheTargets()
    {
        var red = Color.FromRgb(220, 20, 20);
        var link = MakeLink(MakeIco(red));

        var icon = new ShellIconExtractor().ExtractIcon(link, 256);

        icon.Should().NotBeNull();
        icon!.PixelWidth.Should().Be(256);
        Centre(icon).Should().Be(red, "UniFi.lnk runs javaw.exe but is UniFi's icon");
    }

    [Fact]
    public void TheIconLocation_MayUseAnEnvironmentVariable()
    {
        var green = Color.FromRgb(20, 180, 40);
        var ico = MakeIco(green);
        Environment.SetEnvironmentVariable("TF_TEST_ICONDIR", _dir);
        try
        {
            var link = MakeLink(@"%TF_TEST_ICONDIR%\" + Path.GetFileName(ico));

            Centre(new ShellIconExtractor().ExtractIcon(link, 256)!).Should().Be(green);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TF_TEST_ICONDIR", null);
        }
    }

    [Fact]
    public void AShortcutWithoutAnIconLocation_StillShowsItsTargetsIcon()
    {
        var link = MakeLink(iconPath: null);

        var icon = new ShellIconExtractor().ExtractIcon(link, 256);
        var target = new ShellIconExtractor().ExtractIcon(_notepad, 256);

        icon.Should().NotBeNull();
        Centre(icon!).Should().Be(Centre(target!));
    }

    [Fact]
    public void AnIconLocationThatIsTheTargetItself_ChangesNothing()
    {
        var link = MakeLink(_notepad);

        var icon = new ShellIconExtractor().ExtractIcon(link, 256);
        var target = new ShellIconExtractor().ExtractIcon(_notepad, 256);

        icon.Should().NotBeNull();
        Centre(icon!).Should().Be(Centre(target!));
    }

    [Fact]
    public void AnIconLocationThatDoesNotExist_FallsBackToTheTargetsIcon()
    {
        var link = MakeLink(Path.Combine(_dir, "gone.ico"));

        var icon = new ShellIconExtractor().ExtractIcon(link, 256);

        icon.Should().NotBeNull("a stale icon location must not blank the tile");
    }

    [Fact]
    public void AnIconLocationInAnotherProgram_ShowsThatProgramsIcon()
    {
        // calc's own icon is not notepad's: the shortcut's pick wins.
        var calc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        var link = MakeLink(calc, iconIndex: 4);   // shell32 #4 is the closed folder

        var icon = new ShellIconExtractor().ExtractIcon(link, 48);
        var target = new ShellIconExtractor().ExtractIcon(_notepad, 48);

        icon.Should().NotBeNull();
        BitmapPixels(icon!).Should().NotEqual(BitmapPixels(target!), "the icon is shell32's, not notepad's");
    }

    private static byte[] BitmapPixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
