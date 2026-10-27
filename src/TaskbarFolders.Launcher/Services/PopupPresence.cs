using System;
using System.Threading;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// A per-group, machine-wide marker that a popup for the group is (about to be) on screen, and
/// the channel other launchers use to tell it to reload.
/// </summary>
/// <remarks>
/// Adding files from Explorer can start several launchers for one group at once. Only the first
/// should show a popup; the rest add their file and ask that popup to pick it up. "First" is
/// decided atomically by whoever creates the named event, before any file is added, so two
/// launchers starting together cannot both conclude that no popup exists.
/// </remarks>
public sealed class PopupPresence : IDisposable
{
    private readonly EventWaitHandle _event;
    private RegisteredWaitHandle? _registration;

    private PopupPresence(EventWaitHandle handle, bool isFirst)
    {
        _event = handle;
        IsFirst = isFirst;
    }

    /// <summary>True when no other launcher held the marker for this group when it was created.</summary>
    public bool IsFirst { get; }

    /// <summary>Name of the per-group event.</summary>
    public static string EventName(string groupId) => $"Local\\TaskbarFolders.Group.{groupId}.Popup";

    /// <summary>Creates or opens the group's marker.</summary>
    /// <param name="groupId">Group identifier.</param>
    public static PopupPresence Claim(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(groupId), out var createdNew);
        return new PopupPresence(handle, createdNew);
    }

    /// <summary>Asks the popup holding the marker to reload its group.</summary>
    public void SignalReload() => _event.Set();

    /// <summary>
    /// Calls <paramref name="onReload"/> on a thread-pool thread whenever another launcher asks
    /// this popup to reload. A signal sent before this call is delivered once registered.
    /// </summary>
    /// <param name="onReload">Callback; marshal to the UI thread yourself.</param>
    public void ListenForReload(Action onReload)
    {
        ArgumentNullException.ThrowIfNull(onReload);
        _registration ??= ThreadPool.RegisterWaitForSingleObject(
            _event, (_, _) => onReload(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        _event.Dispose();
    }
}
