using System;
using System.IO;

namespace TaskbarFolders.Manager.Services;

/// <summary>Where this copy of TaskbarFolders lives: the folder holding <c>Manager</c> and <c>Launcher</c>.</summary>
/// <param name="Root">Full path of that folder.</param>
public sealed record InstallLocation(string Root)
{
    /// <summary>Name of the Inno Setup uninstaller in an installed copy.</summary>
    public const string InstallerUninstallerName = "unins000.exe";

    private const string ManagerExe = "TaskbarFolders.Manager.exe";
    private const string LauncherExe = "TaskbarFolders.Launcher.exe";

    /// <summary>The copy the running Manager belongs to.</summary>
    public static InstallLocation OfRunningManager() =>
        new(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));

    /// <summary>Full path of the Manager in the <c>Manager</c> folder of this copy.</summary>
    public string PortableManagerPath => Path.Combine(Root, "Manager", ManagerExe);

    /// <summary>
    /// Whether this is the extracted portable ZIP: a <c>Manager</c> and a <c>Launcher</c> folder
    /// side by side, and no installer's uninstaller. A development build has neither, so its
    /// build output is never taken for something to delete.
    /// </summary>
    public bool IsPortable =>
        FindUninstaller() is null
        && File.Exists(PortableManagerPath)
        && File.Exists(Path.Combine(Root, "Launcher", LauncherExe));

    /// <summary>
    /// The installer's uninstaller, or <see langword="null"/> when this copy was not installed
    /// (a portable copy uninstalls through the Manager itself).
    /// </summary>
    public string? FindUninstaller()
    {
        var path = Path.Combine(Root, InstallerUninstallerName);
        return File.Exists(path) ? path : null;
    }
}
