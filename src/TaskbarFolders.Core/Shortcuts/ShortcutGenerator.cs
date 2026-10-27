using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Interop;

namespace TaskbarFolders.Core.Shortcuts;

/// <summary>
/// Default <see cref="IShortcutGenerator"/>. Uses <see cref="IShellLinkW"/> +
/// <see cref="IPersistFile"/> + <see cref="IPropertyStore"/> to author a .lnk that
/// targets <c>Launcher.exe --group-id &lt;id&gt;</c>, carries the group's composite icon,
/// and stamps a distinct AUMID so Windows can pin and group it independently from other
/// shortcuts that share the same target binary.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShortcutGenerator : IShortcutGenerator
{
    private readonly ILogger<ShortcutGenerator>? _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="logger">Optional logger.</param>
    public ShortcutGenerator(ILogger<ShortcutGenerator>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string BuildAumid(string groupId) => GroupAumid.For(groupId);

    /// <inheritdoc/>
    public void Generate(GroupShortcutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetExePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IconPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ShortcutPath);

        var directory = Path.GetDirectoryName(request.ShortcutPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Instantiate ShellLink outside the try so a failed CoCreateInstance cannot reach
        // the finally with a half-constructed RCW.
        var link = new ShellLink();
        try
        {
            var shellLink = (IShellLinkW)link;

            shellLink.SetPath(request.TargetExePath);
            shellLink.SetArguments($"--group-id \"{request.GroupId}\"");
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(request.TargetExePath) ?? string.Empty);
            shellLink.SetDescription(string.IsNullOrWhiteSpace(request.DisplayName) ? request.GroupId : request.DisplayName);
            shellLink.SetIconLocation(request.IconPath, 0);

            StampAumid(link, BuildAumid(request.GroupId));

            // Save atomically: write to .tmp first, then replace, so a crashed write never
            // leaves a half-written .lnk that Explorer would render as a broken pin.
            var temp = request.ShortcutPath + ".tmp";
            var moved = false;
            try
            {
                ((IPersistFile)link).Save(temp, fRemember: true);
                File.Move(temp, request.ShortcutPath, overwrite: true);
                moved = true;
            }
            finally
            {
                if (!moved && File.Exists(temp))
                {
                    // Save or Move threw — clean up the half-written .tmp so it never lingers
                    // alongside the real shortcuts. Best-effort: catch the realistic IO-shaped
                    // exceptions (UnauthorizedAccessException for ACL'd files, IOException for
                    // locked files) so this cleanup cannot mask the original Save/Move failure
                    // the caller actually needs to see.
                    try { File.Delete(temp); }
                    catch (IOException) { /* best-effort */ }
                    catch (UnauthorizedAccessException) { /* best-effort */ }
                }
            }

            _logger?.LogInformation(
                "Wrote shortcut {Path} for group {GroupId} with AUMID {Aumid}.",
                request.ShortcutPath, request.GroupId, BuildAumid(request.GroupId));
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <inheritdoc/>
    public bool TryUpdateIcon(string shortcutPath, string iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
        {
            return false;
        }

        var link = new ShellLink();
        try
        {
            // Saved in place rather than through a .tmp: the file belongs to Windows' pinned
            // folder, and replacing it with a new file could break the pin's tracking.
            ((IPersistFile)link).Load(shortcutPath, StgmReadWrite);
            ((IShellLinkW)link).SetIconLocation(iconPath, 0);
            ((IPersistFile)link).Save(shortcutPath, fRemember: true);
            _logger?.LogInformation("Pointed {Path} at icon {Icon}.", shortcutPath, iconPath);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not update the icon of {Path}.", shortcutPath);
            return false;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>STGM_READWRITE: the pinned copy is loaded to be saved back in place.</summary>
    private const uint StgmReadWrite = 0x00000002;

    private static void StampAumid(object link, string aumid)
    {
        var store = (IPropertyStore)link;

        var key = PropertyKeys.AppUserModelId;
        var ptr = Marshal.StringToCoTaskMemUni(aumid);
        var variant = new PROPVARIANT { vt = PropVariantType.VT_LPWSTR, pwszVal = ptr };

        try
        {
            store.SetValue(ref key, ref variant);
            store.Commit();
        }
        finally
        {
            // SetValue copied the string into the COM object; we own the temp allocation
            // and must free it ourselves.
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}
