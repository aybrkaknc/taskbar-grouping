namespace TaskbarFolders.Core.Shortcuts;

/// <summary>
/// Generates per-group Windows shortcuts (.lnk) that can be pinned to the taskbar.
/// Each shortcut points to a single shared host executable, distinguished by command-line
/// arguments and a distinct AUMID so Windows treats it as a separate pinnable identity.
/// </summary>
public interface IShortcutGenerator
{
    /// <summary>
    /// Builds the AUMID Windows uses to identify a group's pinned tile.
    /// Stable for a given <paramref name="groupId"/> so re-saving never breaks an existing pin.
    /// </summary>
    /// <param name="groupId">The group identifier.</param>
    /// <returns>An AUMID of the form <c>TaskbarFolders.Group.{groupId}</c>.</returns>
    string BuildAumid(string groupId);

    /// <summary>
    /// Creates or replaces the shortcut described by <paramref name="request"/>.
    /// Existing files are overwritten atomically.
    /// </summary>
    void Generate(GroupShortcutRequest request);

    /// <summary>
    /// Points an existing shortcut at a different icon, leaving its target, arguments and
    /// AppUserModelID as they are. Used on the taskbar's pinned copy of a group shortcut, which
    /// Windows owns and which keeps the icon path it was created with.
    /// </summary>
    /// <param name="shortcutPath">Existing <c>.lnk</c> to update in place.</param>
    /// <param name="iconPath">New icon file.</param>
    /// <returns><see langword="false"/> when the shortcut is missing or could not be rewritten; never throws for those.</returns>
    bool TryUpdateIcon(string shortcutPath, string iconPath);
}
