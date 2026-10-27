using System.Threading;
using System.Threading.Tasks;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Removes what TaskbarFolders registered outside its own folder, so a portable copy can be
/// deleted without leaving entries that start a program that is gone.
/// </summary>
public interface IShellIntegrationRemover
{
    /// <summary>
    /// Removes Explorer's "Taskbar Groups" menu, the auto-start entry, and every group's icon,
    /// shortcut and Start menu entry, and switches the menu and auto-start settings off so the
    /// next Manager start does not write them back. Group definitions are kept, and pinned
    /// taskbar tiles are left alone — they are the shell's files.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when every step succeeded.</returns>
    Task<bool> RemoveAsync(CancellationToken cancellationToken = default);
}
