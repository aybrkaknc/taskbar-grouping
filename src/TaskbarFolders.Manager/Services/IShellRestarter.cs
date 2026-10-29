using System.Threading.Tasks;

namespace TaskbarFolders.Manager.Services;

/// <summary>Restarts Windows Explorer, which redraws the taskbar. Only ever on the user's say-so.</summary>
public interface IShellRestarter
{
    /// <summary>
    /// Ends the user's Explorer and waits for it to come back, starting it if Windows does not.
    /// Never throws; failures are logged.
    /// </summary>
    Task RestartAsync();
}
