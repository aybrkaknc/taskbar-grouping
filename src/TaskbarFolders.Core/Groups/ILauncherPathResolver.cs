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

    /// <summary>
    /// Resolves the tiny native client that a shortcut should start in place of the launcher: the
    /// file <c>TaskbarFolders.Click.exe</c> next to the launcher. It hands a click to the resident
    /// launcher and exits, which is much quicker than starting the launcher itself. Null when there
    /// is none (a build without it, or an older install), in which case shortcuts start the
    /// launcher, which does the same hand-over, only slower.
    /// </summary>
    string? TryResolveClick()
    {
        var launcher = TryResolve();
        if (string.IsNullOrEmpty(launcher))
        {
            return null;
        }

        var click = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(launcher) ?? string.Empty, ClickFileName);
        return System.IO.File.Exists(click) ? click : null;
    }

    /// <summary>File name of the click client.</summary>
    const string ClickFileName = "TaskbarFolders.Click.exe";
}
