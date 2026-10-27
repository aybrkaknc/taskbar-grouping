using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TaskbarFolders.Manager.Services;

/// <summary>Default <see cref="IInstanceCloser"/>, by process name.</summary>
public sealed class ProcessInstanceCloser : IInstanceCloser
{
    private static readonly string[] _processNames = ["TaskbarFolders.Manager", "TaskbarFolders.Launcher"];

    private readonly ILogger<ProcessInstanceCloser>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public ProcessInstanceCloser(ILogger<ProcessInstanceCloser>? logger = null) => _logger = logger;

    /// <inheritdoc/>
    public int CloseOthers()
    {
        var self = Environment.ProcessId;
        using var current = Process.GetCurrentProcess();
        var session = current.SessionId;
        var left = 0;
        foreach (var name in _processNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    // Other users' copies are theirs: an uninstall runs per user.
                    if (process.Id == self || process.SessionId != session)
                    {
                        continue;
                    }

                    try
                    {
                        // A popup or a Manager holds nothing that needs saving at this point:
                        // the uninstall is about to delete the data anyway.
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(3000))
                        {
                            left++;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Exited between the listing and the kill.
                    }
                    catch (Win32Exception ex)
                    {
                        _logger?.LogWarning(ex, "Uninstall: could not end {Name} ({Pid}).", name, process.Id);
                        left++;
                    }
                }
            }
        }

        return left;
    }
}
