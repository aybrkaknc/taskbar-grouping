using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Default <see cref="IGroupSyncService"/>. Pipelines the icon engine and the shortcut writer
/// from a single entry point so view models do not have to know the per-group artifact layout.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GroupSyncService : IGroupSyncService
{
    /// <summary>Pixel size requested when extracting per-app icons for the composite source.</summary>
    public const int CompositeSourceIconSize = 128;

    private readonly IAppDataPathProvider _paths;
    private readonly IIconExtractor _extractor;
    private readonly ICompositeIconGenerator _composer;
    private readonly IIcoFileWriter _icoWriter;
    private readonly IIconCache _cache;
    private readonly IShortcutGenerator _shortcutGenerator;
    private readonly ILauncherPathResolver _launcherResolver;
    private readonly IShellChangeNotifier _shellChangeNotifier;
    private readonly IShortcutReader _shortcutReader;
    private readonly ILogger<GroupSyncService>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public GroupSyncService(
        IAppDataPathProvider paths,
        IIconExtractor extractor,
        ICompositeIconGenerator composer,
        IIcoFileWriter icoWriter,
        IIconCache cache,
        IShortcutGenerator shortcutGenerator,
        ILauncherPathResolver launcherResolver,
        IShellChangeNotifier shellChangeNotifier,
        IShortcutReader shortcutReader,
        ILogger<GroupSyncService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(icoWriter);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(shortcutGenerator);
        ArgumentNullException.ThrowIfNull(launcherResolver);
        ArgumentNullException.ThrowIfNull(shellChangeNotifier);
        ArgumentNullException.ThrowIfNull(shortcutReader);

        _paths = paths;
        _extractor = extractor;
        _composer = composer;
        _icoWriter = icoWriter;
        _cache = cache;
        _shortcutGenerator = shortcutGenerator;
        _launcherResolver = launcherResolver;
        _shellChangeNotifier = shellChangeNotifier;
        _shortcutReader = shortcutReader;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SyncAsync(GroupConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Apps.Count == 0)
        {
            _logger?.LogDebug("Group {GroupId} has no apps; skipping shortcut sync.", config.Id);
            return;
        }

        var launcher = _launcherResolver.TryResolve();
        if (launcher is null)
        {
            // User-blocking — without the launcher path, no .lnk gets written and the
            // "Show shortcut..." flow is silently broken. Surface as Error so support logs
            // light it up; LauncherPathResolver itself already logs the probed paths.
            _logger?.LogError(
                "Launcher binary could not be resolved; per-group shortcut for {GroupId} not regenerated.",
                config.Id);
            return;
        }

        var icons = CollectSourceIcons(config);
        if (icons.Count == 0)
        {
            _logger?.LogWarning(
                "None of the apps in {GroupId} produced an extractable icon; shortcut not regenerated.",
                config.Id);
            return;
        }

        var composite = _composer.GenerateComposite(icons);

        var iconPath = _paths.GetGroupIconFile(config.Id);
        await _icoWriter.WriteAsync(composite, iconPath, cancellationToken).ConfigureAwait(false);

        _shortcutGenerator.Generate(new GroupShortcutRequest(
            GroupId: config.Id,
            DisplayName: config.GroupName,
            TargetExePath: launcher,
            IconPath: iconPath,
            ShortcutPath: _paths.GetGroupShortcutFile(config.Id)));

        // v0.4.1: also write a Start Menu anchor .lnk so Windows.UI.Shell.TaskbarManager
        // RequestPinCurrentAppAsync can persist the pin. The pin API silently fails to
        // anchor a pinned tile when no Start Menu entry with the matching AUMID exists.
        WriteStartMenuShortcut(config, launcher, iconPath);
    }

    /// <inheritdoc/>
    public void RemoveArtifacts(string groupId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        TryDelete(_paths.GetGroupIconFile(groupId));
        TryDelete(_paths.GetGroupShortcutFile(groupId));
        TryDelete(_paths.GetStartMenuShortcutFile(SanitizeForFilename(displayName, fallback: groupId)));

        // The name-derived path above misses an anchor written under an earlier display
        // name, so also sweep by identity.
        RemoveStartMenuAnchorsFor(groupId, keepPath: null);
    }

    /// <inheritdoc/>
    public int RemoveOrphanedStartMenuAnchors()
    {
        // Known ids come from the group file names rather than from the loaded configs: the
        // id is reconstructed from the file name on load anyway, and a group whose JSON fails
        // to parse is skipped by LoadAllAsync but still owns its pinned tile. Deleting its
        // anchor would break that pin over a file the user may yet repair.
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(_paths.GroupsDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(_paths.GroupsDirectory, "*.json"))
                {
                    knownIds.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a reliable list of groups every anchor would look orphaned.
            _logger?.LogWarning(ex, "Could not enumerate group files; Start Menu anchor reconcile skipped.");
            return 0;
        }

        var removed = 0;
        foreach (var anchor in EnumerateStartMenuAnchors())
        {
            var aumid = _shortcutReader.TryReadAumid(anchor);
            if (!GroupAumid.TryExtractGroupId(aumid, out var groupId) || knownIds.Contains(groupId))
            {
                // Not ours, unreadable, or still owned by a group — leave it alone.
                continue;
            }

            if (TryDelete(anchor))
            {
                _logger?.LogInformation(
                    "Removed orphaned Start Menu anchor {Path} for missing group {GroupId}.",
                    anchor, groupId);
                removed++;
            }
        }
        return removed;
    }

    /// <inheritdoc/>
    public bool EnsureStartMenuShortcut(GroupConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var fileName = SanitizeForFilename(config.GroupName, fallback: config.Id);
        var startMenuPath = _paths.GetStartMenuShortcutFile(fileName);
        if (File.Exists(startMenuPath))
        {
            return false;
        }

        var iconPath = _paths.GetGroupIconFile(config.Id);
        if (!File.Exists(iconPath))
        {
            _logger?.LogDebug(
                "EnsureStartMenuShortcut: per-group icon {Path} missing — group needs full Sync first.",
                iconPath);
            return false;
        }

        var launcher = _launcherResolver.TryResolve();
        if (launcher is null)
        {
            _logger?.LogWarning(
                "EnsureStartMenuShortcut: launcher binary unresolved; Start Menu anchor for {GroupId} not written.",
                config.Id);
            return false;
        }

        WriteStartMenuShortcut(config, launcher, iconPath);
        return true;
    }

    private void WriteStartMenuShortcut(GroupConfig config, string launcher, string iconPath)
    {
        var fileName = SanitizeForFilename(config.GroupName, fallback: config.Id);
        var startMenuPath = _paths.GetStartMenuShortcutFile(fileName);

        try
        {
            _shortcutGenerator.Generate(new GroupShortcutRequest(
                GroupId: config.Id,
                DisplayName: config.GroupName,
                TargetExePath: launcher,
                IconPath: iconPath,
                ShortcutPath: startMenuPath));

            // v0.4.2: tell the Shell about the new file synchronously (SHCNF_FLUSH) so
            // TaskbarManager.RequestPinCurrentAppAsync sees it on the next call instead of
            // racing the AppsFolder background indexer. Notify-failures inside the wrapper
            // are swallowed there; this try/catch covers the surrounding sync flow only.
            _shellChangeNotifier.NotifyCreate(startMenuPath);

            // The file name follows the display name while the AUMID follows the id, so a
            // rename writes a second anchor carrying the same identity. Drop the old one
            // only after the new one is in place: until then it is what keeps a pin working.
            RemoveStartMenuAnchorsFor(config.Id, keepPath: startMenuPath);
        }
        catch (Exception ex)
        {
            // Start Menu write failure must NOT break the main sync flow — the per-group
            // .lnk under shortcuts/ is already written and is the primary artifact. Pin
            // attempts will fail until the Start Menu entry is restored, but Show shortcut
            // and .lnk-pinned tile flows keep working. Catches everything including
            // COMException from the IShellLinkW interop chain (CLSID_ShellLink failures,
            // IPersistFile save failures, IPropertyStore commit failures).
            _logger?.LogWarning(ex, "Failed to write Start Menu shortcut for {GroupId}.", config.Id);
        }
    }

    /// <summary>
    /// Sanitises a free-text group display name into a safe Windows filename stem.
    /// Replaces invalid characters with '-', strips control chars + trailing dots/whitespace,
    /// clamps to 60 chars, falls back to <paramref name="fallback"/> when the sanitised
    /// string is empty.
    /// </summary>
    internal static string SanitizeForFilename(string displayName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(displayName.Length);
        foreach (var ch in displayName)
        {
            sb.Append(invalid.Contains(ch) || char.IsControl(ch) ? '-' : ch);
        }

        var sanitised = sb.ToString().Trim().TrimEnd('.');
        if (sanitised.Length == 0)
        {
            return fallback;
        }
        if (sanitised.Length > 60)
        {
            sanitised = sanitised[..60].TrimEnd('.', ' ');
        }
        // Defensive: a 60-char-plus input of only dots+spaces becomes empty after the
        // post-clamp trim. Fall back to id so callers never see an empty filename stem.
        return sanitised.Length == 0 ? fallback : sanitised;
    }

    private List<BitmapSource> CollectSourceIcons(GroupConfig config)
    {
        var icons = new List<BitmapSource>(Math.Min(config.Apps.Count, CompositeIconGenerator.MaxTiles));
        foreach (var app in config.Apps.Take(CompositeIconGenerator.MaxTiles))
        {
            var icon = LoadIcon(app.Path);
            if (icon is not null)
            {
                icons.Add(icon);
            }
        }
        return icons;
    }

    private BitmapSource? LoadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_cache.TryGet(path, CompositeSourceIconSize, out var cached))
        {
            return cached;
        }

        var icon = _extractor.ExtractIcon(path, CompositeSourceIconSize);
        if (icon is not null)
        {
            _cache.Set(path, CompositeSourceIconSize, icon);
        }
        return icon;
    }

    /// <summary>
    /// Deletes every Start Menu anchor stamped with <paramref name="groupId"/>'s AUMID except
    /// <paramref name="keepPath"/>. Matching by AUMID rather than by file name is what finds
    /// an anchor left under a display name the group no longer has.
    /// </summary>
    private void RemoveStartMenuAnchorsFor(string groupId, string? keepPath)
    {
        var aumid = GroupAumid.For(groupId);
        var keep = keepPath is null ? null : Path.GetFullPath(keepPath);

        foreach (var anchor in EnumerateStartMenuAnchors())
        {
            if (keep is not null && string.Equals(Path.GetFullPath(anchor), keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(_shortcutReader.TryReadAumid(anchor), aumid, StringComparison.OrdinalIgnoreCase)
                && TryDelete(anchor))
            {
                _logger?.LogInformation("Removed stale Start Menu anchor {Path} for {GroupId}.", anchor, groupId);
            }
        }
    }

    private List<string> EnumerateStartMenuAnchors()
    {
        // Materialised so callers can delete while iterating.
        try
        {
            return Directory.Exists(_paths.StartMenuDirectory)
                ? Directory.EnumerateFiles(_paths.StartMenuDirectory, "*.lnk").ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not enumerate {Directory}.", _paths.StartMenuDirectory);
            return [];
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger?.LogWarning(ex, "Could not delete {Path}.", path);
        }
        return false;
    }
}
