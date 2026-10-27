using System;
using System.IO;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Shared.Configuration;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Services;

public sealed class GroupIconReaderTests : IDisposable
{
    private readonly string _tempBase;
    private readonly AppDataPathProvider _paths;

    public GroupIconReaderTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.IconReader." + Guid.NewGuid().ToString("N"));
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
    public void TryRead_DecodesTheGroupsCurrentIconFile()
    {
        var file = _paths.GetGroupIconFile("g", "0000000a");
        Directory.CreateDirectory(_paths.IconsDirectory);
        File.WriteAllBytes(file, [0]);
        var icon = BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
        var extractor = new Mock<IIconExtractor>();
        extractor.Setup(e => e.ExtractIcon(file, GroupIconReader.Size)).Returns(icon);

        new GroupIconReader(_paths, extractor.Object).TryRead("g").Should().BeSameAs(icon);
    }

    [Fact]
    public void TryRead_ReturnsNull_WithoutTouchingTheExtractor_WhenTheGroupHasNoIconFile()
    {
        var extractor = new Mock<IIconExtractor>(MockBehavior.Strict);

        new GroupIconReader(_paths, extractor.Object).TryRead("g").Should().BeNull();
    }
}
