using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Core.Shortcuts;

/// <summary>
/// Default <see cref="IShellChangeNotifier"/>. Marshals the path to an unmanaged
/// wide-string buffer and invokes <see cref="NativeMethods.SHChangeNotify"/> with
/// <c>SHCNE_CREATE</c> + <c>SHCNF_PATHW | SHCNF_FLUSH</c>.
/// </summary>
/// <remarks>
/// <c>SHCNF_FLUSH</c> blocks until pending Shell notifications are delivered. The cost is
/// a few milliseconds and is paid only on sync (group save / heal-up), not on the hot
/// launcher click path.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ShellChangeNotifier : IShellChangeNotifier
{
    private readonly ILogger<ShellChangeNotifier>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public ShellChangeNotifier(ILogger<ShellChangeNotifier>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void NotifyCreate(string path) => Notify(NativeMethods.SHCNE_CREATE, "SHCNE_CREATE", path);

    /// <inheritdoc/>
    public void NotifyUpdate(string path) => Notify(NativeMethods.SHCNE_UPDATEITEM, "SHCNE_UPDATEITEM", path);

    private void Notify(int eventId, string eventName, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToHGlobalUni(path);
            NativeMethods.SHChangeNotify(
                eventId,
                NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSH,
                pathPtr,
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            // Shell-notify failure must never break the calling sync flow — the .lnk is
            // already on disk and will be picked up by the background indexer eventually.
            // Logged at Warning so support logs surface a repeating failure pattern.
            _logger?.LogWarning(ex, "SHChangeNotify({Event}, {Path}) threw.", eventName, path);
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pathPtr);
            }
        }
    }
}
