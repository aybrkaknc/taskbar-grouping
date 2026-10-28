using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// Reads an image file the user chose — a group's or an app's own icon — into a frozen
/// bitmap. Unlike <see cref="IIconExtractor"/>, which asks the shell for a program's icon, this
/// decodes the file's own pixels.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ImageFile
{
    /// <summary>
    /// The image at <paramref name="path"/>, or <see langword="null"/> when the path is blank,
    /// the file is missing or it is not a readable image — callers then fall back to the
    /// program's own icon.
    /// </summary>
    /// <param name="path">Image file, typically a PNG the app stored itself.</param>
    public static BitmapSource? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return Decode(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (IsUnreadableImage(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes every frame and keeps the largest, so an .ico gives its 256 px image rather
    /// than whichever size happens to come first.
    /// </summary>
    internal static BitmapSource Decode(byte[] bytes)
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

    /// <summary>
    /// What WPF's decoders and the file system throw for a file that is missing, locked, not
    /// an image or a damaged one.
    /// </summary>
    internal static bool IsUnreadableImage(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException;
}
