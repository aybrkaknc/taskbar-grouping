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

    /// <summary>Icon size, in DIPs, of a group that never chose one.</summary>
    public const int DefaultIconSize = 48;

    private int _columns = 3;
    private int _iconSize = DefaultIconSize;
    private int _tileSpacing;

    /// <summary>The icon sizes, in DIPs, a group can choose from, smallest first.</summary>
    public static IReadOnlyList<int> IconSizes { get; } = [32, 48, 64, 80];

    /// <summary>The gaps between popup tiles, in DIPs, a group can choose from, smallest first.</summary>
    public static IReadOnlyList<int> TileSpacings { get; } = [0, 4, 8, 12];

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
    /// Size, in DIPs, of the app icons in the popup: one of <see cref="IconSizes"/>. Any other
    /// value is snapped to the nearest of them on assignment (the smaller one when halfway), so
    /// a hand-edited file cannot produce a size the popup was not laid out for.
    /// </summary>
    [JsonPropertyName("iconSize")]
    public int IconSize
    {
        get => _iconSize;
        set => _iconSize = Snap(value, IconSizes);
    }

    /// <summary>
    /// Gap, in DIPs, between neighbouring tiles in the popup: one of <see cref="TileSpacings"/>,
    /// snapped like <see cref="IconSize"/>. 0 — the tiles touching — is what every group had
    /// before the setting existed, and what a file without it loads as.
    /// </summary>
    [JsonPropertyName("tileSpacing")]
    public int TileSpacing
    {
        get => _tileSpacing;
        set => _tileSpacing = Snap(value, TileSpacings);
    }

    /// <summary>
    /// Whether the popup shows each app's name under its icon. Without names the tiles shrink
    /// to the icon; the name is still the tile's tooltip.
    /// </summary>
    [JsonPropertyName("showNames")]
    public bool ShowNames { get; set; } = true;

    /// <summary>
    /// What this group's popup is drawn on, or <see langword="null"/> — every group before the
    /// setting existed — to follow <see cref="AppSettings.PopupBackdrop"/>.
    /// </summary>
    [JsonPropertyName("backdrop")]
    [JsonConverter(typeof(CamelCaseEnumConverter<PopupBackdrop>))]
    public PopupBackdrop? Backdrop { get; set; }

    /// <summary>
    /// The colour laid over this group's popup, or <see langword="null"/> to follow
    /// <see cref="AppSettings.PopupTint"/>. <see cref="PopupTint.None"/> is a choice of its own:
    /// no colour, whatever Settings say.
    /// </summary>
    [JsonPropertyName("tint")]
    [JsonConverter(typeof(CamelCaseEnumConverter<PopupTint>))]
    public PopupTint? Tint { get; set; }

    /// <summary>
    /// Applications contained in this group.
    /// </summary>
    [JsonPropertyName("apps")]
    public List<AppEntry> Apps { get; set; } = [];

    // The nearest of the steps; the smaller one when halfway, since the steps are ascending.
    private static int Snap(int value, IReadOnlyList<int> steps)
    {
        var nearest = steps[0];
        foreach (var size in steps)
        {
            if (Math.Abs(size - value) < Math.Abs(nearest - value))
            {
                nearest = size;
            }
        }

        return nearest;
    }
}
