using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// Default <see cref="ICustomGroupIconStore"/>: one PNG per group at
/// <see cref="IAppDataPathProvider.GetGroupCustomIconFile"/>, normalised on import to a
/// transparent <see cref="Size"/>-pixel square with the image centred.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CustomGroupIconStore : ICustomGroupIconStore
{
    /// <summary>Edge length of the stored image; the largest size the group's .ico carries.</summary>
    public const int Size = 256;

    private readonly IAppDataPathProvider _paths;
    private readonly ILogger<CustomGroupIconStore>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public CustomGroupIconStore(IAppDataPathProvider paths, ILogger<CustomGroupIconStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool Exists(string groupId) => File.Exists(_paths.GetGroupCustomIconFile(groupId));

    /// <inheritdoc/>
    public BitmapSource? TryLoad(string groupId)
    {
        var path = _paths.GetGroupCustomIconFile(groupId);
        if (!File.Exists(path))
        {
            return null;
        }

        var image = ImageFile.TryLoad(path);
        if (image is null)
        {
            _logger?.LogWarning("Custom icon of {GroupId} could not be read; composing one instead.", groupId);
        }
        return image;
    }

    /// <inheritdoc/>
    public async Task<bool> TryImportAsync(string groupId, string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var target = _paths.GetGroupCustomIconFile(groupId);

        var png = await TryNormaliseAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            _logger?.LogWarning("{Path} could not be read as an image; group icon of {GroupId} unchanged.", sourcePath, groupId);
            return false;
        }

        await WriteAtomicallyAsync(target, png, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Group {GroupId} now uses the icon from {Path}.", groupId, sourcePath);
        return true;
    }

    /// <inheritdoc/>
    public async Task<string?> TryImportAppIconAsync(string groupId, string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var png = await TryNormaliseAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            _logger?.LogWarning("{Path} could not be read as an image; app icon in {GroupId} unchanged.", sourcePath, groupId);
            return null;
        }

        // Named after its content: the same image chosen again is the same file.
        var version = Convert.ToHexString(SHA256.HashData(png).AsSpan(0, 4)).ToLowerInvariant();
        var target = _paths.GetAppCustomIconFile(groupId, version);
        await WriteAtomicallyAsync(target, png, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Stored app icon {Target} from {Path}.", target, sourcePath);
        return target;
    }

    /// <inheritdoc/>
    public void PruneAppIcons(string groupId, IEnumerable<string?> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        var keep = new HashSet<string>(
            inUse.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(p!)),
            StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(_paths.AppCustomIconsDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_paths.AppCustomIconsDirectory, "*.png").ToList())
            {
                if (_paths.IsAppCustomIconFile(groupId, file) && !keep.Contains(Path.GetFullPath(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not prune the app icons of {GroupId}.", groupId);
        }
    }

    /// <summary>The image as a PNG fitted into the square, or <see langword="null"/> when unreadable.</summary>
    private static async Task<byte[]?> TryNormaliseAsync(string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            var source = ImageFile.Decode(await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false));
            return EncodePng(FitToSquare(source));
        }
        catch (Exception ex) when (ImageFile.IsUnreadableImage(ex))
        {
            return null;
        }
    }

    private static async Task WriteAtomicallyAsync(string target, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temp, target, overwrite: true);
    }

    /// <inheritdoc/>
    public void Delete(string groupId)
    {
        var path = _paths.GetGroupCustomIconFile(groupId);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // File.Delete is silent about a missing file but throws for a missing folder, and
            // the folder does not exist until an image has been chosen. Nothing to delete.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not delete the custom icon {Path}.", path);
        }
    }

    private static RenderTargetBitmap FitToSquare(BitmapSource image)
    {
        var scale = Math.Min((double)Size / image.PixelWidth, (double)Size / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawImage(image, new Rect((Size - width) / 2, (Size - height) / 2, width, height));
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
