using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Manager.Services;

/// <summary>Default <see cref="IUninstaller"/>.</summary>
/// <remarks>
/// Order matters. Other processes end first so nothing re-creates what is being removed and
/// no file is locked; tiles are unpinned while their shortcuts still resolve; the user's data
/// goes last, because every log line written after it would bring the logs folder back. What
/// this process still writes after that — its last log lines — is removed once the process has
/// exited, by the installer's <c>[UninstallDelete]</c> or, for a portable copy, by the helper
/// script started at the end (<see cref="CompatibilityRecordSweep"/>).
/// </remarks>
public sealed class Uninstaller : IUninstaller
{
    /// <summary>
    /// Keys under <c>HKEY_CURRENT_USER</c> where Windows records programs it has seen run, by
    /// executable path or AppUserModelID. Nothing else removes them when a program goes.
    /// </summary>
    public static readonly IReadOnlyList<string> WindowsRecordKeys =
    [
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppLaunch",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\ShowJumpView",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppBadgeUpdated",
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
    ];

    private readonly IInstanceCloser _instances;
    private readonly IShortcutReader _shortcutReader;
    private readonly ITaskbarUnpinner _unpinner;
    private readonly IAppSettingsStore _settings;
    private readonly IAutoStartService _autoStart;
    private readonly ExplorerContextMenuService _explorerMenu;
    private readonly IGroupConfigStore _groups;
    private readonly IGroupSyncService _sync;
    private readonly IRegistryWriter _registry;
    private readonly IAppDataPathProvider _paths;
    private readonly IProcessRunner _processes;
    private readonly InstallLocation _install;
    private readonly ILogger<Uninstaller>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public Uninstaller(
        IInstanceCloser instances,
        IShortcutReader shortcutReader,
        ITaskbarUnpinner unpinner,
        IAppSettingsStore settings,
        IAutoStartService autoStart,
        ExplorerContextMenuService explorerMenu,
        IGroupConfigStore groups,
        IGroupSyncService sync,
        IRegistryWriter registry,
        IAppDataPathProvider paths,
        IProcessRunner processes,
        InstallLocation install,
        ILogger<Uninstaller>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(shortcutReader);
        ArgumentNullException.ThrowIfNull(unpinner);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(explorerMenu);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(install);

        _instances = instances;
        _shortcutReader = shortcutReader;
        _unpinner = unpinner;
        _settings = settings;
        _autoStart = autoStart;
        _explorerMenu = explorerMenu;
        _groups = groups;
        _sync = sync;
        _registry = registry;
        _paths = paths;
        _processes = processes;
        _install = install;
        _logger = logger;
    }

    /// <summary>
    /// Whether a value name in one of <see cref="WindowsRecordKeys"/> is about this program:
    /// a path inside <paramref name="installRoot"/> (MuiCache appends ".FriendlyAppName" and
    /// similar to it) or a group's AppUserModelID.
    /// </summary>
    /// <param name="valueName">Registry value name.</param>
    /// <param name="installRoot">Folder holding the <c>Manager</c> and <c>Launcher</c> folders.</param>
    public static bool IsOurRecord(string valueName, string installRoot)
    {
        ArgumentNullException.ThrowIfNull(valueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var root = Path.TrimEndingDirectorySeparator(installRoot) + Path.DirectorySeparatorChar;
        return valueName.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || valueName.StartsWith(GroupAumid.Prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<UninstallReport> RunAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Uninstall started for {Root}.", _install.Root);
        var ok = _instances.CloseOthers() == 0;

        var tilesLeft = UnpinGroupTiles();
        ok &= tilesLeft == 0;

        try
        {
            // If the data folder cannot be deleted below, at least the settings must not bring
            // the menu or the auto-start entry back.
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            settings.ShowInExplorerMenu = false;
            settings.AutoStart = false;
            await _settings.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ok = false;
            _logger?.LogWarning(ex, "Uninstall: settings could not be updated.");
        }

        ok &= Try("auto-start entry", _autoStart.Disable);
        ok &= Try("Explorer menu", () => _explorerMenu.Write([], launcherPath: null));

        try
        {
            foreach (var group in await _groups.LoadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                ok &= Try($"artifacts of {group.Id}", () => _sync.RemoveArtifacts(group.Id, group.GroupName));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ok = false;
            _logger?.LogWarning(ex, "Uninstall: groups could not be read.");
        }

        ok &= Try("Windows' records of the program", RemoveWindowsRecords);
        StartSweepAfterExit();
        _logger?.LogInformation("Uninstall: deleting the user data in {Path}; complete so far={Complete}.", _paths.AppDataRoot, ok);

        // No logging from here on: a log line would re-create the folder being deleted.
        ok &= TryQuiet(() => DeleteDirectory(_paths.AppDataRoot));

        // With the group files gone every anchor is an orphan, including those of groups whose
        // config could not be parsed above.
        ok &= TryQuiet(() => _sync.RemoveOrphanedStartMenuAnchors());
        ok &= TryQuiet(() => DeleteIfEmpty(_paths.StartMenuDirectory));

        return new UninstallReport(ok, tilesLeft);
    }

    private int UnpinGroupTiles()
    {
        var left = 0;
        try
        {
            if (!Directory.Exists(_paths.PinnedTaskbarDirectory))
            {
                return 0;
            }

            foreach (var pinned in Directory.EnumerateFiles(_paths.PinnedTaskbarDirectory, "*.lnk").ToList())
            {
                if (GroupAumid.TryExtractGroupId(_shortcutReader.TryReadAumid(pinned), out _)
                    && !_unpinner.TryUnpin(pinned))
                {
                    left++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Uninstall: pinned tiles could not be listed.");
            left++;
        }

        return left;
    }

    /// <summary>
    /// Windows records this process and the installer's uninstaller as they exit, after the
    /// cleanup above. A detached <c>cmd</c> waits for both and deletes those records. An
    /// installed copy has an <c>unins000.exe</c> to wait for and its files are Inno's to delete;
    /// a portable copy has nobody else, so the same script also deletes its folders. A
    /// development build gets nothing. A failure is logged and does not make the uninstall
    /// incomplete: nothing the user could act on is left, only registry values they never see.
    /// </summary>
    private void StartSweepAfterExit()
    {
        try
        {
            string content;
            if (_install.FindUninstaller() is not null)
            {
                content = CompatibilityRecordSweep.BuildScript(_install.Root);
            }
            else if (_install.IsPortable)
            {
                content = CompatibilityRecordSweep.BuildPortableScript(_install.Root, _paths.AppDataRoot);
            }
            else
            {
                return;
            }

            var script = Path.Combine(Path.GetTempPath(), "TaskbarFolders-sweep-" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(script, content);
            var started = _processes.TryStart(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
            });
            if (!started)
            {
                _logger?.LogWarning("Uninstall: the helper that removes Windows' exit records could not be started.");
                File.Delete(script);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger?.LogWarning(ex, "Uninstall: the helper that removes Windows' exit records could not be set up.");
        }
    }

    private void RemoveWindowsRecords()
    {
        foreach (var key in WindowsRecordKeys)
        {
            foreach (var name in _registry.GetValueNames(key).Where(n => IsOurRecord(n, _install.Root)))
            {
                _registry.DeleteValue(key, name);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
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
            _logger?.LogWarning(ex, "Uninstall: {Step} could not be removed.", what);
            return false;
        }
    }

    private static bool TryQuiet(Action step)
    {
        try
        {
            step();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}
