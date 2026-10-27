using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
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

        try
        {
            return Decode(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (IsUnreadableImage(ex))
        {
            _logger?.LogWarning(ex, "Custom icon of {GroupId} could not be read; composing one instead.", groupId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryImportAsync(string groupId, string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var target = _paths.GetGroupCustomIconFile(groupId);

        byte[] png;
        try
        {
            var source = Decode(await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false));
            png = EncodePng(FitToSquare(source));
        }
        catch (Exception ex) when (IsUnreadableImage(ex))
        {
            _logger?.LogWarning(ex, "{Path} could not be read as an image; group icon of {GroupId} unchanged.", sourcePath, groupId);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        await File.WriteAllBytesAsync(temp, png, cancellationToken).ConfigureAwait(false);
        File.Move(temp, target, overwrite: true);
        _logger?.LogInformation("Group {GroupId} now uses the icon from {Path}.", groupId, sourcePath);
        return true;
    }

    /// <inheritdoc/>
    public void Delete(string groupId)
    {
        var path = _paths.GetGroupCustomIconFile(groupId);
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not delete the custom icon {Path}.", path);
        }
    }

    /// <summary>
    /// Decodes every frame and keeps the largest, so an .ico gives its 256 px image rather
    /// than whichever size happens to come first.
    /// </summary>
    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames.MaxBy(f => (long)f.PixelWidth * f.PixelHeight)
            ?? throw new NotSupportedException("The image has no frames.");
        if (frame.CanFreeze)
        {
            frame.Freeze();
        }
        return frame;
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

    // What WPF's decoders and the file system throw for a file that is missing, locked, not
    // an image or a damaged one.
    private static bool IsUnreadableImage(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException;
}
