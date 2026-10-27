using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Adds files named on the command line to the launcher's group: the path taken by Explorer's
/// "Taskbar Groups" menu and by a file dropped on the group's taskbar tile.
/// </summary>
/// <remarks>
/// Explorer starts one launcher per selected file, so a multi-selection arrives as up to
/// fifteen processes at once, all editing the same <c>groups/&lt;id&gt;.json</c>. Each one
/// loads, adds and saves inside a per-group, machine-wide lock, so none overwrites another's
/// addition. A named <see cref="Semaphore"/> rather than a <see cref="Mutex"/>: a mutex must be
/// released by the thread that took it, and the work in between awaits.
/// </remarks>
public sealed class CommandLineAppAdder
{
    /// <summary>How long to wait for another launcher editing the same group.</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private readonly IGroupConfigStore _store;
    private readonly IGroupSyncService _syncService;
    private readonly LauncherOptions _options;
    private readonly ILogger<CommandLineAppAdder>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public CommandLineAppAdder(
        IGroupConfigStore store,
        IGroupSyncService syncService,
        LauncherOptions options,
        ILogger<CommandLineAppAdder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _syncService = syncService;
        _options = options;
        _logger = logger;
    }

    /// <summary>Name of the per-group lock shared by every launcher process.</summary>
    public static string LockName(string groupId) => $"Local\\TaskbarFolders.Group.{groupId}.Edit";

    /// <summary>
    /// Adds every file <see cref="AppEntry.TryCreateFromPath"/> accepts and the group does not
    /// already contain, then saves and re-syncs the group. Returns how many were added; 0 for a
    /// group that does not exist.
    /// </summary>
    /// <param name="paths">Files from the command line.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> AddAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return 0;
        }

        using var gate = new Semaphore(1, 1, LockName(_options.GroupId));
        var entered = gate.WaitOne(LockTimeout);
        if (!entered)
        {
            // A launcher that died holding the lock must not block every later one forever.
            _logger?.LogWarning("Timed out waiting for group {GroupId}'s edit lock; adding anyway.", _options.GroupId);
        }

        try
        {
            var config = await _store.LoadAsync(_options.GroupId, cancellationToken).ConfigureAwait(false);
            if (config is null)
            {
                _logger?.LogWarning("Cannot add files to unknown group {GroupId}.", _options.GroupId);
                return 0;
            }

            var added = 0;
            foreach (var path in paths)
            {
                if (!AppEntry.TryCreateFromPath(path, out var entry))
                {
                    _logger?.LogInformation("Ignored {Path}: not an .exe or .lnk.", path);
                    continue;
                }

                if (config.Apps.Any(a => string.Equals(a.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger?.LogInformation("{Path} is already in group {GroupId}.", path, _options.GroupId);
                    continue;
                }

                config.Apps.Add(entry);
                added++;
            }

            if (added > 0)
            {
                await _store.SaveAsync(config, cancellationToken).ConfigureAwait(false);
                await _syncService.SyncAsync(config, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Added {Count} app(s) to group {GroupId} from the command line.", added, _options.GroupId);
            }

            return added;
        }
        finally
        {
            if (entered)
            {
                gate.Release();
            }
        }
    }
}
