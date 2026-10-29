using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// Disk-backed <see cref="IIconCache"/>. Cache entries live as PNGs under
/// <see cref="IAppDataPathProvider.IconsDirectory"/><c>/cache/</c> with a SHA-256 key
/// derived from <c>sourcePath</c> + <c>lastWriteUtcTicks</c> + <c>size</c>. Entries older
/// than <see cref="RetainDays"/> are pruned in the constructor.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileSystemIconCache : IIconCache
{
    /// <summary>How many days a cache file is kept on disk regardless of source-file changes.</summary>
    public const int RetainDays = 30;

    /// <summary>Cache sub-folder name beneath <see cref="IAppDataPathProvider.IconsDirectory"/>.</summary>
    public const string CacheFolderName = "cache";

    private readonly string _cacheDir;
    private readonly ILogger<FileSystemIconCache>? _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="paths">Path provider for locating the cache directory.</param>
    /// <param name="logger">Optional logger.</param>
    public FileSystemIconCache(IAppDataPathProvider paths, ILogger<FileSystemIconCache>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _cacheDir = Path.Combine(paths.IconsDirectory, CacheFolderName);
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Schedules <see cref="PruneStaleEntries"/> on the thread pool. App.OnStartup calls this
    /// once after the main window has been shown so the sweep does not block the first paint.
    /// IOException is swallowed inside the background task — the next launch will retry.
    /// </remarks>
    public void StartBackgroundPrune() =>
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                PruneStaleEntries();
            }
            catch (IOException ex)
            {
                _logger?.LogWarning(ex, "Background icon-cache prune failed; will retry next launch.");
            }
        });

    /// <inheritdoc/>
    public bool TryGet(string sourcePath, int size, [NotNullWhen(true)] out BitmapSource? icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        icon = null;
        var cachedFile = GetCachePath(sourcePath, size);
        if (!File.Exists(cachedFile))
        {
            return false;
        }

        try
        {
            // Read into a MemoryStream so we own and close the FileStream immediately.
            // Passing the Uri ctor of PngBitmapDecoder leaves the underlying FileStream
            // open until the decoder is GC'd — on Windows Server CI this is slow enough
            // that a same-call File.Delete races with the still-open handle.
            byte[] bytes;
            using (var fs = new FileStream(cachedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }

            using var memory = new MemoryStream(bytes, writable: false);
            var decoder = new PngBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return false;
            }

            var frame = decoder.Frames[0];
            frame.Freeze();
            icon = frame;
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
        {
            // Corrupt cache entry — delete and miss so caller regenerates.
            _logger?.LogWarning(ex, "Corrupt cache entry {File}; deleting", cachedFile);
            TryDelete(cachedFile);
            return false;
        }
    }

    /// <inheritdoc/>
    public void Set(string sourcePath, int size, BitmapSource icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(icon);

        Directory.CreateDirectory(_cacheDir);

        var target = GetCachePath(sourcePath, size);
        var temp = target + ".tmp";

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(icon));

        try
        {
            using (var stream = File.Create(temp))
            {
                encoder.Save(stream);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch (IOException ex)
        {
            // A failed cache write must not break the calling extraction flow.
            _logger?.LogWarning(ex, "Failed to write cache entry for {Source} size {Size}", sourcePath, size);
            TryDelete(temp);
        }
    }

    internal string GetCachePath(string sourcePath, int size) =>
        Path.Combine(_cacheDir, ComputeKey(sourcePath, size));

    private static string ComputeKey(string sourcePath, int size)
    {
        var lastWriteTicks = File.Exists(sourcePath)
            ? File.GetLastWriteTimeUtc(sourcePath).Ticks
            : 0L;

        // The version changes whenever what is extracted for a path does, so entries written by
        // an older extractor are never found: v2 followed the fix for icons that came back as
        // a small glyph in the corner of a 256 px canvas, v3 the one that makes a shortcut
        // show its own icon instead of its target's.
        var material = string.Create(
            CultureInfo.InvariantCulture,
            $"{sourcePath}|{lastWriteTicks}|{size}|v3");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash) + ".png";
    }

    private void PruneStaleEntries()
    {
        if (!Directory.Exists(_cacheDir))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-RetainDays);
        foreach (var file in Directory.EnumerateFiles(_cacheDir, "*.png"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // File in use — skip and retry on the next launch.
            }
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger?.LogDebug(ex, "Could not delete {Path}", path);
        }
    }
}
