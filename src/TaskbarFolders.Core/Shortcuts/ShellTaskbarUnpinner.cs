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
/// the displayed caption is not, which is why the caption is never matched. Should the menu refuse
/// the verb by name, the item whose canonical verb it is is looked up in the built menu and
/// invoked by its command id instead — the same verb, reached the way a click reaches it.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ShellTaskbarUnpinner : ITaskbarUnpinner
{
    /// <summary>Canonical name of the shell's unpin verb.</summary>
    public const string UnpinVerb = "taskbarunpin";

    // Command ids handed to QueryContextMenu start here; an item's offset is its id minus this.
    private const uint FirstCommandId = 1;
    private const int VerbBufferChars = 260;

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
            hr = menu.QueryContextMenu(hmenu, 0, FirstCommandId, 0x7FFF, NativeMethods.CMF_EXTENDEDVERBS);
            if (hr < 0)
            {
                _logger?.LogWarning("Unpin: building the menu for {Path} failed (HRESULT 0x{Hr:X8}).", pinnedShortcutPath, hr);
                return false;
            }

            hr = InvokeByName(menu);
            if (hr == 0)
            {
                _logger?.LogInformation("Unpinned {Path} from the taskbar.", pinnedShortcutPath);
                return true;
            }
            _logger?.LogWarning("Unpin: {Path} refused the unpin verb by name (HRESULT 0x{Hr:X8}); trying its menu item.", pinnedShortcutPath, hr);

            if (!TryFindVerbOffset(menu, hmenu, out var offset))
            {
                _logger?.LogWarning("Unpin: the menu of {Path} has no unpin item.", pinnedShortcutPath);
                return false;
            }

            hr = Invoke(menu, (IntPtr)offset);
            if (hr != 0)
            {
                _logger?.LogWarning("Unpin: {Path} refused its unpin menu item (HRESULT 0x{Hr:X8}).", pinnedShortcutPath, hr);
                return false;
            }

            _logger?.LogInformation("Unpinned {Path} from the taskbar through its menu item.", pinnedShortcutPath);
            return true;
        }
        finally
        {
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

    private static int InvokeByName(IContextMenu menu)
    {
        var verb = Marshal.StringToHGlobalAnsi(UnpinVerb);
        try
        {
            return Invoke(menu, verb);
        }
        finally
        {
            Marshal.FreeHGlobal(verb);
        }
    }

    // lpVerb is either a pointer to an ANSI verb name or, below 0x10000, a command offset.
    private static int Invoke(IContextMenu menu, IntPtr verb)
    {
        var info = new CMINVOKECOMMANDINFO
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
            lpVerb = verb,
            nShow = NativeMethods.SW_SHOWNORMAL,
        };
        return menu.InvokeCommand(ref info);
    }

    private static bool TryFindVerbOffset(IContextMenu menu, IntPtr hmenu, out uint offset)
    {
        offset = 0;
        var count = NativeMethods.GetMenuItemCount(hmenu);
        var buffer = Marshal.AllocHGlobal(VerbBufferChars * sizeof(char));
        try
        {
            for (var position = 0; position < count; position++)
            {
                var id = NativeMethods.GetMenuItemID(hmenu, position);
                if (id == uint.MaxValue || id < FirstCommandId)
                {
                    continue; // A submenu, a separator, or not one of the handler's commands.
                }

                Marshal.WriteInt16(buffer, 0);
                var candidate = id - FirstCommandId;
                if (menu.GetCommandString((UIntPtr)candidate, NativeMethods.GCS_VERBW, IntPtr.Zero, buffer, VerbBufferChars) == 0
                    && string.Equals(Marshal.PtrToStringUni(buffer), UnpinVerb, StringComparison.OrdinalIgnoreCase))
                {
                    offset = candidate;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
