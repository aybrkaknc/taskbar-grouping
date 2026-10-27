using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Shared.Configuration;

/// <summary>
/// JSON-file-backed <see cref="IGroupConfigStore"/> writing to
/// <see cref="IAppDataPathProvider.GroupsDirectory"/>.
/// </summary>
/// <remarks>
/// Writes are atomic: serialise into <c>{id}.json.tmp</c>, then <see cref="File.Move(string,string,bool)"/>
/// over the target. A crash mid-write leaves either the previous file intact or a leftover <c>.tmp</c> file
/// that LoadAll ignores (only files matching <c>*.json</c> are picked up).
/// </remarks>
public sealed class JsonGroupConfigStore : IGroupConfigStore
{
    private readonly IAppDataPathProvider _paths;
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance bound to the supplied <see cref="IAppDataPathProvider"/>.
    /// </summary>
    /// <param name="paths">Path provider for locating the groups directory.</param>
    public JsonGroupConfigStore(IAppDataPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _paths = paths;
        _options = JsonOptions.Default;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GroupConfig>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_paths.GroupsDirectory))
        {
            return Array.Empty<GroupConfig>();
        }

        var results = new List<GroupConfig>();
        foreach (var file in Directory.EnumerateFiles(_paths.GroupsDirectory, "*.json"))
        {
            GroupConfig? config;
            try
            {
                config = await LoadFromFileAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // One damaged or locked file must not take every other group with it: the
                // Manager, and the uninstaller with it, used to crash on start. The file stays
                // on disk for the user to repair; its id still counts as a known group in the
                // Start menu sweep, which reads file names, not contents.
                continue;
            }

            if (config is not null)
            {
                results.Add(config);
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public Task<GroupConfig?> LoadAsync(string groupId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var file = _paths.GetGroupFile(groupId);
        return File.Exists(file)
            ? LoadFromFileAsync(file, cancellationToken)
            : Task.FromResult<GroupConfig?>(null);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(GroupConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Id);

        Directory.CreateDirectory(_paths.GroupsDirectory);

        var target = _paths.GetGroupFile(config.Id);
        var temp = target + ".tmp";

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, config, _options, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temp, target, overwrite: true);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string groupId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var file = _paths.GetGroupFile(groupId);
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    private async Task<GroupConfig?> LoadFromFileAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        var config = await JsonSerializer.DeserializeAsync<GroupConfig>(stream, _options, cancellationToken)
            .ConfigureAwait(false);

        if (config is not null)
        {
            // The file name (sans extension) is the canonical group id — Save writes to
            // GetGroupFile(config.Id) so disk layout is the source of truth. Override
            // whatever id appears in the JSON so that:
            //   - JSON without "id" works (GroupConfig.Id defaults to a fresh Guid we never want to surface)
            //   - A user-renamed file keeps its new identity instead of resurrecting the stale id
            config.Id = Path.GetFileNameWithoutExtension(file);
        }

        return config;
    }
}
