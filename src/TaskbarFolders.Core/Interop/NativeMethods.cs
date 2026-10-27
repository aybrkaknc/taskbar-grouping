using System;
using System.Runtime.InteropServices;

namespace TaskbarFolders.Core.Interop;

/// <summary>
/// P/Invoke entry points used by the icon engine. All members are <see langword="internal"/>
/// to satisfy CA1401 (do not expose P/Invoke methods on public types).
/// </summary>
internal static class NativeMethods
{
    // --- shell32 ----------------------------------------------------------

    /// <summary>Retrieves information about an object in the file system.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    /// <summary>Retrieves the requested system image list. Returns S_OK on success.</summary>
    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern int SHGetImageList(
        int iImageList,
        ref Guid riid,
        out IntPtr ppv);

    /// <summary>
    /// Notifies the system of an event that an application has performed (file created,
    /// renamed, attribute changed, etc.). Used to tell the Shell's AppsFolder index about
    /// a newly written Start Menu shortcut so <c>TaskbarManager.RequestPinCurrentAppAsync</c>
    /// can find it without racing the background indexer.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(
        int wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);

    // --- user32 -----------------------------------------------------------

    /// <summary>Destroys an icon and frees any memory the icon occupied.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // --- Flags & constants ------------------------------------------------

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_LARGEICON = 0x0;
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint SHGFI_SYSICONINDEX = 0x4000;
    public const uint SHGFI_LINKOVERLAY = 0x8000;

    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    public const int SHIL_SMALL = 0;       // 16x16 (system metric SM_CXSMICON)
    public const int SHIL_LARGE = 1;       // 32x32 (system metric SM_CXICON)
    public const int SHIL_EXTRALARGE = 2;  // 48x48
    public const int SHIL_SYSSMALL = 3;    // SM_CXSMICON
    public const int SHIL_JUMBO = 4;       // 256x256

    public const uint ILD_TRANSPARENT = 0x1;
    public const uint ILD_IMAGE = 0x20;

    public const uint SLR_NO_UI = 0x1;
    public const int MAX_PATH = 260;

    // SHChangeNotify event ids and flags. Only the subset we currently need.
    public const int SHCNE_CREATE = 0x00000002;
    public const int SHCNE_UPDATEITEM = 0x00002000;
    public const uint SHCNF_PATHW = 0x00000005;
    public const uint SHCNF_FLUSH = 0x00001000;

    /// <summary>IID for IImageList.</summary>
    public static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
}
