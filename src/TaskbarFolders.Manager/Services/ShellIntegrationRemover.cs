using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Manager.Services;

/// <summary>Default <see cref="IShellIntegrationRemover"/>.</summary>
/// <remarks>
/// Every step runs even when an earlier one fails: half a cleanup is still better than none
/// for someone about to delete the folder. Settings go first, so that whatever else happens
/// the next Manager start does not re-create the menu or the auto-start entry.
/// </remarks>
public sealed class ShellIntegrationRemover : IShellIntegrationRemover
{
    private readonly IAppSettingsStore _settings;
    private readonly IAutoStartService _autoStart;
    private readonly ExplorerContextMenuService _explorerMenu;
    private readonly IGroupConfigStore _groups;
    private readonly IGroupSyncService _sync;
    private readonly IAppDataPathProvider _paths;
    private readonly ILogger<ShellIntegrationRemover>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public ShellIntegrationRemover(
        IAppSettingsStore settings,
        IAutoStartService autoStart,
        ExplorerContextMenuService explorerMenu,
        IGroupConfigStore groups,
        IGroupSyncService sync,
        IAppDataPathProvider paths,
        ILogger<ShellIntegrationRemover>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(explorerMenu);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(paths);

        _settings = settings;
        _autoStart = autoStart;
        _explorerMenu = explorerMenu;
        _groups = groups;
        _sync = sync;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var ok = true;

        try
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            settings.ShowInExplorerMenu = false;
            settings.AutoStart = false;
            await _settings.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ok = false;
            _logger?.LogWarning(ex, "Remove from this PC: settings could not be updated.");
        }

        ok &= Try("auto-start entry", _autoStart.Disable);
        ok &= Try("Explorer menu", () => _explorerMenu.Write([], launcherPath: null));

        try
        {
            var groups = await _groups.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            foreach (var group in groups)
            {
                ok &= Try($"artifacts of {group.Id}", () => _sync.RemoveArtifacts(group.Id, group.GroupName));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ok = false;
            _logger?.LogWarning(ex, "Remove from this PC: groups could not be read.");
        }

        Try("empty Start menu folder", RemoveStartMenuFolderIfEmpty);

        _logger?.LogInformation("Remove from this PC finished; complete={Complete}.", ok);
        return ok;
    }

    private void RemoveStartMenuFolderIfEmpty()
    {
        var folder = _paths.StartMenuDirectory;
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }

    private bool Try(string what, Action step)
    {
        try
        {
            step();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger?.LogWarning(ex, "Remove from this PC: {Step} could not be removed.", what);
            return false;
        }
    }
}
