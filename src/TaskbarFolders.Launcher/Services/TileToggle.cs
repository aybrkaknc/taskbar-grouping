using System;
using System.Threading;
using System.Windows;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Makes a second click on a group's taskbar tile close its popup instead of opening a new one.
/// </summary>
/// <remarks>
/// The click that should close the popup is also a click on the tile, which starts a new
/// launcher. The outside-click hook sees the press, and the shell starts the launcher on the
/// release, a few hundred milliseconds later — by then the old popup is gone. So the popup
/// that is closed by a press on its own tile leaves a per-group marker behind and keeps its
/// (windowless) process alive briefly; a launcher that finds the marker shows nothing.
/// </remarks>
public sealed class TileToggle : IDisposable
{
    /// <summary>How long a dismissed popup waits for the launcher its tile click starts.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// How far, in DIPs, a press may land from the click that opened the popup and still count
    /// as a press on the same tile. Generous on purpose: the marker is per group, so the only
    /// false positive is a press beside the tile followed by a click on it within
    /// <see cref="Window"/>.
    /// </summary>
    public const double ToleranceDip = 40;

    private readonly EventWaitHandle _event;

    private TileToggle(EventWaitHandle handle) => _event = handle;

    /// <summary>Name of the per-group marker.</summary>
    /// <param name="groupId">Group identifier.</param>
    public static string EventName(string groupId) => $"Local\\TaskbarFolders.Group.{groupId}.Dismissed";

    /// <summary>
    /// Whether a press at (<paramref name="x"/>, <paramref name="y"/>) lands on the tile whose
    /// click opened the popup at <paramref name="anchor"/>. All coordinates are device pixels.
    /// </summary>
    /// <param name="anchor">Cursor position when the popup's launcher started.</param>
    /// <param name="x">Press X.</param>
    /// <param name="y">Press Y.</param>
    /// <param name="dpiScale">Device pixels per DIP on the taskbar's monitor.</param>
    public static bool IsOnOpeningTile(Point anchor, int x, int y, double dpiScale)
    {
        var tolerance = ToleranceDip * dpiScale;
        return Math.Abs(x - anchor.X) <= tolerance && Math.Abs(y - anchor.Y) <= tolerance;
    }

    /// <summary>Leaves the marker for <paramref name="groupId"/>; dispose to remove it.</summary>
    /// <param name="groupId">Group identifier.</param>
    public static TileToggle MarkDismissed(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        return new TileToggle(new EventWaitHandle(false, EventResetMode.ManualReset, EventName(groupId)));
    }

    /// <summary>
    /// Consumes the marker if a popup for <paramref name="groupId"/> was just closed from its
    /// tile. A launcher that gets <see langword="true"/> shows nothing.
    /// </summary>
    /// <param name="groupId">Group identifier.</param>
    public static bool TryConsume(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (!EventWaitHandle.TryOpenExisting(EventName(groupId), out var handle))
        {
            return false;
        }

        using (handle)
        {
            // Lets the dismissed popup's process exit now instead of at the end of its wait.
            handle.Set();
        }

        return true;
    }

    /// <summary>Blocks until the next launcher consumes the marker, or <paramref name="timeout"/> passes.</summary>
    /// <param name="timeout">Longest wait.</param>
    /// <returns><see langword="true"/> when a launcher consumed it.</returns>
    public bool WaitForNextLaunch(TimeSpan timeout) => _event.WaitOne(timeout);

    /// <inheritdoc/>
    public void Dispose() => _event.Dispose();
}
