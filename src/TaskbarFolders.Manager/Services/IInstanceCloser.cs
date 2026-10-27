namespace TaskbarFolders.Manager.Services;

/// <summary>Ends the other running TaskbarFolders processes, so their files can be deleted.</summary>
public interface IInstanceCloser
{
    /// <summary>
    /// Ends every Manager except this process and every Launcher (open popups), waiting
    /// briefly for each to exit.
    /// </summary>
    /// <returns>Number of processes that would not end.</returns>
    int CloseOthers();
}
