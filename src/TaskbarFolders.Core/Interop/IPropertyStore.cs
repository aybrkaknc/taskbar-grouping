using System;
using System.Runtime.InteropServices;

namespace TaskbarFolders.Core.Interop;

/// <summary>
/// Win32 <c>PROPERTYKEY</c> — pair of <c>fmtid</c> Guid and <c>pid</c> property identifier.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

/// <summary>
/// Subset of Win32 <c>PROPVARIANT</c> sufficient for VT_LPWSTR (the only variant we set
/// — <c>PKEY_AppUserModel_ID</c> is a unicode string). The full union has many more shapes
/// but we never read or write the others, so a 24-byte sequential layout that includes the
/// vt code and a wide-string pointer is enough.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr pwszVal;     // Used for VT_LPWSTR
    public IntPtr reservedTail; // Pad to native PROPVARIANT size (16 bytes on x64).
}

/// <summary>
/// Frees the contents of a <see cref="PROPVARIANT"/> returned by
/// <c>IPropertyStore.GetValue</c>. The store allocates the string; the caller owns it.
/// </summary>
internal static class PropVariantNativeMethods
{
    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PROPVARIANT pvar);
}

/// <summary>Subset of Win32 VARENUM constants we actually use. Named to avoid colliding with the BCL <c>VarEnum</c> type.</summary>
internal static class PropVariantType
{
    public const ushort VT_EMPTY = 0;
    public const ushort VT_LPWSTR = 31;
}

/// <summary>
/// COM <c>IPropertyStore</c> interface — implemented by IShellLink instances so we can
/// stamp <c>PKEY_AppUserModel_ID</c> on the generated shortcut.
/// Methods declared in vtable order.
/// </summary>
[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);

    void GetAt(uint iProp, out PROPERTYKEY pkey);

    void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);

    void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);

    void Commit();
}

/// <summary>
/// Well-known property keys.
/// </summary>
internal static class PropertyKeys
{
    /// <summary>
    /// <c>PKEY_AppUserModel_ID</c> — the AUMID Windows uses to group, pin, and identify
    /// taskbar items. Documented under
    /// https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-id
    /// </summary>
    public static readonly PROPERTYKEY AppUserModelId = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    /// <summary><c>PKEY_AppUserModel_PackageFullName</c>: a packaged app's package, version included.</summary>
    public static readonly PROPERTYKEY PackageFullName = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 21,
    };

    /// <summary><c>PKEY_Link_TargetParsingPath</c>: what a Win32 entry of the installed-apps list starts.</summary>
    public static readonly PROPERTYKEY LinkTargetParsingPath = new()
    {
        fmtid = new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"),
        pid = 2,
    };

    /// <summary><c>PKEY_Link_Arguments</c>: the arguments Start passes to that target.</summary>
    public static readonly PROPERTYKEY LinkArguments = new()
    {
        fmtid = new Guid("436F2667-14E2-4FEB-B30A-146C53B5B674"),
        pid = 100,
    };
}
