using System.Collections.Generic;
using System.Linq;

namespace TaskbarFolders.Core.Shortcuts;

/// <summary>
/// Finds and removes the taskbar tiles of groups, recognised by their AUMID in the shell's
/// pinned-items folder — Windows names the pinned copy itself, so the file name says nothing.
/// </summary>
public interface IPinnedTileRemover
{
    /// <summary>Unpins every taskbar tile carrying the AUMID of <paramref name="groupId"/>.</summary>
    /// <param name="groupId">Group identifier.</param>
    /// <returns>How many tiles were removed, and the paths of those the shell refused.</returns>
    PinnedTileRemoval Unpin(string groupId);

    /// <summary>Whether a taskbar tile carrying the AUMID of <paramref name="groupId"/> exists. Reads only; never throws.</summary>
    /// <param name="groupId">Group identifier.</param>
    bool IsPinned(string groupId);

    /// <summary>
    /// Lists the pinned tiles of groups that no longer exist: a group AUMID with no
    /// <c>groups/&lt;id&gt;.json</c>. Reads only; never throws.
    /// </summary>
    IReadOnlyList<string> FindOrphans();
}

/// <summary>Outcome of <see cref="IPinnedTileRemover.Unpin"/>.</summary>
/// <param name="Removed">Tiles the shell took off the taskbar.</param>
/// <param name="Left">Paths of the pinned copies the shell refused to unpin.</param>
public sealed record PinnedTileRemoval(int Removed, IReadOnlyList<string> Left)
{
    /// <summary>Whether the group had any tile on the taskbar at all.</summary>
    public bool Found => Removed > 0 || Left.Count > 0;

    // Compares the paths, not the list instance.
    /// <inheritdoc/>
    public bool Equals(PinnedTileRemoval? other) =>
        other is not null && Removed == other.Removed && Left.SequenceEqual(other.Left);

    /// <inheritdoc/>
    public override int GetHashCode() => Removed;
}
