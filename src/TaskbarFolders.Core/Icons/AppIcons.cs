using System;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Core.Icons;

/// <summary>
/// The one way an app's icon is looked up — by sync for the group's composite, by the Manager
/// for its list and by the popup for its tiles: the image the user chose for the app first,
/// then the icon cache, then the shell, whose result is cached.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppIcons
{
    /// <summary>
    /// <paramref name="app"/>'s icon at <paramref name="size"/>, frozen, or <see langword="null"/>
    /// when it has none (callers decide whether to show a placeholder). Blocking: file and shell
    /// work, so the popup calls it off the UI thread.
    /// </summary>
    /// <param name="app">The app.</param>
    /// <param name="size">Pixel size asked of the shell and used as the cache key.</param>
    /// <param name="extractor">Shell icon extraction.</param>
    /// <param name="cache">Cache of extracted icons, keyed by path, write time and size.</param>
    public static BitmapSource? Load(AppEntry app, int size, IIconExtractor extractor, IIconCache cache)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A chosen image is a file of ours, read as it is; the cache is for what the shell
        // extracts.
        return ImageFile.TryLoad(app.IconPath) ?? Extract(app.Path, size, extractor, cache);
    }

    /// <summary>The program's own icon for <paramref name="path"/>, through the cache.</summary>
    /// <param name="path">Executable or shortcut.</param>
    /// <param name="size">Pixel size asked of the shell and used as the cache key.</param>
    /// <param name="extractor">Shell icon extraction.</param>
    /// <param name="cache">Cache of extracted icons.</param>
    public static BitmapSource? Extract(string path, int size, IIconExtractor extractor, IIconCache cache)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(cache);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (cache.TryGet(path, size, out var cached))
        {
            return cached;
        }

        var icon = extractor.ExtractIcon(path, size);
        if (icon is null)
        {
            return null;
        }

        // Frozen before it is cached or handed to another thread: an unfrozen bitmap keeps the
        // affinity of the thread that made it.
        if (icon.CanFreeze && !icon.IsFrozen)
        {
            icon.Freeze();
        }
        cache.Set(path, size, icon);
        return icon;
    }
}
