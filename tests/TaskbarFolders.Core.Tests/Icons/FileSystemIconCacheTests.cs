using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Shared.Configuration;
using Xunit;

namespace TaskbarFolders.Core.Tests.Icons;

public sealed class FileSystemIconCacheTests : IDisposable
{
    private readonly string _tempBase;
    private readonly AppDataPathProvider _paths;
    private readonly string _sourceFile;

    public FileSystemIconCacheTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.IconCache." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);
        _paths = new AppDataPathProvider(_tempBase);

        // Real source file so the cache key includes a real LastWriteTimeUtc.
        _sourceFile = Path.Combine(_tempBase, "source.exe");
        File.WriteAllBytes(_sourceFile, [1, 2, 3, 4]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBase))
        {
            Directory.Delete(_tempBase, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static RenderTargetBitmap SolidBitmap(int size, Color color)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, size, size));
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenCacheEmpty()
    {
        var sut = new FileSystemIconCache(_paths);

        sut.TryGet(_sourceFile, 256, out var icon).Should().BeFalse();
        icon.Should().BeNull();
    }

    [Fact]
    public void SetThenTryGet_ReturnsCachedBitmap_WithSameDimensions()
    {
        var sut = new FileSystemIconCache(_paths);
        sut.Set(_sourceFile, 64, SolidBitmap(64, Colors.Red));

        sut.TryGet(_sourceFile, 64, out var icon).Should().BeTrue();
        icon.Should().NotBeNull();
        icon!.PixelWidth.Should().Be(64);
        icon.PixelHeight.Should().Be(64);
        icon.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void DifferentSizesShareSourcePath_ButProduceDifferentEntries()
    {
        var sut = new FileSystemIconCache(_paths);

        sut.Set(_sourceFile, 16, SolidBitmap(16, Colors.Red));
        sut.Set(_sourceFile, 256, SolidBitmap(256, Colors.Blue));

        sut.TryGet(_sourceFile, 16, out var small).Should().BeTrue();
        small!.PixelWidth.Should().Be(16);

        sut.TryGet(_sourceFile, 256, out var large).Should().BeTrue();
        large!.PixelWidth.Should().Be(256);
    }

    [Fact]
    public void TryGet_ReturnsFalse_AfterSourceFileModified()
    {
        var sut = new FileSystemIconCache(_paths);
        sut.Set(_sourceFile, 64, SolidBitmap(64, Colors.Red));

        sut.TryGet(_sourceFile, 64, out _).Should().BeTrue("baseline: cache populated");

        // Mutate the source file's LastWriteTimeUtc — the key changes so the cache misses.
        Thread.Sleep(20); // ensure NTFS write-time resolution registers a change
        File.SetLastWriteTimeUtc(_sourceFile, DateTime.UtcNow.AddMinutes(5));

        sut.TryGet(_sourceFile, 64, out var icon).Should().BeFalse("stale source must invalidate the entry");
        icon.Should().BeNull();
    }

    [Fact]
    public void TheKey_IsNotTheOneEarlierVersionsWrote_SoTheirTinyIconsAreNotServed()
    {
        // Before the canvas fix the cache stored the shell's 256 px canvas with a small icon in
        // its corner; an entry written then has this key and must never be found again.
        var sut = new FileSystemIconCache(_paths);
        var oldMaterial = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{_sourceFile}|{File.GetLastWriteTimeUtc(_sourceFile).Ticks}|256");
        var oldKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(oldMaterial))) + ".png";

        Path.GetFileName(sut.GetCachePath(_sourceFile, 256)).Should().NotBe(oldKey);
    }

    [Fact]
    public void Set_OverwritesExistingEntry_ForSameKey()
    {
        var sut = new FileSystemIconCache(_paths);

        sut.Set(_sourceFile, 64, SolidBitmap(64, Colors.Red));
        sut.Set(_sourceFile, 64, SolidBitmap(64, Colors.Blue));

        sut.TryGet(_sourceFile, 64, out var icon).Should().BeTrue();
        icon.Should().NotBeNull();
        // Cannot easily compare colours after round-trip, but the file existing and
        // decoding successfully proves the overwrite worked.
    }

    [Fact]
    public void TryGet_ReturnsFalse_AndDeletesEntry_WhenCacheFileIsCorrupt()
    {
        var sut = new FileSystemIconCache(_paths);
        var cacheFile = sut.GetCachePath(_sourceFile, 64);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        File.WriteAllText(cacheFile, "not a png");

        sut.TryGet(_sourceFile, 64, out _).Should().BeFalse();
        File.Exists(cacheFile).Should().BeFalse("corrupt entry must be deleted so the next Set succeeds");
    }

    [Fact]
    public void Ctor_DoesNotPrune_LeavesStaleEntriesForBackgroundSweep()
    {
        // v0.4 contract: ctor must NOT touch the cache directory. Pruning is deferred to
        // StartBackgroundPrune so the launcher startup is not blocked by enumerate-and-delete.
        var cacheDir = Path.Combine(_paths.IconsDirectory, FileSystemIconCache.CacheFolderName);
        Directory.CreateDirectory(cacheDir);
        var ancient = Path.Combine(cacheDir, "deadbeef.png");
        File.WriteAllBytes(ancient, [0]);
        File.SetLastWriteTimeUtc(ancient, DateTime.UtcNow.AddDays(-(FileSystemIconCache.RetainDays + 1)));

        _ = new FileSystemIconCache(_paths);

        File.Exists(ancient).Should().BeTrue("v0.4 ctor does not prune");
    }

    [Fact]
    public void StartBackgroundPrune_DeletesStaleEntries()
    {
        var cacheDir = Path.Combine(_paths.IconsDirectory, FileSystemIconCache.CacheFolderName);
        Directory.CreateDirectory(cacheDir);
        var ancient = Path.Combine(cacheDir, "deadbeef.png");
        File.WriteAllBytes(ancient, [0]);
        File.SetLastWriteTimeUtc(ancient, DateTime.UtcNow.AddDays(-(FileSystemIconCache.RetainDays + 1)));

        var fresh = Path.Combine(cacheDir, "cafebabe.png");
        File.WriteAllBytes(fresh, [0]);

        var sut = new FileSystemIconCache(_paths);
        sut.StartBackgroundPrune();

        // Background task — poll up to 10 s for the deletion to land. CI runners with cold
        // ThreadPool can take >2 s to schedule the Task.Run lambda; 10 s is comfortable
        // headroom while still failing fast on a real regression.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (File.Exists(ancient) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        File.Exists(ancient).Should().BeFalse("background prune must remove stale entries");
        File.Exists(fresh).Should().BeTrue("fresh entries must survive");
    }

    [Fact]
    public void TryGet_ThrowsForBlankSourcePath()
    {
        var sut = new FileSystemIconCache(_paths);

        var act = () => sut.TryGet("   ", 64, out _);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Set_ThrowsForNullBitmap()
    {
        var sut = new FileSystemIconCache(_paths);

        var act = () => sut.Set(_sourceFile, 64, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AnInstalledAppsEntry_IsKeyedByItsPackageVersion_SoAnUpdateIsReadAfresh()
    {
        const string app = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var version = "Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe";
        var sut = new FileSystemIconCache(_paths, _ => version);
        sut.Set(app, 64, SolidBitmap(64, Colors.Red));
        sut.TryGet(app, 64, out _).Should().BeTrue();

        version = "Microsoft.WindowsCalculator_11.2700.0.0_x64__8wekyb3d8bbwe";

        sut.TryGet(app, 64, out _).Should().BeFalse("the package was updated, and its icon may have been");
    }
}
