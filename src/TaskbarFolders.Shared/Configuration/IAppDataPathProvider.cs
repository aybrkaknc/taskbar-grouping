namespace TaskbarFolders.Shared.Configuration;

/// <summary>
/// Provides absolute paths under <c>%APPDATA%/TaskbarFolders</c> where group configurations,
/// settings, and generated icons are persisted.
/// </summary>
public interface IAppDataPathProvider
{
    /// <summary>Gets the root directory used by TaskbarFolders for user-scoped data.</summary>
    string AppDataRoot { get; }

    /// <summary>Gets the directory containing per-group JSON configuration files.</summary>
    string GroupsDirectory { get; }

    /// <summary>Gets the directory containing generated composite and per-group icons.</summary>
    string IconsDirectory { get; }

    /// <summary>Gets the directory containing rotated diagnostic log files.</summary>
    string LogsDirectory { get; }

    /// <summary>Gets the directory containing per-group pinnable shortcut (.lnk) files.</summary>
    string ShortcutsDirectory { get; }

    /// <summary>
    /// Gets the per-user Start Menu sub-folder where TaskbarFolders publishes .lnk
    /// shortcuts that act as the AUMID anchor required by
    /// <c>Windows.UI.Shell.TaskbarManager.RequestPinCurrentAppAsync</c>.
    /// Defaults to <c>%APPDATA%/Microsoft/Windows/Start Menu/Programs/TaskbarFolders/</c>.
    /// </summary>
    string StartMenuDirectory { get; }

    /// <summary>
    /// Gets the folder where the shell keeps its own copies of taskbar pins
    /// (<c>%APPDATA%/Microsoft/Internet Explorer/Quick Launch/User Pinned/TaskBar</c>). The
    /// app only ever updates the icon location of the copies carrying a group's AUMID.
    /// </summary>
    string PinnedTaskbarDirectory { get; }

    /// <summary>Gets the path to the global application settings file.</summary>
    string SettingsFile { get; }

    /// <summary>Builds the full path for a single group's JSON configuration file.</summary>
    /// <param name="groupId">Group identifier.</param>
    string GetGroupFile(string groupId);

    /// <summary>
    /// Builds the legacy, unversioned path of a group's composite icon (<c>&lt;id&gt;.ico</c>).
    /// Written by versions before icons were versioned; still recognised and cleaned up.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    string GetGroupIconFile(string groupId);

    /// <summary>
    /// Builds the path of one version of a group's composite icon (<c>&lt;id&gt;-&lt;version&gt;.ico</c>).
    /// Each change to the icon gets a new file name, because Windows caches a shortcut's icon
    /// by path and would keep showing the old image of a file rewritten in place.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    /// <param name="version">Eight hex digits.</param>
    string GetGroupIconFile(string groupId, string version);

    /// <summary>
    /// Whether <paramref name="path"/> names one of the group's icon files — the legacy
    /// <c>&lt;id&gt;.ico</c> or a versioned <c>&lt;id&gt;-&lt;8 hex&gt;.ico</c>. Pure; touches no files.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    /// <param name="path">File path or name to test.</param>
    bool IsGroupIconFile(string groupId, string path);

    /// <summary>
    /// Builds the path of the image a user chose as the group's icon
    /// (<c>icons/custom/&lt;id&gt;.png</c>). While it exists, sync uses it in place of the
    /// composite of the apps' icons.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    string GetGroupCustomIconFile(string groupId);

    /// <summary>Builds the full path for a single group's pinnable shortcut (.lnk) file.</summary>
    /// <param name="groupId">Group identifier — used as the file name stem so distinct groups never collide regardless of display name.</param>
    string GetGroupShortcutFile(string groupId);

    /// <summary>
    /// Builds the full path for a Start Menu shortcut. <paramref name="sanitizedFileName"/>
    /// is the .lnk filename stem (without extension) — caller is responsible for sanitising
    /// the display name first.
    /// </summary>
    string GetStartMenuShortcutFile(string sanitizedFileName);
}
