using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Shared.Configuration;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

public sealed class CustomGroupIconStoreTests : IDisposable
{
    private readonly string _tempBase;
    private readonly AppDataPathProvider _paths;
    private readonly CustomGroupIconStore _sut;

    public CustomGroupIconStoreTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.CustomIcon." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);
        _paths = new AppDataPathProvider(_tempBase);
        _sut = new CustomGroupIconStore(_paths);
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
    public async Task TryImportAsync_StoresAPng_FittedIntoATransparentSquare()
    {
        var source = WriteImage("wide.png", new PngBitmapEncoder(), Solid(Colors.Red, 100, 50));

        var ok = await _sut.TryImportAsync("g", source);

        ok.Should().BeTrue();
        _sut.Exists("g").Should().BeTrue();
        var stored = _sut.TryLoad("g")!;
        stored.PixelWidth.Should().Be(CustomGroupIconStore.Size);
        stored.PixelHeight.Should().Be(CustomGroupIconStore.Size);
        Pixel(stored, 128, 128).Should().Be(Colors.Red, "the image is centred");
        Pixel(stored, 128, 10).A.Should().Be(0, "a wide image leaves the band above it transparent");
    }

    [Fact]
    public async Task TryImportAsync_ReadsJpegAndIco()
    {
        var jpeg = WriteImage("photo.jpg", new JpegBitmapEncoder(), Solid(Colors.Blue, 64, 64));
        var ico = Path.Combine(_tempBase, "icon.ico");
        File.WriteAllBytes(ico, IcoFileWriter.BuildIcoBytes(Solid(Colors.Green, 256, 256)));

        (await _sut.TryImportAsync("a", jpeg)).Should().BeTrue();
        (await _sut.TryImportAsync("b", ico)).Should().BeTrue();

        Pixel(_sut.TryLoad("b")!, 3, 3).Should().Be(Colors.Green, "the 256 px frame fills the square edge to edge");
    }

    [Fact]
    public async Task TryImportAsync_KeepsSourceIndependent_OfTheOriginalFile()
    {
        var source = WriteImage("gone.png", new PngBitmapEncoder(), Solid(Colors.Red, 32, 32));

        await _sut.TryImportAsync("g", source);
        File.Delete(source);

        _sut.TryLoad("g").Should().NotBeNull("a copy is stored, not a reference to the chosen file");
    }

    [Fact]
    public async Task TryImportAsync_RejectsAFileThatIsNotAnImage_AndKeepsThePreviousChoice()
    {
        var good = WriteImage("good.png", new PngBitmapEncoder(), Solid(Colors.Red, 32, 32));
        var bad = Path.Combine(_tempBase, "not-an-image.png");
        File.WriteAllText(bad, "hello");
        await _sut.TryImportAsync("g", good);
        var before = File.ReadAllBytes(_paths.GetGroupCustomIconFile("g"));

        var ok = await _sut.TryImportAsync("g", bad);

        ok.Should().BeFalse();
        File.ReadAllBytes(_paths.GetGroupCustomIconFile("g")).Should().Equal(before);
    }

    [Fact]
    public async Task TryImportAsync_ReturnsFalse_ForAMissingFile()
    {
        var ok = await _sut.TryImportAsync("g", Path.Combine(_tempBase, "missing.png"));

        ok.Should().BeFalse();
        _sut.Exists("g").Should().BeFalse();
    }

    [Fact]
    public void TryLoad_ReturnsNull_WhenNothingWasChosen()
    {
        _sut.TryLoad("g").Should().BeNull();
        _sut.Exists("g").Should().BeFalse();
    }

    [Fact]
    public void TryLoad_ReturnsNull_ForADamagedStoredFile()
    {
        var path = _paths.GetGroupCustomIconFile("g");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);

        _sut.TryLoad("g").Should().BeNull("sync falls back to the composite rather than failing");
    }

    [Fact]
    public async Task Delete_RemovesTheChoice_AndToleratesNone()
    {
        await _sut.TryImportAsync("g", WriteImage("x.png", new PngBitmapEncoder(), Solid(Colors.Red, 16, 16)));

        _sut.Delete("g");
        _sut.Delete("g");

        _sut.Exists("g").Should().BeFalse();
    }

    [Fact]
    public async Task TryImportAppIconAsync_StoresAFittedCopy_NamedAfterItsContent()
    {
        var source = WriteImage("app.png", new PngBitmapEncoder(), Solid(Colors.Red, 64, 32));

        var first = await _sut.TryImportAppIconAsync("g", source);
        var again = await _sut.TryImportAppIconAsync("g", source);

        first.Should().NotBeNull();
        _paths.IsAppCustomIconFile("g", first!).Should().BeTrue();
        File.Exists(first).Should().BeTrue();
        again.Should().Be(first, "the same image chosen twice is one file");
        var stored = ImageFile.TryLoad(first)!;
        stored.PixelWidth.Should().Be(CustomGroupIconStore.Size);
        stored.PixelHeight.Should().Be(CustomGroupIconStore.Size);
    }

    [Fact]
    public async Task TryImportAppIconAsync_ReturnsNull_ForAFileThatIsNotAnImage()
    {
        var bad = Path.Combine(_tempBase, "bad.png");
        File.WriteAllText(bad, "hello");

        (await _sut.TryImportAppIconAsync("g", bad)).Should().BeNull();
        Directory.Exists(_paths.AppCustomIconsDirectory).Should().BeFalse("nothing is written for a rejected file");
    }

    [Fact]
    public async Task PruneAppIcons_DeletesOnlyTheGroupsUnusedImages()
    {
        var kept = await _sut.TryImportAppIconAsync("g", WriteImage("a.png", new PngBitmapEncoder(), Solid(Colors.Red, 16, 16)));
        var dropped = await _sut.TryImportAppIconAsync("g", WriteImage("b.png", new PngBitmapEncoder(), Solid(Colors.Blue, 16, 16)));
        var otherGroup = await _sut.TryImportAppIconAsync("h", WriteImage("c.png", new PngBitmapEncoder(), Solid(Colors.Green, 16, 16)));

        _sut.PruneAppIcons("g", [kept, null]);

        File.Exists(kept).Should().BeTrue();
        File.Exists(dropped).Should().BeFalse();
        File.Exists(otherGroup).Should().BeTrue("another group's images are not this group's to prune");
    }

    [Fact]
    public void PruneAppIcons_ToleratesAMissingFolder()
    {
        var act = () => _sut.PruneAppIcons("g", []);

        act.Should().NotThrow();
    }

    private string WriteImage(string name, BitmapEncoder encoder, BitmapSource image)
    {
        var path = Path.Combine(_tempBase, name);
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static BitmapSource Solid(Color color, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
