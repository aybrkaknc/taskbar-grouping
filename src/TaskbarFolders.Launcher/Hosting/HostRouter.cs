using System;
using System.Collections.Generic;

namespace TaskbarFolders.Launcher.Hosting;

/// <summary>What the resident launcher does with a request.</summary>
public enum HostAction
{
    /// <summary>Open the group's popup.</summary>
    Show,

    /// <summary>Close the group's open popup: the tile was clicked again.</summary>
    Close,

    /// <summary>The popup is open: add the files that came with the request and redraw it.</summary>
    Reload,

    /// <summary>Do nothing: this request is the second half of a click that already closed the popup.</summary>
    Ignore,
}

/// <summary>
/// Decides what a request means. A click on a tile that has its popup open must close it, but the
/// popup is already gone by the time the request arrives: the press closed it (the popup sees that
/// press) and the request comes with the release. So "the popup was just closed by a press on its
/// own tile" is remembered for a short while, and the next request for that group is swallowed.
/// Pure logic with an injectable clock, so it is tested without a window.
/// </summary>
public sealed class HostRouter
{
    /// <summary>How long a press that closed a popup keeps the group's next request from reopening it.</summary>
    public static readonly TimeSpan DismissWindow = TimeSpan.FromMilliseconds(1500);

    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, DateTimeOffset> _dismissed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance.</summary>
    /// <param name="clock">The time source; <see cref="DateTimeOffset.UtcNow"/> when omitted.</param>
    public HostRouter(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Records that a press on the group's own tile just closed its popup.</summary>
    /// <param name="groupId">The group whose popup was closed.</param>
    public void MarkDismissedByOwnTile(string groupId)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);
        _dismissed[groupId] = _clock();
    }

    /// <summary>Decides what to do with a request for <paramref name="groupId"/>.</summary>
    /// <param name="groupId">The group the request names.</param>
    /// <param name="isOpen">Whether that group's popup is open now.</param>
    /// <param name="hasFiles">Whether the request carries files to add.</param>
    /// <returns>The action to take.</returns>
    public HostAction Decide(string groupId, bool isOpen, bool hasFiles)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);

        // Files are never a toggle: a multi-selection sent from Explorer arrives as one request
        // per file, and each must be added whether or not the popup is open yet.
        if (hasFiles)
        {
            return isOpen ? HostAction.Reload : HostAction.Show;
        }

        if (_dismissed.Remove(groupId, out var at) && _clock() - at <= DismissWindow)
        {
            return HostAction.Ignore;
        }

        return isOpen ? HostAction.Close : HostAction.Show;
    }
}
