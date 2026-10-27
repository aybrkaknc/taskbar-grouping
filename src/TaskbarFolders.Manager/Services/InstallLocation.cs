using System;
using System.IO;

namespace TaskbarFolders.Manager.Services;

/// <summary>Where this copy of TaskbarFolders lives: the folder holding <c>Manager</c> and <c>Launcher</c>.</summary>
/// <param name="Root">Full path of that folder.</param>
public sealed record InstallLocation(string Root)
{
    /// <summary>Name of the Inno Setup uninstaller in an installed copy.</summary>
    public const string InstallerUninstallerName = "unins000.exe";

    /// <summary>Name of the script in a portable copy.</summary>
    public const string PortableUninstallerName = "Uninstall.cmd";

    /// <summary>The copy the running Manager belongs to.</summary>
    public static InstallLocation OfRunningManager() =>
        new(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));

    /// <summary>
    /// The uninstaller that ships with this copy — the installer's, else the portable
    /// script — or <see langword="null"/> for a development build, which has neither.
    /// </summary>
    public string? FindUninstaller()
    {
        foreach (var name in new[] { InstallerUninstallerName, PortableUninstallerName })
        {
            var path = Path.Combine(Root, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
