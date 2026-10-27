using System.Threading;
using System.Threading.Tasks;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Removes every trace TaskbarFolders leaves outside its program folder. The one procedure
/// behind the installer's uninstaller, the portable <c>Uninstall.cmd</c> and Settings.
/// </summary>
public interface IUninstaller
{
    /// <summary>
    /// Closes the other TaskbarFolders processes, unpins every group tile, removes Explorer's
    /// menu, the auto-start entry, the Start menu entries and the records Windows keeps about
    /// the program, and deletes the user's groups, settings, icons and logs. Every step runs
    /// even when an earlier one failed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened; <see cref="UninstallReport.Complete"/> is false when anything was left.</returns>
    Task<UninstallReport> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IUninstaller.RunAsync"/>.</summary>
/// <param name="Complete">True when every step succeeded.</param>
/// <param name="TilesLeftPinned">Group tiles the shell would not unpin; the user has to unpin them.</param>
public sealed record UninstallReport(bool Complete, int TilesLeftPinned);
