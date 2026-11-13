using System;

namespace TaskbarFolders.Core.Shortcuts;

/// <summary>
/// Single source of truth for the AppUserModelID format used by both the shortcut writer
/// (Manager-side, when generating the .lnk) and the launcher process (when stamping its
/// own AUMID via <c>SetCurrentProcessExplicitAppUserModelID</c>). Both ends MUST agree on
/// this string or Windows will not consider the running launcher window an instance of
/// the pinned tile and will leave the pin "ghost-empty".
/// </summary>
public static class GroupAumid
{
    /// <summary>Stable namespace prefix for all TaskbarFolders group AUMIDs.</summary>
    public const string Prefix = TaskbarFolders.Shared.Hosting.HostEndpoint.GroupAumidPrefix;

    /// <summary>Builds the AUMID for the supplied group.</summary>
    /// <param name="groupId">Group identifier — usually a GUID hex string.</param>
    public static string For(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        return Prefix + groupId;
    }

    /// <summary>
    /// Reverses <see cref="For"/>: extracts the group id from a TaskbarFolders AUMID.
    /// Used when Windows launches the pinned-via-API tile without preserving the original
    /// command line — the launcher recovers its group from the AUMID Windows already assigned
    /// to the process via <c>GetCurrentProcessExplicitAppUserModelID</c>.
    /// </summary>
    /// <param name="aumid">AUMID candidate, e.g. <c>TaskbarFolders.Group.abc-123</c>.</param>
    /// <param name="groupId">On success, the parsed group id portion.</param>
    /// <returns><see langword="true"/> if the AUMID matches the TaskbarFolders prefix and the suffix is non-empty.</returns>
    public static bool TryExtractGroupId(string? aumid, out string groupId)
    {
        groupId = string.Empty;
        if (string.IsNullOrEmpty(aumid))
        {
            return false;
        }
        // Case-insensitive prefix match because Windows AUMID comparison itself is
        // case-insensitive (PKEY_AppUserModel_ID uses wcsicmp). A third-party tool or
        // a manual shell:::AppsFolder edit can normalise the prefix to a different case;
        // we accept any case here so recovery still works.
        if (!aumid.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var suffix = aumid[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }
        groupId = suffix;
        return true;
    }
}
