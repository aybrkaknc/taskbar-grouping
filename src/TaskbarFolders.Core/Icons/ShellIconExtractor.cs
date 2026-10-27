using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// Default <see cref="IIconExtractor"/> backed by the Windows shell. Uses
/// <c>SHGetFileInfo</c> + <c>SHGetImageList</c> for executables/shortcuts and
/// <see cref="IconBitmapDecoder"/> for raw <c>.ico</c> files. Resolves <c>.lnk</c>
/// targets via <c>IShellLinkW</c> before extraction so the popup shows the target
/// application's icon rather than the shortcut overlay.
/// </summary>
/// <remarks>
/// Safe to call from several threads at once: the shell calls are serialised, because
/// <c>SHGetFileInfo</c> and the system image lists, called concurrently from thread-pool
/// threads, returned no icon for most of a popup's apps.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ShellIconExtractor : IIconExtractor
{
    // Process-wide: the state the concurrent calls trip over is the shell's, not this instance's.
    private static readonly object _shellLock = new();

    private readonly ILogger<ShellIconExtractor>? _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="logger">Optional logger; instances created without DI receive a null logger.</param>
    public ShellIconExtractor(ILogger<ShellIconExtractor>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public BitmapSource? ExtractIcon(string filePath, int size = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            var resolved = ResolvePath(filePath);
            var extension = Path.GetExtension(resolved);

            if (string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
            {
                return ExtractFromIcoFile(resolved, size);
            }

            lock (_shellLock)
            {
                return ExtractFromShell(resolved, size);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to extract icon from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// If the supplied path points to a <c>.lnk</c>, walks the shortcut to its
    /// target executable. If the resolution fails (broken link, missing target),
    /// returns the original path so the shell falls back to the link's overlay.
    /// </summary>
    private string ResolvePath(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return filePath;
        }

        // Instantiate outside the try so an unlikely CLSID-not-registered failure cannot
        // race with the finally block (link would still be null at that point).
        var link = new ShellLink();
        try
        {
            // Cast once; both interfaces share the same underlying RCW so a single
            // FinalReleaseComObject in the finally block balances all references.
            var shellLink = (IShellLinkW)link;
            ((IPersistFile)link).Load(filePath, 0);
            shellLink.Resolve(IntPtr.Zero, NativeMethods.SLR_NO_UI);

            var buffer = new StringBuilder(NativeMethods.MAX_PATH);
            shellLink.GetPath(buffer, NativeMethods.MAX_PATH, IntPtr.Zero, 0);
            var target = buffer.ToString();

            return string.IsNullOrWhiteSpace(target) ? filePath : target;
        }
        catch (COMException ex)
        {
            _logger?.LogDebug(ex, "Could not resolve shortcut {FilePath}; falling back to original path", filePath);
            return filePath;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static BitmapFrame? ExtractFromIcoFile(string path, int size)
    {
        var decoder = new IconBitmapDecoder(
            new Uri(path, UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            return null;
        }

        // Smallest frame at or above the requested size; if none, use the largest available.
        var frame = decoder.Frames
            .Where(f => f.PixelWidth >= size)
            .OrderBy(f => f.PixelWidth)
            .FirstOrDefault()
            ?? decoder.Frames.OrderByDescending(f => f.PixelWidth).First();

        frame.Freeze();
        return frame;
    }

    private BitmapSource? ExtractFromShell(string filePath, int size)
    {
        return size switch
        {
            <= 16 => ExtractViaSHGetFileInfo(filePath, NativeMethods.SHGFI_SMALLICON),
            <= 32 => ExtractViaSHGetFileInfo(filePath, NativeMethods.SHGFI_LARGEICON),
            _ => ExtractViaSHGetImageList(filePath, size),
        };
    }

    private BitmapSource? ExtractViaSHGetFileInfo(string filePath, uint sizeFlag)
    {
        var info = default(SHFILEINFO);
        var flags = NativeMethods.SHGFI_ICON | sizeFlag | NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var result = NativeMethods.SHGetFileInfo(
            filePath,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            _logger?.LogDebug("SHGetFileInfo returned no icon for {FilePath}", filePath);
            return null;
        }

        return CreateAndDestroy(info.hIcon);
    }

    private BitmapSource? ExtractViaSHGetImageList(string filePath, int size)
    {
        // Step 1: ask the shell for the icon index of this file.
        var info = default(SHFILEINFO);
        var indexFlags = NativeMethods.SHGFI_SYSICONINDEX | NativeMethods.SHGFI_USEFILEATTRIBUTES;

        var indexResult = NativeMethods.SHGetFileInfo(
            filePath,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            indexFlags);

        if (indexResult == IntPtr.Zero)
        {
            _logger?.LogDebug("SHGetFileInfo (icon index) returned 0 for {FilePath}", filePath);
            return null;
        }

        // Step 2: pick the image list whose native size fits the requested size,
        // then extract the indexed icon as an HICON.
        var shil = size switch
        {
            >= 256 => NativeMethods.SHIL_JUMBO,
            >= 48 => NativeMethods.SHIL_EXTRALARGE,
            >= 32 => NativeMethods.SHIL_LARGE,
            _ => NativeMethods.SHIL_SMALL,
        };

        var iid = NativeMethods.IID_IImageList;
        var hr = NativeMethods.SHGetImageList(shil, ref iid, out var imageListPtr);
        if (hr != 0 || imageListPtr == IntPtr.Zero)
        {
            _logger?.LogDebug("SHGetImageList(SHIL={Shil}) failed with HRESULT 0x{Hresult:X8}", shil, hr);
            return null;
        }

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var imageList = (IImageList)Marshal.GetObjectForIUnknown(imageListPtr);
            try
            {
                var getIconHr = imageList.GetIcon(info.iIcon, NativeMethods.ILD_TRANSPARENT, out hIcon);
                if (getIconHr != 0 || hIcon == IntPtr.Zero)
                {
                    _logger?.LogDebug("IImageList.GetIcon failed with HRESULT 0x{Hresult:X8}", getIconHr);
                    return null;
                }

                return CreateAndDestroy(hIcon);
            }
            finally
            {
                Marshal.FinalReleaseComObject(imageList);
            }
        }
        finally
        {
            Marshal.Release(imageListPtr);
        }
    }

    /// <summary>
    /// Wraps an HICON in a frozen <see cref="BitmapSource"/>, then destroys the HICON.
    /// CreateBitmapSourceFromHIcon copies the bits, so destroying the handle immediately
    /// is the documented, leak-free pattern.
    /// </summary>
    private BitmapSource? CreateAndDestroy(IntPtr hIcon)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (!NativeMethods.DestroyIcon(hIcon))
            {
                _logger?.LogWarning("DestroyIcon returned false for handle 0x{Handle:X}", hIcon.ToInt64());
            }
        }
    }
}
