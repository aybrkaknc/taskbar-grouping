namespace TaskbarFolders.Shared.Models;

/// <summary>Theme preference for the Manager UI and per-group popup.</summary>
public enum ThemePreference
{
    /// <summary>Follow the Windows system theme (default).</summary>
    System = 0,

    /// <summary>Always render the light variant.</summary>
    Light = 1,

    /// <summary>Always render the dark variant.</summary>
    Dark = 2,
}

/// <summary>
/// What the launcher popup is drawn on. The materials are DWM's, on Windows 11 22H2 and later;
/// older Windows show a plain translucent panel for all three.
/// </summary>
public enum PopupBackdrop
{
    /// <summary>Mica, the Manager's own material: a tint taken from the desktop background (default).</summary>
    Mica = 0,

    /// <summary>Mica Alt: Mica with the desktop's colour shown more strongly.</summary>
    MicaAlt = 1,

    /// <summary>Acrylic: a blur of whatever is behind the popup.</summary>
    Acrylic = 2,

    /// <summary>No material: an opaque panel in the theme's colour.</summary>
    Off = 3,
}

/// <summary>
/// A colour laid over the popup's backdrop. The colours themselves, one per theme, are in
/// <c>TaskbarFolders.Core.Themes.PopupTints</c>.
/// </summary>
public enum PopupTint
{
    /// <summary>No colour: the backdrop as it is (default).</summary>
    None = 0,

    /// <summary>The accent colour chosen in Windows' personalisation settings.</summary>
    Accent = 1,

    /// <summary>Deep blue.</summary>
    Midnight = 2,

    /// <summary>Green.</summary>
    Forest = 3,

    /// <summary>Warm orange.</summary>
    Sunset = 4,
}

/// <summary>Where the launcher popup is anchored relative to the taskbar icon.</summary>
public enum PopupPositionPreference
{
    /// <summary>Pick automatically based on the taskbar edge (default).</summary>
    Auto = 0,

    /// <summary>Always render the popup above the taskbar.</summary>
    Above = 1,

    /// <summary>Always render the popup below the taskbar.</summary>
    Below = 2,
}
