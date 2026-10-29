using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Keeps Explorer's classic context menu for <c>.exe</c> and <c>.lnk</c> files in step with the
/// groups: a "Taskbar Groups" submenu with one entry per group, each starting the launcher with
/// <c>--group-id &lt;id&gt; "%1"</c>, which adds the file and opens the popup.
/// </summary>
/// <remarks>
/// Static verbs under <c>HKCU\Software\Classes\{exefile,lnkfile}\shell</c>: per user, no
/// elevation, no signing. Windows 11 lists them under "Show more options" — its compact menu
/// only takes packaged <c>IExplorerCommand</c> handlers (ADR-007). The whole submenu is deleted
/// and rewritten on every refresh, so renamed and deleted groups never linger.
/// </remarks>
public sealed class ExplorerContextMenuService : IDisposable
{
    /// <summary>File classes that get the submenu.</summary>
    public static readonly IReadOnlyList<string> FileClasses = ["exefile", "lnkfile"];

    /// <summary>Key name of the submenu under each class's <c>shell</c> key.</summary>
    public const string VerbKeyName = "TaskbarFolders.Groups";

    /// <summary>Caption of the submenu.</summary>
    public const string MenuCaption = "Taskbar Groups";

    private readonly IRegistryWriter _registry;
    private readonly IGroupConfigStore _groups;
    private readonly IAppSettingsStore _settings;
    private readonly ILauncherPathResolver _launcher;
    private readonly ILogger<ExplorerContextMenuService>? _logger;

    // One refresh at a time, reading and writing together: see RefreshAsync.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>Initializes a new instance.</summary>
    public ExplorerContextMenuService(
        IRegistryWriter registry,
        IGroupConfigStore groups,
        IAppSettingsStore settings,
        ILauncherPathResolver launcher,
        ILogger<ExplorerContextMenuService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(launcher);

        _registry = registry;
        _groups = groups;
        _settings = settings;
        _launcher = launcher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Dispose() => _refreshGate.Dispose();

    /// <summary>Path of the submenu key for one file class, under <c>HKEY_CURRENT_USER</c>.</summary>
    /// <param name="fileClass">e.g. <c>lnkfile</c>.</param>
    public static string MenuKey(string fileClass) => $@"Software\Classes\{fileClass}\shell\{VerbKeyName}";

    /// <summary>
    /// Rewrites the submenu from the current groups and settings, or removes it when the setting
    /// is off, there are no groups, or the launcher cannot be found. Failures are logged, never
    /// thrown: the menu is a convenience and must not break the Manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Callers do not wait for each other: deleting a group raises the change twice, and each
        // handler starts a refresh. Two at once deleted the same registry tree from both sides,
        // and the one that wrote last won even if it had read the older list. The read and the
        // write are therefore one step, so a later refresh always reads after an earlier wrote.
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            var groups = settings.ShowInExplorerMenu
                ? await _groups.LoadAllAsync(cancellationToken).ConfigureAwait(false)
                : [];
            var launcher = groups.Count > 0 ? _launcher.TryResolve() : null;

            Write(groups, launcher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger?.LogWarning(ex, "Could not update Explorer's Taskbar Groups menu.");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Writes the submenu for <paramref name="groups"/>; removes it when the list is empty or
    /// <paramref name="launcherPath"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="groups">Groups to list, in any order; shown alphabetically.</param>
    /// <param name="launcherPath">Launcher executable the entries start.</param>
    public void Write(IReadOnlyList<GroupConfig> groups, string? launcherPath)
    {
        ArgumentNullException.ThrowIfNull(groups);

        foreach (var fileClass in FileClasses)
        {
            _registry.DeleteTree(MenuKey(fileClass));
        }

        if (groups.Count == 0 || string.IsNullOrEmpty(launcherPath))
        {
            _logger?.LogInformation("Explorer's Taskbar Groups menu removed.");
            return;
        }

        var ordered = groups.OrderBy(g => g.GroupName, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var fileClass in FileClasses)
        {
            var root = MenuKey(fileClass);
            _registry.SetString(root, "MUIVerb", MenuCaption);
            _registry.SetString(root, "SubCommands", string.Empty);
            _registry.SetString(root, "Icon", $"{launcherPath},0");

            for (var i = 0; i < ordered.Count; i++)
            {
                var group = ordered[i];

                // Subkeys are listed in name order, so a number prefix keeps the menu
                // alphabetical by display name rather than by id.
                var entry = $@"{root}\shell\{i + 1:D2}-{group.Id}";

                // A single "&" marks a menu accelerator and would vanish from the caption.
                _registry.SetString(entry, "MUIVerb", group.GroupName.Replace("&", "&&", StringComparison.Ordinal));
                _registry.SetString($@"{entry}\command", null, $"\"{launcherPath}\" --group-id \"{group.Id}\" \"%1\"");
            }
        }

        _logger?.LogInformation("Explorer's Taskbar Groups menu lists {Count} group(s).", ordered.Count);
    }
}
