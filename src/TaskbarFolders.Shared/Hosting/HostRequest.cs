using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TaskbarFolders.Shared.Hosting;

/// <summary>
/// A request to the resident launcher: show (or toggle) the popup of one group at the place the
/// tile was clicked. Sent by every launcher process the shell starts for a pinned tile, a menu
/// entry or a dropped file; it is one line of text so that the sender needs neither JSON nor
/// reflection (a native-AOT client sends it too).
/// </summary>
/// <remarks>
/// Wire form: <c>v1 TAB groupId TAB x TAB y [TAB file]...</c>, UTF-8, one line. Nothing in it can
/// contain a TAB: group ids are limited to letters, digits and <c>. _ -</c>, and Windows file
/// names cannot hold one.
/// </remarks>
/// <param name="GroupId">The group to show.</param>
/// <param name="X">Cursor X at the click, in device pixels.</param>
/// <param name="Y">Cursor Y at the click, in device pixels.</param>
/// <param name="Files">Files to add to the group first (Explorer's menu, a file dropped on the tile).</param>
public sealed record HostRequest(string GroupId, int X, int Y, IReadOnlyList<string> Files)
{
    private const string Version = "v1";
    private const char Separator = '\t';
    private const int MaxGroupIdLength = 96;

    /// <summary>The request as one line, without a line terminator.</summary>
    /// <returns>The wire form.</returns>
    public string ToLine()
    {
        var parts = new List<string>(4 + Files.Count)
        {
            Version,
            GroupId,
            X.ToString(CultureInfo.InvariantCulture),
            Y.ToString(CultureInfo.InvariantCulture),
        };
        parts.AddRange(Files);
        return string.Join(Separator, parts);
    }

    /// <summary>Reads a request from its wire form.</summary>
    /// <param name="line">One line, as <see cref="ToLine"/> writes it.</param>
    /// <param name="request">The request, when the line is a valid one.</param>
    /// <returns><see langword="true"/> for a well-formed request with a valid group id.</returns>
    public static bool TryParse(string? line, [NotNullWhen(true)] out HostRequest? request)
    {
        request = null;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var parts = line.Split(Separator);
        if (parts.Length < 4
            || !string.Equals(parts[0], Version, StringComparison.Ordinal)
            || !IsValidGroupId(parts[1])
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        var files = new List<string>(parts.Length - 4);
        for (var i = 4; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                files.Add(parts[i]);
            }
        }

        request = new HostRequest(parts[1], x, y, files);
        return true;
    }

    // Same alphabet as AppDataPathProvider's group-id whitelist: a request names a file under the
    // per-app data root, so it must not be able to carry a path.
    private static bool IsValidGroupId(string id)
    {
        // ".." is made of allowed characters but names a parent folder.
        if (id.Length is 0 or > MaxGroupIdLength || id.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in id)
        {
            var ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
