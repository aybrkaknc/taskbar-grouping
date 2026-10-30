using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// The icon and the version of an app stored as its <c>shell:AppsFolder</c> entry (ADR-009).
/// <c>SHGetFileInfo</c> cannot resolve those entries; <see cref="IShellItemImageFactory"/> can, and
/// gives a clean 32-bit icon in about 25 ms. Callers hold <see cref="ShellAccess.Lock"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AppsFolderIcon
{
    /// <summary>The app's icon at <paramref name="size"/> pixels, frozen; <see langword="null"/> if the app is not there.</summary>
    public static BitmapSource? Extract(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (NativeMethods.SHCreateItemFromParsingNameAny(path, IntPtr.Zero, ref iid, out var factoryObject) != 0
            || factoryObject is not IShellItemImageFactory factory)
        {
            return null;
        }

        var hbitmap = IntPtr.Zero;
        try
        {
            if (factory.GetImage(new NativeSize { cx = size, cy = size }, ShellGuids.IconOnly, out hbitmap) != 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            return FromPremultipliedBitmap(hbitmap);
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                _ = DeleteObject(hbitmap);
            }

            Marshal.FinalReleaseComObject(factory);
        }
    }

    /// <summary>
    /// The package a packaged app's entry belongs to, version included — what changes when the app
    /// is updated, so the icon cache keys on it. <see langword="null"/> for any other entry.
    /// </summary>
    public static string? PackageFullName(string path)
    {
        var iid = typeof(IShellItem2).GUID;
        if (NativeMethods.SHCreateItemFromParsingNameAny(path, IntPtr.Zero, ref iid, out var itemObject) != 0
            || itemObject is not IShellItem2 item)
        {
            return null;
        }

        try
        {
            var key = PropertyKeys.PackageFullName;
            if (item.GetString(ref key, out var ptr) != 0 || ptr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(item);
        }
    }

    // The factory hands back a DIB section with premultiplied alpha. Imaging.CreateBitmapSourceFromHBitmap
    // drops the alpha of such a bitmap, so the bits are copied as they are, rows bottom-up as GDI stores them.
    private static BitmapSource? FromPremultipliedBitmap(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0 || bitmap.bmBitsPixel != 32 || bitmap.bmBits == IntPtr.Zero)
        {
            return null;
        }

        var height = Math.Abs(bitmap.bmHeight);
        var stride = bitmap.bmWidthBytes;
        var pixels = new byte[stride * height];
        for (var row = 0; row < height; row++)
        {
            var source = bitmap.bmHeight > 0 ? height - 1 - row : row;
            Marshal.Copy(bitmap.bmBits + (source * stride), pixels, row * stride, stride);
        }

        var image = BitmapSource.Create(bitmap.bmWidth, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        image.Freeze();
        return image;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public short bmPlanes;
        public short bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr h, int c, out NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);
}
