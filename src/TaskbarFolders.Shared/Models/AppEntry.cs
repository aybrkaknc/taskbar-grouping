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
    /// Display name of the application.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Full path to the executable or shortcut file.
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
}
