using System;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Win32;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Theme applier for the launcher. It runs once per popup: with no live theme-change listener
/// like the Manager has, a resident launcher re-reads the setting and applies it again each time
/// a popup opens, replacing the dictionary it merged the last time.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LauncherThemeApplier
{
    // The dictionary this class merged last, per application, so a second Apply swaps it
    // instead of stacking another theme in front of it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Application, ResourceDictionary> _applied = new();

    private const string LightDictionaryUri = "/TaskbarFolders.Launcher;component/Themes/Light.xaml";
    private const string DarkDictionaryUri = "/TaskbarFolders.Launcher;component/Themes/Dark.xaml";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>
    /// Merges the chosen theme dictionary into <see cref="Application.Resources"/>, in front of
    /// everything else, and removes the one a previous call merged.
    /// </summary>
    public static void Apply(Application app, ThemePreference preference)
    {
        ArgumentNullException.ThrowIfNull(app);

        var effective = Resolve(preference);
        var uri = effective == ThemePreference.Dark
            ? new Uri(DarkDictionaryUri, UriKind.Relative)
            : new Uri(LightDictionaryUri, UriKind.Relative);

        if (_applied.TryGetValue(app, out var previous))
        {
            app.Resources.MergedDictionaries.Remove(previous);
            _applied.Remove(app);
        }

        var dictionary = new ResourceDictionary { Source = uri };
        app.Resources.MergedDictionaries.Insert(0, dictionary);
        _applied.Add(app, dictionary);
    }

    /// <summary>
    /// Resolves <see cref="ThemePreference.System"/> against the current Windows setting.
    /// Light/Dark return verbatim. Defaults to Light if the registry key is missing.
    /// </summary>
    public static ThemePreference Resolve(ThemePreference preference)
    {
        if (preference != ThemePreference.System)
        {
            return preference;
        }

        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
        return key?.GetValue(AppsUseLightThemeValue) is int value && value == 0
            ? ThemePreference.Dark
            : ThemePreference.Light;
    }
}
