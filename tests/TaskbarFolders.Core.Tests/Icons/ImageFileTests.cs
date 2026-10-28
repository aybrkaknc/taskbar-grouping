using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

public sealed class ImageFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TaskbarFolders.ImageFile." + Guid.NewGuid().ToString("N"));

    public ImageFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryLoad_ReturnsNull_ForABlankPath(string? path)
    {
        ImageFile.TryLoad(path).Should().BeNull();
    }

    [Fact]
    public void TryLoad_ReturnsNull_ForAMissingOrDamagedFile()
    {
        var damaged = Path.Combine(_dir, "damaged.png");
        File.WriteAllBytes(damaged, [1, 2, 3]);

        ImageFile.TryLoad(Path.Combine(_dir, "missing.png")).Should().BeNull();
        ImageFile.TryLoad(damaged).Should().BeNull();
    }

    [Fact]
    public void TryLoad_ReadsAPng_AsAFrozenBitmap()
    {
        var path = Path.Combine(_dir, "icon.png");
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.Teal, null, new Rect(0, 0, 40, 30));
        }
        var bitmap = new RenderTargetBitmap(40, 30, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        var loaded = ImageFile.TryLoad(path);

        loaded.Should().NotBeNull();
        loaded!.IsFrozen.Should().BeTrue("the popup reads it on a pool thread and shows it on the UI thread");
        loaded.PixelWidth.Should().Be(40);
    }
}
