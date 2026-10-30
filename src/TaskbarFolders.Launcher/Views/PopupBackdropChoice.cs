using TaskbarFolders.Core.Interop;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Views;

/// <summary>Which material the popup is drawn on, and how DWM is asked for it.</summary>
public static class PopupBackdropChoice
{
    /// <summary>The group's own choice if it made one, otherwise the default in Settings.</summary>
    /// <param name="group">The group's choice, <see cref="GroupConfig.Backdrop"/>.</param>
    /// <param name="settings">The default, <see cref="AppSettings.PopupBackdrop"/>.</param>
    /// <returns>The backdrop to draw.</returns>
    public static PopupBackdrop Resolve(PopupBackdrop? group, PopupBackdrop settings) => group ?? settings;

    /// <summary>
    /// The DWM material for <paramref name="backdrop"/>. <see cref="PopupBackdrop.Off"/> asks for
    /// none, so nothing is composed behind the opaque panel the window paints instead.
    /// </summary>
    /// <param name="backdrop">The resolved choice.</param>
    /// <returns>The value for <see cref="WindowBackdrop.TryApply"/>.</returns>
    public static WindowBackdropKind KindFor(PopupBackdrop backdrop) => backdrop switch
    {
        PopupBackdrop.MicaAlt => WindowBackdropKind.MicaAlt,
        PopupBackdrop.Acrylic => WindowBackdropKind.Acrylic,
        PopupBackdrop.Off => WindowBackdropKind.None,
        _ => WindowBackdropKind.Mica,
    };
}
