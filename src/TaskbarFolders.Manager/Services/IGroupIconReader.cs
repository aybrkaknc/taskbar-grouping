using System.Windows.Media.Imaging;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Reads the icon a group currently has on the taskbar, for display next to its name.
/// </summary>
public interface IGroupIconReader
{
    /// <summary>
    /// The group's current icon, or <see langword="null"/> when it has none yet (no apps, never
    /// synced) or the file cannot be read.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    BitmapSource? TryRead(string groupId);
}
