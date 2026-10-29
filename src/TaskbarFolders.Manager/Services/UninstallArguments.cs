using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// The Manager's uninstall mode: <c>TaskbarFolders.Manager.exe --uninstall [--no-confirm]</c>,
/// run by the installer's uninstaller and, for a portable copy, by Settings.
/// </summary>
public static class UninstallArguments
{
    /// <summary>Runs <see cref="IUninstaller"/> instead of opening the Manager.</summary>
    public const string Uninstall = "--uninstall";

    /// <summary>
    /// Skips the confirmation and reports only problems — for a caller that has already asked
    /// (the installer's uninstaller, which also runs unattended with <c>/VERYSILENT</c>).
    /// </summary>
    public const string NoConfirm = "--no-confirm";

    /// <summary>Exit code: everything was removed.</summary>
    public const int ExitComplete = 0;

    /// <summary>Exit code: something was left behind; the log and the message say what.</summary>
    public const int ExitIncomplete = 1;

    /// <summary>Exit code: the user said no. Nothing has been touched, and no helper was started.</summary>
    public const int ExitCancelled = 2;

    /// <summary>Whether <paramref name="args"/> ask for uninstall mode.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static bool IsUninstall(IEnumerable<string> args) => Has(args, Uninstall);

    /// <summary>Whether <paramref name="args"/> contain <see cref="NoConfirm"/>.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static bool IsNoConfirm(IEnumerable<string> args) => Has(args, NoConfirm);

    /// <summary>Question asked before uninstalling.</summary>
    public const string ConfirmMessage =
        "This removes TaskbarFolders from this PC: its taskbar tiles, the \"Taskbar Groups\" menu, the " +
        "start-with-Windows entry, its Start menu entries, and your groups and settings.\n\nContinue?";

    /// <summary>What to tell the user after <see cref="IUninstaller.RunAsync"/>.</summary>
    /// <param name="report">The uninstaller's result.</param>
    public static string ResultMessage(UninstallReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Complete)
        {
            return "TaskbarFolders has been removed from this PC.";
        }

        var tiles = report.TilesLeftPinned > 0
            ? $"{report.TilesLeftPinned} group tile(s) are still on the taskbar: right-click each and choose " +
              "\"Unpin from taskbar\". "
            : string.Empty;
        // Not "see the log": the log folder is part of what an uninstall deletes.
        return tiles + "Some traces could not be removed, usually because a file was in use. " +
            "Restarting Windows and uninstalling again removes them.";
    }

    private static bool Has(IEnumerable<string> args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
