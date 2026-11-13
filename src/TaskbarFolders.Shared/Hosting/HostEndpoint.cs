using System.Diagnostics;

namespace TaskbarFolders.Shared.Hosting;

/// <summary>
/// Names of the kernel objects the resident launcher and its clients meet at. Pipe names are
/// machine-wide, so the name carries the user and the logon session: a second session of the same
/// user must not reach the first one's popups.
/// </summary>
public static class HostEndpoint
{
    /// <summary>
    /// Held by the resident launcher for as long as it lives. A client that can open it knows a
    /// host exists without trying the pipe. <c>Local\</c> is per logon session.
    /// </summary>
    public const string MutexName = @"Local\TaskbarFolders.Host";

    /// <summary>
    /// Prefix of every group's AppUserModelID. Kept here, below both the launcher and the native
    /// click client, so that the client can recover a group id from its AUMID without referencing
    /// the Core assembly; <c>GroupAumid</c> builds the ids from the same constant.
    /// </summary>
    public const string GroupAumidPrefix = "TaskbarFolders.Group.";

    /// <summary>The named pipe the resident launcher listens on.</summary>
    public static string PipeName { get; } = BuildPipeName();

    /// <summary>Whether a resident launcher exists: its mutex can be opened.</summary>
    /// <param name="mutexName">The mutex name; <see cref="MutexName"/> when omitted.</param>
    /// <returns><see langword="true"/> when some process holds the mutex.</returns>
    public static bool HostExists(string? mutexName = null)
    {
        if (!Mutex.TryOpenExisting(mutexName ?? MutexName, out var existing))
        {
            return false;
        }

        existing.Dispose();
        return true;
    }

    private static string BuildPipeName()
    {
        using var process = Process.GetCurrentProcess();
        return $"TaskbarFolders.Host.{Environment.UserName}.{process.SessionId}";
    }
}
