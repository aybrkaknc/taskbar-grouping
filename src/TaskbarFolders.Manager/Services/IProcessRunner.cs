using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Thin abstraction over <see cref="Process.Start(ProcessStartInfo)"/> so view-model
/// code paths that need to spawn helper processes can be unit-tested without actually
/// running the child binary.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Starts the process described by <paramref name="psi"/> and waits for it to exit.
    /// Returns the exit code or throws <see cref="TimeoutException"/> if the timeout
    /// elapses before exit.
    /// </summary>
    Task<int> RunAndWaitAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Starts the process described by <paramref name="psi"/> without waiting for it.</summary>
    /// <returns><see langword="false"/> when it could not be started.</returns>
    bool TryStart(ProcessStartInfo psi);
}
