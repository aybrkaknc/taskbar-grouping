using System;
using System.Windows;

namespace TaskbarFolders.Launcher.Views;

/// <summary>A press outside the popup, as reported by <see cref="PopupWindow.OutsidePress"/>.</summary>
/// <param name="position">Press position in device pixels.</param>
public sealed class OutsidePressEventArgs(Point position) : EventArgs
{
    /// <summary>Press position in device pixels.</summary>
    public Point Position { get; } = position;

    /// <summary>
    /// Set by a handler when the press is on the taskbar tile that opened the popup; the popup
    /// then closes even while pinned.
    /// </summary>
    public bool IsOnOpeningTile { get; set; }
}
