using System;
using System.IO;
using System.Linq;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Core.Groups;

/// <summary>
/// Finds a group's generated icon on disk. The file name changes with every new image
/// (ADR-006), so the current one is the newest file <see cref="IAppDataPathProvider.IsGroupIconFile"/>
/// accepts — never a fixed path.
/// </summary>
public static class GroupIconFiles
{
    /// <summary>
    /// The group's most recently written icon file, or <see langword="null"/> when it has none
    /// (an empty group, a group never synced) or the icons folder cannot be read.
    /// </summary>
    /// <param name="paths">Path provider locating the icons folder.</param>
    /// <param name="groupId">Group identifier.</param>
    public static string? FindCurrent(IAppDataPathProvider paths, string groupId)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            return Directory.Exists(paths.IconsDirectory)
                ? Directory.EnumerateFiles(paths.IconsDirectory, "*.ico")
                    .Where(icon => paths.IsGroupIconFile(groupId, icon))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
