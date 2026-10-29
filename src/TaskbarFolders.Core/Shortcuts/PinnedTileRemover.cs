using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Core.Shortcuts;

/// <summary>Default <see cref="IPinnedTileRemover"/>.</summary>
public sealed class PinnedTileRemover : IPinnedTileRemover
{
    private readonly IAppDataPathProvider _paths;
    private readonly IShortcutReader _reader;
    private readonly ITaskbarUnpinner _unpinner;

    /// <summary>Initializes a new instance.</summary>
    public PinnedTileRemover(IAppDataPathProvider paths, IShortcutReader reader, ITaskbarUnpinner unpinner)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(unpinner);

        _paths = paths;
        _reader = reader;
        _unpinner = unpinner;
    }

    /// <inheritdoc/>
    public PinnedTileRemoval Unpin(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var removed = 0;
        var left = new List<string>();
        foreach (var (path, id) in GroupTiles())
        {
            if (!string.Equals(id, groupId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_unpinner.TryUnpin(path))
            {
                removed++;
            }
            else
            {
                left.Add(path);
            }
        }

        return new PinnedTileRemoval(removed, left);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FindOrphans()
    {
        // Known ids come from the file names, as in the Start menu anchor sweep: a group whose
        // JSON does not parse still owns its tile.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(_paths.GroupsDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(_paths.GroupsDirectory, "*.json"))
                {
                    known.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a list of groups every tile would look orphaned.
            return [];
        }

        return GroupTiles().Where(t => !known.Contains(t.GroupId)).Select(t => t.Path).ToList();
    }

    private List<(string Path, string GroupId)> GroupTiles()
    {
        var tiles = new List<(string, string)>();
        try
        {
            if (!Directory.Exists(_paths.PinnedTaskbarDirectory))
            {
                return tiles;
            }

            foreach (var path in Directory.EnumerateFiles(_paths.PinnedTaskbarDirectory, "*.lnk"))
            {
                if (GroupAumid.TryExtractGroupId(_reader.TryReadAumid(path), out var id))
                {
                    tiles.Add((path, id));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing found is the honest answer when the folder cannot be read.
        }

        return tiles;
    }
}
