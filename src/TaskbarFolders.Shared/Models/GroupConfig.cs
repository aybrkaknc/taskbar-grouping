using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Shared.Models;

/// <summary>
/// Configuration for a single taskbar group.
/// </summary>
public sealed class GroupConfig
{
    /// <summary>Minimum allowed value for <see cref="Columns"/>.</summary>
    public const int MinColumns = 1;

    /// <summary>Maximum allowed value for <see cref="Columns"/>.</summary>
    public const int MaxColumns = 6;

    private int _columns = 3;

    /// <summary>
    /// Unique identifier for the group.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Display name of the group.
    /// </summary>
    [JsonPropertyName("groupName")]
    public required string GroupName { get; set; }

    /// <summary>
    /// Number of columns in the popup grid layout. Must be in [<see cref="MinColumns"/>..<see cref="MaxColumns"/>].
    /// Clamped on assignment so deserialised configs with out-of-range values cannot crash the popup layout.
    /// </summary>
    [JsonPropertyName("columns")]
    public int Columns
    {
        get => _columns;
        set => _columns = Math.Clamp(value, MinColumns, MaxColumns);
    }

    /// <summary>
    /// Applications contained in this group.
    /// </summary>
    [JsonPropertyName("apps")]
    public List<AppEntry> Apps { get; set; } = [];
}
