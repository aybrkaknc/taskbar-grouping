using System;
using System.Windows.Media;
using Microsoft.Win32;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Core.Themes;

/// <summary>
/// The two shades of the Windows accent colour a popup tint uses: Windows' <c>Dark2</c> for the
/// dark theme and <c>Light2</c> for the light one, which it made for exactly this — a surface that
/// text in that theme stays readable on.
/// </summary>
/// <param name="Dark">The shade for the dark theme.</param>
/// <param name="Light">The shade for the light theme.</param>
public readonly record struct AccentShades(Color Dark, Color Light)
{
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    /// <summary>
    /// Reads the shades from Windows' <c>AccentPalette</c> value: eight RGBA entries, lightest
    /// first, of which entry 1 is <c>Light2</c> and entry 5 <c>Dark2</c>.
    /// </summary>
    /// <param name="palette">The value's bytes, or <see langword="null"/>.</param>
    /// <returns>The shades, or <see cref="PopupTints.DefaultAccent"/> for a missing or short value.</returns>
    public static AccentShades FromPalette(byte[]? palette)
    {
        if (palette is null || palette.Length < 32)
        {
            return PopupTints.DefaultAccent;
        }

        return new AccentShades(
            Color.FromRgb(palette[20], palette[21], palette[22]),
            Color.FromRgb(palette[4], palette[5], palette[6]));
    }

    /// <summary>The user's accent shades, read now; Windows' blue when they cannot be read.</summary>
    /// <returns>The shades.</returns>
    public static AccentShades Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKey);
            return FromPalette(key?.GetValue("AccentPalette") as byte[]);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return PopupTints.DefaultAccent;
        }
    }
}

/// <summary>
/// The colours of <see cref="PopupTint"/>, one per theme. Each is translucent, so the backdrop
/// still shows, and dark or pale enough for the theme's text: <c>PopupTintsTests</c> measures
/// the contrast over Mica's base colour. Change a colour only with that test green.
/// </summary>
public static class PopupTints
{
    // How much of a tint covers the backdrop: less in dark, where colour reads strongly.
    private const byte DarkAlpha = 0x59;
    private const byte LightAlpha = 0x80;

    /// <summary>Windows' default blue, used when the user's accent cannot be read.</summary>
    public static AccentShades DefaultAccent { get; } = new(Color.FromRgb(0x00, 0x3E, 0x92), Color.FromRgb(0x99, 0xEB, 0xFF));

    /// <summary>
    /// The popup's opaque panel with the backdrop off: Mica's own base colour. The Launcher's
    /// themes carry it as <c>PopupSolidBackground</c>; <c>PopupTintsTests</c> keeps the two equal.
    /// </summary>
    /// <param name="dark">Whether the popup wears the dark theme.</param>
    /// <returns>An opaque colour.</returns>
    public static Color SolidPanel(bool dark) => dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);

    /// <summary>The colour to lay over a popup, or <see langword="null"/> for <see cref="PopupTint.None"/>.</summary>
    /// <param name="tint">The tint.</param>
    /// <param name="dark">Whether the popup wears the dark theme.</param>
    /// <param name="accent">The user's accent shades, for <see cref="PopupTint.Accent"/>.</param>
    /// <returns>A translucent colour, or <see langword="null"/>.</returns>
    public static Color? ColorFor(PopupTint tint, bool dark, AccentShades accent)
    {
        Color? rgb = tint switch
        {
            PopupTint.Accent => dark ? accent.Dark : accent.Light,
            PopupTint.Midnight => dark ? Color.FromRgb(0x1E, 0x40, 0xAF) : Color.FromRgb(0xBF, 0xDB, 0xFE),
            PopupTint.Forest => dark ? Color.FromRgb(0x14, 0x53, 0x2D) : Color.FromRgb(0xBB, 0xF7, 0xD0),
            PopupTint.Sunset => dark ? Color.FromRgb(0x9A, 0x34, 0x12) : Color.FromRgb(0xFE, 0xD7, 0xAA),
            _ => null,
        };

        return rgb is { } c ? Color.FromArgb(dark ? DarkAlpha : LightAlpha, c.R, c.G, c.B) : null;
    }

    /// <summary>
    /// <paramref name="top"/> painted over <paramref name="bottom"/> (source-over). Over a clear
    /// base that is <paramref name="top"/> itself; over an opaque one, an opaque colour.
    /// </summary>
    /// <param name="top">The colour in front.</param>
    /// <param name="bottom">The colour behind.</param>
    /// <returns>The colour seen.</returns>
    public static Color Over(Color top, Color bottom)
    {
        if (bottom.A == 0)
        {
            return top;
        }

        var ta = top.A / 255.0;
        var ba = bottom.A / 255.0 * (1 - ta);
        var a = ta + ba;
        byte Mix(byte t, byte b) => (byte)Math.Round(((t * ta) + (b * ba)) / a);
        return Color.FromArgb((byte)Math.Round(a * 255), Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }
}
