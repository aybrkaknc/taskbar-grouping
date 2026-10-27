using System;
using System.Collections.Generic;

namespace TaskbarFolders.Launcher.Configuration;

/// <summary>
/// Parses launcher command-line arguments into strongly typed options.
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// Name of the argument that carries the target group identifier.
    /// </summary>
    public const string GroupIdArg = "--group-id";

    /// <summary>
    /// Flag that routes the launcher into pin-to-taskbar mode instead of opening the popup.
    /// </summary>
    public const string PinModeArg = "--pin-mode";

    /// <summary>
    /// Returns <see langword="true"/> if <see cref="PinModeArg"/> appears in the argument vector.
    /// </summary>
    /// <param name="args">Raw arguments as received from <see cref="System.Windows.StartupEventArgs.Args"/>.</param>
    public static bool HasPinMode(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (string.Equals(arg, PinModeArg, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Attempts to extract the group identifier from the supplied argument vector.
    /// </summary>
    /// <param name="args">Raw arguments as received from <see cref="System.Windows.StartupEventArgs.Args"/>.</param>
    /// <returns>The group identifier or <see langword="null"/> if absent or empty.</returns>
    public static string? TryParseGroupId(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], GroupIdArg, StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i + 1];
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the positional arguments: files to add to the group. They arrive when a file is
    /// sent through Explorer's "Taskbar Groups" menu (<c>--group-id &lt;id&gt; "%1"</c>) or dropped
    /// on the group's taskbar tile, which appends the file to the pinned shortcut's arguments.
    /// Flags, the value of <see cref="GroupIdArg"/>, and anything starting with <c>--</c> are skipped;
    /// whether a file is an acceptable app is decided later, by <c>AppEntry.TryCreateFromPath</c>.
    /// </summary>
    /// <param name="args">Raw arguments as received from <see cref="System.Windows.StartupEventArgs.Args"/>.</param>
    public static IReadOnlyList<string> ParseFilesToAdd(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, GroupIdArg, StringComparison.OrdinalIgnoreCase))
            {
                i++; // skip its value
                continue;
            }

            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(arg);
        }

        return files;
    }
}
