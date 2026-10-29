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

    /// <summary>Creates a shell item for a file-system path; used to reach its context menu.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    // --- user32 -----------------------------------------------------------

    /// <summary>
    /// Extracts icons of a chosen size from an executable, DLL or icon file: the one at
    /// <paramref name="nIconIndex"/> (a negative number is a resource id), at the size asked for
    /// where the file has it and the nearest otherwise. Returns how many were extracted; every
    /// handle in <paramref name="phicon"/> must be destroyed with <see cref="DestroyIcon"/>.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint PrivateExtractIconsW(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        uint[]? piconid,
        uint nIcons,
        uint flags);

    /// <summary>Destroys an icon and frees any memory the icon occupied.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Creates an empty popup menu for <c>IContextMenu.QueryContextMenu</c> to fill.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr CreatePopupMenu();

    /// <summary>Destroys a menu created by <see cref="CreatePopupMenu"/>.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    /// <summary>Number of items in a menu, or -1 on failure.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetMenuItemCount(IntPtr hMenu);

    /// <summary>Command id of the item at <paramref name="nPos"/>; <c>0xFFFFFFFF</c> for a submenu or separator.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetMenuItemID(IntPtr hMenu, int nPos);

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

    // IContextMenu.QueryContextMenu: include verbs normally shown only with Shift.
    public const uint CMF_EXTENDEDVERBS = 0x00000100;

    // IContextMenu.GetCommandString: the item's canonical verb, as UTF-16.
    public const uint GCS_VERBW = 0x00000004;
    public const int SW_SHOWNORMAL = 1;

    /// <summary>IID for IShellItem.</summary>
    public static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>IID for IContextMenu.</summary>
    public static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    /// <summary>BHID_SFUIObject: binds a shell item to its UI objects, such as its context menu.</summary>
    public static readonly Guid BHID_SFUIObject = new("3981E225-F559-11D3-8E3A-00C04F6837D5");

    /// <summary>IID for IImageList.</summary>
    public static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
}
