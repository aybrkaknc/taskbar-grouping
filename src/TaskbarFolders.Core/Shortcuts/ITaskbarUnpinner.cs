namespace TaskbarFolders.Core.Shortcuts;

/// <summary>Takes a pinned shortcut off the taskbar, the way its "Unpin from taskbar" menu entry does.</summary>
public interface ITaskbarUnpinner
{
    /// <summary>
    /// Invokes the shell's <c>taskbarunpin</c> verb on <paramref name="pinnedShortcutPath"/>, a
    /// file in the taskbar's pinned-items folder. The shell removes the tile and the file.
    /// </summary>
    /// <param name="pinnedShortcutPath">Full path of the pinned <c>.lnk</c>.</param>
    /// <returns><see langword="true"/> when the shell accepted the verb.</returns>
    bool TryUnpin(string pinnedShortcutPath);
}
