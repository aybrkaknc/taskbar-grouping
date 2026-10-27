using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Core.Shortcuts;

/// <summary>Default <see cref="ITaskbarUnpinner"/>, through the item's shell context menu.</summary>
/// <remarks>
/// Windows offers no API for an app to unpin itself, but the pinned shortcut's own context menu
/// carries "Unpin from taskbar", and invoking it by its canonical name works from a normal,
/// non-elevated process (verified on Windows 11 build 26300). The verb name is language-neutral;
/// the displayed caption is not, which is why the caption is never matched.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ShellTaskbarUnpinner : ITaskbarUnpinner
{
    /// <summary>Canonical name of the shell's unpin verb.</summary>
    public const string UnpinVerb = "taskbarunpin";

    private readonly ILogger<ShellTaskbarUnpinner>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public ShellTaskbarUnpinner(ILogger<ShellTaskbarUnpinner>? logger = null) => _logger = logger;

    /// <inheritdoc/>
    public bool TryUnpin(string pinnedShortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pinnedShortcutPath);

        var iidItem = NativeMethods.IID_IShellItem;
        var hr = NativeMethods.SHCreateItemFromParsingName(pinnedShortcutPath, IntPtr.Zero, ref iidItem, out var item);
        if (hr != 0 || item is null)
        {
            _logger?.LogWarning("Unpin: could not open {Path} (HRESULT 0x{Hr:X8}).", pinnedShortcutPath, hr);
            return false;
        }

        IContextMenu? menu = null;
        var hmenu = IntPtr.Zero;
        var verb = IntPtr.Zero;
        try
        {
            var bhid = NativeMethods.BHID_SFUIObject;
            var iidMenu = NativeMethods.IID_IContextMenu;
            hr = item.BindToHandler(IntPtr.Zero, ref bhid, ref iidMenu, out var unknown);
            if (hr != 0)
            {
                _logger?.LogWarning("Unpin: no context menu for {Path} (HRESULT 0x{Hr:X8}).", pinnedShortcutPath, hr);
                return false;
            }

            menu = (IContextMenu)Marshal.GetObjectForIUnknown(unknown);
            Marshal.Release(unknown);

            // The verb is only invocable after the menu has been built.
            hmenu = NativeMethods.CreatePopupMenu();
            hr = menu.QueryContextMenu(hmenu, 0, 1, 0x7FFF, NativeMethods.CMF_EXTENDEDVERBS);
            if (hr < 0)
            {
                _logger?.LogWarning("Unpin: building the menu for {Path} failed (HRESULT 0x{Hr:X8}).", pinnedShortcutPath, hr);
                return false;
            }

            verb = Marshal.StringToHGlobalAnsi(UnpinVerb);
            var info = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                lpVerb = verb,
                nShow = NativeMethods.SW_SHOWNORMAL,
            };
            hr = menu.InvokeCommand(ref info);
            if (hr != 0)
            {
                _logger?.LogWarning("Unpin: {Path} refused the unpin verb (HRESULT 0x{Hr:X8}).", pinnedShortcutPath, hr);
                return false;
            }

            _logger?.LogInformation("Unpinned {Path} from the taskbar.", pinnedShortcutPath);
            return true;
        }
        finally
        {
            if (verb != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(verb);
            }
            if (hmenu != IntPtr.Zero)
            {
                _ = NativeMethods.DestroyMenu(hmenu);
            }
            if (menu is not null)
            {
                Marshal.FinalReleaseComObject(menu);
            }
            Marshal.FinalReleaseComObject(item);
        }
    }
}
