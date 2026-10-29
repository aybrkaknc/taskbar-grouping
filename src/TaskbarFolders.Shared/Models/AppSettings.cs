using System.Text.Json.Serialization;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Shared.Models;

/// <summary>
/// Global application settings for TaskbarFolders.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Whether to start the Manager application on Windows startup.
    /// </summary>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    /// <summary>
    /// Global theme setting.
    /// </summary>
    [JsonPropertyName("theme")]
    [JsonConverter(typeof(CamelCaseEnumConverter<ThemePreference>))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// Whether to enable popup animations.
    /// </summary>
    [JsonPropertyName("enableAnimations")]
    public bool EnableAnimations { get; set; } = true;

    /// <summary>
    /// Popup position preference.
    /// </summary>
    [JsonPropertyName("popupPosition")]
    [JsonConverter(typeof(CamelCaseEnumConverter<PopupPositionPreference>))]
    public PopupPositionPreference PopupPosition { get; set; } = PopupPositionPreference.Auto;

    /// <summary>
    /// Whether Explorer's classic context menu for <c>.exe</c> and <c>.lnk</c> files offers
    /// "Taskbar Groups", with one entry per group that adds the file to it.
    /// </summary>
    [JsonPropertyName("showInExplorerMenu")]
    public bool ShowInExplorerMenu { get; set; } = true;

    /// <summary>
    /// Whether the user has been told about taskbar tiles left behind by groups that no longer
    /// exist. Shown once, after a group is deleted; not part of the settings dialog.
    /// </summary>
    [JsonPropertyName("orphanedTilesNoticeShown")]
    public bool OrphanedTilesNoticeShown { get; set; }
}
