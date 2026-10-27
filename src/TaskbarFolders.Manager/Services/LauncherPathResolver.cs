using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Default <see cref="ILauncherPathResolver"/>. Probes three layouts in order:
/// <list type="number">
///   <item>
///     <b>Side-by-side</b> — <c>{baseDir}/TaskbarFolders.Launcher.exe</c>. Covers single-folder
///     deployments if anyone ever ships one.
///   </item>
///   <item>
///     <b>Sibling folder</b> — <c>{baseDir}/../Launcher/TaskbarFolders.Launcher.exe</c>.
///     Matches the actual installer + portable ZIP layouts where <c>installer/setup.iss</c>
///     deploys Manager to <c>{app}\Manager</c> and Launcher to <c>{app}\Launcher</c>.
///   </item>
///   <item>
///     <b>Dev walk-up</b> — climbs to <c>TaskbarFolders.sln</c>, then probes every target
///     framework folder under <c>src/TaskbarFolders.Launcher/bin/{Cfg}/</c>. Activates only
///     when running from <c>dotnet run</c> or the test bin tree.
///   </item>
/// </list>
/// Returns the first match or <see langword="null"/>, logging the probed paths at error level
/// so support logs pinpoint which assumption failed.
/// </summary>
public sealed class LauncherPathResolver : ILauncherPathResolver
{
    /// <summary>File name of the launcher binary the Manager looks for.</summary>
    public const string LauncherFileName = "TaskbarFolders.Launcher.exe";

    /// <summary>Sibling folder name probed for the installer / portable ZIP layout.</summary>
    public const string LauncherFolderName = "Launcher";

    private readonly ILogger<LauncherPathResolver>? _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="logger">Optional logger; diagnostic paths are emitted at error level when no probe matches.</param>
    public LauncherPathResolver(ILogger<LauncherPathResolver>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string? TryResolve() => TryResolveFrom(AppContext.BaseDirectory);

    /// <summary>
    /// Test hook — runs the probe sequence against an arbitrary base directory so the installer
    /// and portable layouts can be exercised without mutating <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    internal string? TryResolveFrom(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var probed = new List<string>(3);

        var sideBySide = Path.Combine(baseDirectory, LauncherFileName);
        probed.Add(sideBySide);
        if (File.Exists(sideBySide))
        {
            return sideBySide;
        }

        // Normalise via GetFullPath so the leading ".." is collapsed before the existence
        // check — File.Exists tolerates it, but the diagnostic log should show the resolved form.
        var siblingFolder = Path.GetFullPath(
            Path.Combine(baseDirectory, "..", LauncherFolderName, LauncherFileName));
        probed.Add(siblingFolder);
        if (File.Exists(siblingFolder))
        {
            return siblingFolder;
        }

        var dir = new DirectoryInfo(baseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TaskbarFolders.sln")))
            {
                var launcherBin = Path.Combine(
                    dir.FullName,
                    "src",
                    "TaskbarFolders.Launcher",
                    "bin",
                    DetectConfiguration(baseDirectory));

                foreach (var devCandidate in DevCandidates(launcherBin))
                {
                    probed.Add(devCandidate);
                    if (File.Exists(devCandidate))
                    {
                        return devCandidate;
                    }
                }
                break;
            }
            dir = dir.Parent;
        }

        _logger?.LogError(
            "Launcher binary not found. Probed: {Probed}",
            string.Join("; ", probed));
        return null;
    }

    private static string DetectConfiguration(string baseDirectory)
    {
        // The Manager's bin path is .../bin/<Configuration>/<Tfm>/. Slice the configuration
        // segment so we point at the matching Launcher build (Debug↔Debug, Release↔Release).
        var parts = baseDirectory.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
        return parts.Length >= 2 ? parts[^2] : "Release";
    }

    /// <summary>
    /// Every <c>{launcherBin}/{Tfm}/TaskbarFolders.Launcher.exe</c> candidate, highest target
    /// framework first, falling back to the bin directory itself so the diagnostic log still
    /// names a path when nothing was built.
    /// </summary>
    /// <remarks>
    /// The launcher's target framework cannot be derived from the Manager's own bin path. The
    /// Manager builds into <c>net8.0-windows</c> while the launcher builds into
    /// <c>net8.0-windows10.0.19041.0</c> - it needs the Win10 1903 SDK projections for
    /// <c>Windows.UI.Shell.TaskbarManager</c>. Reusing the Manager's segment produced a path
    /// that can never exist, so this probe never matched in a dev tree: <c>dotnet run</c> on the
    /// Manager resolved no launcher and <c>GroupSyncService</c> bailed out before writing any
    /// icon or shortcut. Enumerating the real folders also survives future framework bumps.
    /// </remarks>
    private static List<string> DevCandidates(string launcherBinDirectory)
    {
        if (!Directory.Exists(launcherBinDirectory))
        {
            return [Path.Combine(launcherBinDirectory, LauncherFileName)];
        }

        var frameworkDirectories = Directory.GetDirectories(launcherBinDirectory);
        // Ordinal descending so a specific framework moniker wins over the bare one
        // (net8.0-windows10.0.19041.0 before net8.0-windows) and the probe order is stable.
        Array.Sort(frameworkDirectories, StringComparer.OrdinalIgnoreCase);
        Array.Reverse(frameworkDirectories);

        var candidates = new List<string>(frameworkDirectories.Length);
        foreach (var frameworkDirectory in frameworkDirectories)
        {
            candidates.Add(Path.Combine(frameworkDirectory, LauncherFileName));
        }

        if (candidates.Count == 0)
        {
            candidates.Add(Path.Combine(launcherBinDirectory, LauncherFileName));
        }

        return candidates;
    }
}
