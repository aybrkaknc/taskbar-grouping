using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Core.Apps;

/// <summary>
/// <see cref="IInstalledAppsProvider"/> over <c>shell:AppsFolder</c>: binds the folder, walks its
/// items with <see cref="IEnumShellItems"/> and reads each one's name, AppUserModelID, link target,
/// link arguments and package. Measured at about 250 ms for 195 apps (ADR-009). Runs off the
/// caller's thread, inside the process-wide shell lock.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellInstalledAppsProvider : IInstalledAppsProvider
{
    private readonly ILogger<ShellInstalledAppsProvider>? _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="logger">Optional logger.</param>
    public ShellInstalledAppsProvider(ILogger<ShellInstalledAppsProvider>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => (IReadOnlyList<InstalledApp>)Read(cancellationToken), cancellationToken);

    private List<InstalledApp> Read(CancellationToken cancellationToken)
    {
        var apps = new List<InstalledApp>();
        lock (ShellAccess.Lock)
        {
            var folderIid = typeof(IShellItem2).GUID;
            if (NativeMethods.SHCreateItemFromParsingNameAny(ShellGuids.AppsFolder, IntPtr.Zero, ref folderIid, out var folderObject) != 0
                || folderObject is not IShellItem2 folder)
            {
                _logger?.LogWarning("The installed-apps folder could not be opened.");
                return apps;
            }

            try
            {
                var bhid = ShellGuids.EnumItems;
                var enumIid = typeof(IEnumShellItems).GUID;
                if (folder.BindToHandler(IntPtr.Zero, ref bhid, ref enumIid, out var enumPtr) != 0 || enumPtr == IntPtr.Zero)
                {
                    _logger?.LogWarning("The installed-apps folder could not be listed.");
                    return apps;
                }

                var items = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
                Marshal.Release(enumPtr);
                try
                {
                    while (!cancellationToken.IsCancellationRequested && items.Next(1, out var item, out var fetched) == 0 && fetched == 1 && item is not null)
                    {
                        try
                        {
                            if (ReadOne(item) is { IsListed: true } app)
                            {
                                apps.Add(app);
                            }
                        }
                        finally
                        {
                            Marshal.FinalReleaseComObject(item);
                        }
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(items);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(folder);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return apps
            .GroupBy(a => a.Aumid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static InstalledApp? ReadOne(IShellItem2 item)
    {
        var aumid = GetString(item, PropertyKeys.AppUserModelId);
        string? name = null;
        if (item.GetDisplayName(ShellGuids.NormalDisplay, out var namePtr) == 0)
        {
            name = TakeString(namePtr);
        }

        if (string.IsNullOrWhiteSpace(aumid) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new InstalledApp(
            name,
            aumid,
            GetString(item, PropertyKeys.LinkTargetParsingPath),
            GetString(item, PropertyKeys.LinkArguments),
            GetString(item, PropertyKeys.PackageFullName));
    }

    // A property that is not set comes back as a failed HRESULT; that is "no value", not an error.
    private static string? GetString(IShellItem2 item, PROPERTYKEY key)
    {
        var copy = key;
        return item.GetString(ref copy, out var ptr) == 0 ? TakeString(ptr) : null;
    }

    private static string? TakeString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var value = Marshal.PtrToStringUni(ptr);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}
