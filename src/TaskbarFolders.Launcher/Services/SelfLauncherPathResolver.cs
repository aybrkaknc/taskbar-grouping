using System;
using System.IO;
using TaskbarFolders.Core.Groups;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// The launcher's <see cref="ILauncherPathResolver"/>: its own executable. A group's shortcuts
/// target the launcher, and the running launcher is that binary, so there is nothing to probe.
/// </summary>
/// <remarks>
/// Used when the popup re-syncs a group after adding or removing apps. Reporting the process
/// path also matches what the Manager resolves in an installed or portable layout, where the
/// Manager finds this same file next to itself.
/// </remarks>
public sealed class SelfLauncherPathResolver : ILauncherPathResolver
{
    /// <inheritdoc/>
    public string? TryResolve()
    {
        var path = Environment.ProcessPath;
        return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
    }
}
