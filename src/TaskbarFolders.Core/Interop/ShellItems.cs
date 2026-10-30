using System;
using System.Runtime.InteropServices;

namespace TaskbarFolders.Core.Interop;

/// <summary>
/// <c>IShellItem2</c>, declared up to <c>GetString</c>. COM vtables are positional and a C#
/// <c>ComImport</c> interface cannot inherit one, so <c>IShellItem</c>'s five methods come first,
/// in order, and nothing after <c>GetString</c> may be called through this declaration.
/// </summary>
[ComImport]
[Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    // IShellItem
    [PreserveSig]
    int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetParent(out IntPtr ppsi);

    [PreserveSig]
    int GetDisplayName(uint sigdnName, out IntPtr ppszName);

    [PreserveSig]
    int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    [PreserveSig]
    int Compare(IntPtr psi, uint hint, out int piOrder);

    // IShellItem2
    [PreserveSig]
    int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetPropertyStoreWithCreateObject(int flags, IntPtr punkCreateObject, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetPropertyStoreForKeys(IntPtr rgKeys, uint cKeys, int flags, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int Update(IntPtr pbc);

    [PreserveSig]
    int GetProperty(ref PROPERTYKEY key, IntPtr ppropvar);

    [PreserveSig]
    int GetCLSID(ref PROPERTYKEY key, out Guid pclsid);

    [PreserveSig]
    int GetFileTime(ref PROPERTYKEY key, out long pft);

    [PreserveSig]
    int GetInt32(ref PROPERTYKEY key, out int pi);

    /// <summary>A string property; the caller frees it with <see cref="Marshal.FreeCoTaskMem"/>.</summary>
    [PreserveSig]
    int GetString(ref PROPERTYKEY key, out IntPtr ppsz);
}

/// <summary><c>IEnumShellItems</c>: the items of a shell folder, one batch at a time.</summary>
[ComImport]
[Guid("70629033-E363-4A28-A567-0DB78006E6D7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    /// <summary>Returns S_OK while items come, S_FALSE at the end.</summary>
    [PreserveSig]
    int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem2? rgelt, out uint pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IntPtr ppenum);
}

/// <summary><c>IShellItemImageFactory</c>: the picture the shell shows for an item.</summary>
[ComImport]
[Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    /// <summary>A 32-bit HBITMAP the caller deletes.</summary>
    [PreserveSig]
    int GetImage(NativeSize size, int flags, out IntPtr phbm);
}

/// <summary>Win32 <c>SIZE</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int cx;
    public int cy;
}

/// <summary>Identifiers the installed-apps code binds with.</summary>
internal static class ShellGuids
{
    /// <summary><c>BHID_EnumItems</c>: bind a folder to an <see cref="IEnumShellItems"/>.</summary>
    public static readonly Guid EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");

    /// <summary>The installed-apps folder, <c>shell:AppsFolder</c>.</summary>
    public const string AppsFolder = "shell:AppsFolder";

    /// <summary><c>SIIGBF_ICONONLY</c>: the icon, never a thumbnail of contents.</summary>
    public const int IconOnly = 0x4;

    /// <summary><c>SIGDN_NORMALDISPLAY</c>.</summary>
    public const uint NormalDisplay = 0;
}
