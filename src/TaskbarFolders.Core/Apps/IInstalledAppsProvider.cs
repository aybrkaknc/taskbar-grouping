using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarFolders.Core.Apps;

/// <summary>Reads Windows' installed-apps list.</summary>
public interface IInstalledAppsProvider
{
    /// <summary>
    /// The entries the picker shows (<see cref="InstalledApp.IsListed"/>), one per AppUserModelID,
    /// sorted by name. Never throws for the shell's sake: an entry that cannot be read is left out.
    /// </summary>
    /// <param name="cancellationToken">Stops between entries.</param>
    /// <returns>The listed apps.</returns>
    Task<IReadOnlyList<InstalledApp>> ListAsync(CancellationToken cancellationToken = default);
}
