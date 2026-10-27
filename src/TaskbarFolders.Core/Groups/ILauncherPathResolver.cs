namespace TaskbarFolders.Core.Groups;

/// <summary>
/// Locates the host <c>TaskbarFolders.Launcher.exe</c> binary that per-group shortcuts target.
/// Abstracted from the default filesystem resolver so unit tests can supply a fixture path.
/// </summary>
public interface ILauncherPathResolver
{
    /// <summary>
    /// Resolves an absolute path to the launcher executable, or <see langword="null"/> if it
    /// cannot be found. <see langword="null"/> is a non-fatal signal: sync logs it and skips the
    /// shortcut rather than failing the save. The Manager probes its install layouts; the launcher
    /// answers with its own path, since it is the shortcut target.
    /// </summary>
    string? TryResolve();
}
