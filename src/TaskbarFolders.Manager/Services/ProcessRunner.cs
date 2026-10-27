using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Default <see cref="IProcessRunner"/> backed by <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc/>
    public async Task<int> RunAndWaitAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(psi);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process {psi.FileName}.");

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Try to clean up — best-effort, the kernel may take a moment.
            try { process.Kill(entireProcessTree: true); } catch { /* ignored */ }
            throw new TimeoutException($"Process {psi.FileName} did not exit within {timeout}.");
        }

        return process.ExitCode;
    }

    /// <inheritdoc/>
    public bool TryStart(ProcessStartInfo psi)
    {
        ArgumentNullException.ThrowIfNull(psi);
        try
        {
            using var process = Process.Start(psi);
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
