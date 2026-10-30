using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace TaskbarFolders.Shared.Models;

/// <summary>
/// Represents a single application entry within a taskbar group.
/// </summary>
public sealed class AppEntry
{
    /// <summary>
    /// What <see cref="Path"/> starts with for an app stored as its entry in Windows' installed-apps
    /// list (ADR-009): the shell starts it from there exactly as Start does.
    /// </summary>
    public const string ShellAppsPrefix = @"shell:AppsFolder\";

    /// <summary>
    /// Display name of the application.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Full path to the executable or shortcut file, or <see cref="ShellAppsPrefix"/> followed by
    /// the app's AppUserModelID for an app picked from the installed-apps list.
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>
    /// Optional custom icon path. If null, the icon is extracted from the executable.
    /// </summary>
    [JsonPropertyName("iconPath")]
    public string? IconPath { get; set; }

    /// <summary>
    /// Optional command-line arguments to pass when launching the application.
    /// Null means "no arguments" and is omitted on write to keep config files compact.
    /// </summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    /// <summary>
    /// Builds an entry for a file the user picked or dropped, applying the one rule both the
    /// Manager and the popup use: only <c>.exe</c> and <c>.lnk</c> files are accepted (any
    /// case), and the display name is the file name without its extension.
    /// </summary>
    /// <param name="path">Path of the picked file.</param>
    /// <param name="entry">The new entry, or <see langword="null"/> when the file is not accepted.</param>
    /// <returns><see langword="true"/> when an entry was created.</returns>
    public static bool TryCreateFromPath(string? path, [NotNullWhen(true)] out AppEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = System.IO.Path.GetExtension(path);
        if (!string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        entry = new AppEntry
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Path = path,
        };
        return true;
    }

    /// <summary>
    /// Builds an entry for an app picked from Windows' installed-apps list that has no plain
    /// program to point at — a packaged app, or one Start starts with arguments (ADR-009). It is
    /// stored as <see cref="ShellAppsPrefix"/> and its AppUserModelID.
    /// </summary>
    /// <param name="name">The name the list shows for the app.</param>
    /// <param name="aumid">The app's AppUserModelID, its parsing name in <c>shell:AppsFolder</c>.</param>
    /// <param name="entry">The new entry, or <see langword="null"/> for a blank name or id.</param>
    /// <returns><see langword="true"/> when an entry was created.</returns>
    public static bool TryCreateFromAumid(string? name, string? aumid, [NotNullWhen(true)] out AppEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(aumid))
        {
            return false;
        }

        entry = new AppEntry { Name = name.Trim(), Path = ShellAppsPrefix + aumid.Trim() };
        return true;
    }

    /// <summary>Whether <paramref name="path"/> names an app by its installed-apps entry rather than a file.</summary>
    /// <param name="path">An <see cref="AppEntry.Path"/>.</param>
    /// <returns><see langword="true"/> for <see cref="ShellAppsPrefix"/> followed by an id.</returns>
    public static bool IsShellApp(string? path) =>
        path is not null
        && path.Length > ShellAppsPrefix.Length
        && path.StartsWith(ShellAppsPrefix, StringComparison.OrdinalIgnoreCase);
}
