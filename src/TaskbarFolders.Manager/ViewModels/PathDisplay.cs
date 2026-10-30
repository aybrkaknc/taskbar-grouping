using System;
using System.IO;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// Formats file paths for display in the UI.
/// </summary>
/// <remarks>
/// The app list shows each entry's path underneath its name, which means the user's account
/// name is on screen for every app installed under their profile. That is noise in normal use
/// and a small privacy leak in any screenshot, screen share or bug report — the kind of thing
/// nobody notices until the image is already published. Collapsing the profile prefix to
/// <c>%USERPROFILE%</c> keeps the path useful for telling two similarly named apps apart while
/// taking the account name off screen. Only the display changes; what is persisted and what is
/// launched is always the real path.
/// </remarks>
internal static class PathDisplay
{
    private const string UserProfileToken = "%USERPROFILE%";

    /// <summary>
    /// Returns <paramref name="path"/> with the current user's profile directory replaced by
    /// <c>%USERPROFILE%</c>, or unchanged when it lies outside the profile.
    /// </summary>
    /// <param name="path">Full path to format. <see langword="null"/> and blank pass through.</param>
    /// <returns>The path as it should be shown to the user.</returns>
    public static string ForDisplay(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path ?? string.Empty;
        }

        // An installed app has no path to show; its id tells two of the same name apart.
        if (TaskbarFolders.Shared.Models.AppEntry.IsShellApp(path))
        {
            return "Installed app · " + path[TaskbarFolders.Shared.Models.AppEntry.ShellAppsPrefix.Length..];
        }

        string profile;
        try
        {
            profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch (PlatformNotSupportedException)
        {
            return path;
        }

        if (string.IsNullOrEmpty(profile))
        {
            return path;
        }

        profile = profile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!path.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var remainder = path[profile.Length..];

        // The prefix must end at a separator, otherwise a sibling directory that merely starts
        // with the profile name — C:\Users\alice-backup next to C:\Users\alice — would be
        // rewritten into something that no longer points anywhere.
        if (remainder.Length == 0)
        {
            return UserProfileToken;
        }

        return remainder[0] is '\\' or '/'
            ? UserProfileToken + remainder
            : path;
    }
}
