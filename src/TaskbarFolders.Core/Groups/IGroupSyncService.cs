using System.Threading;
using System.Threading.Tasks;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Core.Groups;

/// <summary>
/// Keeps the on-disk per-group artifacts (composite .ico + pinnable .lnk) in sync with the
/// current <see cref="GroupConfig"/>. Called whenever a group is persisted — by the Manager, or by
/// the popup when apps are added or removed there.
/// </summary>
public interface IGroupSyncService
{
    /// <summary>
    /// Regenerates the composite icon and the per-group shortcut. No-op when the group has
    /// zero apps — the shortcut is left untouched so a pinned tile does not silently lose
    /// its icon while the user is half-way through editing.
    /// </summary>
    Task SyncAsync(GroupConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the per-group icon, the per-group .lnk under <c>%APPDATA%/TaskbarFolders/shortcuts</c>,
    /// and the Start Menu anchor .lnk under <c>%APPDATA%/Microsoft/Windows/Start Menu/Programs/TaskbarFolders</c>.
    /// Silent on missing files. <paramref name="displayName"/> locates the current Start Menu
    /// file (filename = sanitised display name); any further anchor carrying the group's AUMID,
    /// such as one written under an earlier name, is removed as well.
    /// </summary>
    void RemoveArtifacts(string groupId, string displayName);

    /// <summary>
    /// Deletes Start Menu anchors whose AUMID names a group that no longer has a config file.
    /// Anchors without a TaskbarFolders AUMID are left alone. Used by the Manager startup
    /// reconciler. Returns the number of files removed.
    /// </summary>
    int RemoveOrphanedStartMenuAnchors();

    /// <summary>
    /// Ensures the Start Menu anchor .lnk for the supplied group exists, writing it from the
    /// already-present per-group .ico if missing. Used by the Manager startup reconciler to
    /// heal v0.4.0 installs that pre-date the Start Menu anchor convention. Returns
    /// <see langword="true"/> when a new file was written.
    /// </summary>
    bool EnsureStartMenuShortcut(GroupConfig config);
}
