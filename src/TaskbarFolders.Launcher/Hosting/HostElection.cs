using System;
using System.Threading;
using TaskbarFolders.Shared.Hosting;

namespace TaskbarFolders.Launcher.Hosting;

/// <summary>
/// Decides which launcher process is the resident one. The host owns a named mutex for as long as
/// it lives; the kernel removes it when the process dies, so a crashed host never leaves a stale
/// claim behind. Nobody waits on the mutex: only its existence matters.
/// </summary>
public sealed class HostElection : IDisposable
{
    private readonly string _name;
    private Mutex? _mutex;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="name">The mutex name; <see cref="HostEndpoint.MutexName"/> in production, a unique one in tests.</param>
    public HostElection(string? name = null)
    {
        _name = name ?? HostEndpoint.MutexName;
    }

    /// <summary>Whether a resident launcher exists: its mutex can be opened.</summary>
    /// <param name="name">The mutex name; <see cref="HostEndpoint.MutexName"/> when omitted.</param>
    /// <returns><see langword="true"/> when some process holds the mutex.</returns>
    public static bool HostExists(string? name = null) => HostEndpoint.HostExists(name);

    /// <summary>Tries to become the resident launcher.</summary>
    /// <returns><see langword="true"/> when this process now holds the mutex; <see langword="false"/> when another does.</returns>
    public bool TryBecomeHost()
    {
        if (_mutex is not null)
        {
            return true;
        }

        var mutex = new Mutex(initiallyOwned: false, _name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _mutex?.Dispose();
        _mutex = null;
    }
}
