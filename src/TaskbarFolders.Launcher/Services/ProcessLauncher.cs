using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Default <see cref="IProcessLauncher"/>. Uses <see cref="Process.Start(ProcessStartInfo)"/>
/// with <c>UseShellExecute = true</c> so the OS resolves <c>.lnk</c> targets, applies
/// associations, and quotes paths correctly. The working directory is set to the
/// target's containing folder so apps that load resources by relative path work — except for an
/// installed app stored by its <c>shell:AppsFolder</c> entry (ADR-009), which the shell starts as
/// Start does.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessLauncher : IProcessLauncher
{
    private readonly ILogger<ProcessLauncher>? _logger;
    private readonly Func<ProcessStartInfo, Process?> _start;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="logger">Optional logger.</param>
    public ProcessLauncher(ILogger<ProcessLauncher>? logger = null)
        : this(Process.Start, logger)
    {
    }

    /// <summary>For tests: starts through <paramref name="start"/> instead of <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
    internal ProcessLauncher(Func<ProcessStartInfo, Process?> start, ILogger<ProcessLauncher>? logger = null)
    {
        _start = start;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool Launch(string path, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // An installed app (ADR-009) is started by the shell, which hands back no process, and
        // "shell:AppsFolder" is no directory to start it in.
        var shellApp = AppEntry.IsShellApp(path);
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
            WorkingDirectory = shellApp ? string.Empty : SafeGetDirectoryName(path),
        };

        try
        {
            using var process = _start(startInfo);
            if (process is null && shellApp)
            {
                return true;
            }

            if (process is null)
            {
                _logger?.LogWarning("Process.Start returned null for {Path}.", path);
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _logger?.LogWarning(ex, "Failed to launch {Path}.", path);
            return false;
        }
    }

    private static string SafeGetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
