using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// The records Windows writes about a short-lived process <em>as it exits</em> (the Program
/// Compatibility Assistant's <c>SACP</c> values) come after the uninstall's own cleanup, and a
/// process cannot delete its own exit record. This builds the script a detached <c>cmd</c> runs
/// once the Manager (and the installer's uninstaller) have exited. For a portable copy the same
/// script also deletes what a running Manager cannot: the copy's own folders.
/// </summary>
public static class CompatibilityRecordSweep
{
    /// <summary>How long the script waits for the two processes to exit before it sweeps anyway.</summary>
    public const int WaitSeconds = 120;

    /// <summary>Seconds after the last exit at which the first deletion runs.</summary>
    /// <remarks>Windows was measured writing a record 3.4 s after the process exited.</remarks>
    public const int FirstPassSeconds = 3;

    /// <summary>Seconds after the last exit at which the deletion runs again, for a record Windows wrote late.</summary>
    public const int SecondPassSeconds = 5;

    private const string ManagerExe = "TaskbarFolders.Manager.exe";
    private const string UninstallerExe = InstallLocation.InstallerUninstallerName;
    private const string System32 = "%SystemRoot%\\System32\\";

    /// <summary>
    /// The executable paths that run during an uninstall and so get a record on exit.
    /// </summary>
    /// <param name="installRoot">Folder holding <c>Manager</c> and <c>Launcher</c>; a trailing separator is accepted.</param>
    public static IReadOnlyList<string> RecordPaths(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var root = Path.TrimEndingDirectorySeparator(installRoot);
        return
        [
            root + @"\Manager\" + ManagerExe,
            root + @"\Launcher\TaskbarFolders.Launcher.exe",
            root + @"\" + UninstallerExe,
        ];
    }

    /// <summary>Builds the batch script for the copy installed in <paramref name="installRoot"/>.</summary>
    /// <param name="installRoot">Folder holding <c>Manager</c> and <c>Launcher</c>.</param>
    /// <exception cref="ArgumentException">The path contains a character that cannot be quoted in a batch file.</exception>
    public static string BuildScript(string installRoot) => Build(installRoot, portableAppDataRoot: null);

    /// <summary>
    /// Builds the batch script for an extracted portable copy: the registry sweep, then the
    /// <c>Manager</c> and <c>Launcher</c> folders, the runtime .NET unpacked into <c>%TEMP%</c>,
    /// the data folder (the Manager's last log lines), and finally the extracted folder itself if
    /// nothing else is in it.
    /// </summary>
    /// <param name="installRoot">Folder holding <c>Manager</c> and <c>Launcher</c>.</param>
    /// <param name="appDataRoot">The <c>%APPDATA%\TaskbarFolders</c> folder.</param>
    /// <exception cref="ArgumentException">A path contains a character that cannot be quoted in a batch file.</exception>
    public static string BuildPortableScript(string installRoot, string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        return Build(installRoot, appDataRoot);
    }

    private static string Build(string installRoot, string? portableAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        RequireQuotable(installRoot);
        if (portableAppDataRoot is not null)
        {
            RequireQuotable(portableAppDataRoot);
        }

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("set /a n=0");
        script.AppendLine(":wait");
        script.AppendLine($"{System32}tasklist.exe /fi \"imagename eq {ManagerExe}\" /nh | {System32}find.exe /i \"{ManagerExe}\" >nul && goto again");
        script.AppendLine($"{System32}tasklist.exe /fi \"imagename eq {UninstallerExe}\" /nh | {System32}find.exe /i \"{UninstallerExe}\" >nul && goto again");
        script.AppendLine("goto sweep");
        script.AppendLine(":again");
        script.AppendLine("set /a n+=1");
        script.AppendLine(FormattableString.Invariant($"if %n% geq {WaitSeconds} goto sweep"));
        script.AppendLine($"{System32}ping.exe -n 2 127.0.0.1 >nul");
        script.AppendLine("goto wait");
        script.AppendLine(":sweep");

        // ping -n N waits N - 1 seconds. The second pass also retries a folder that a virus
        // scanner still held at the first.
        script.AppendLine(FormattableString.Invariant($"{System32}ping.exe -n {FirstPassSeconds + 1} 127.0.0.1 >nul"));
        AppendPass(script, installRoot, portableAppDataRoot);
        script.AppendLine(FormattableString.Invariant($"{System32}ping.exe -n {SecondPassSeconds - FirstPassSeconds + 1} 127.0.0.1 >nul"));
        AppendPass(script, installRoot, portableAppDataRoot);

        if (portableAppDataRoot is not null)
        {
            // No /s: a folder that holds anything of the user's own stays.
            script.AppendLine(string.Create(CultureInfo.InvariantCulture, $"rd \"{Escape(Path.TrimEndingDirectorySeparator(installRoot))}\" 2>nul"));
        }

        // "(goto)" ends the batch first, so deleting the running script does not confuse cmd.
        script.AppendLine("(goto) 2>nul & del \"%~f0\"");
        return script.ToString();
    }

    private static void AppendPass(StringBuilder script, string installRoot, string? portableAppDataRoot)
    {
        foreach (var key in Uninstaller.WindowsRecordKeys)
        {
            foreach (var path in RecordPaths(installRoot))
            {
                foreach (var name in ValueNames(key, path))
                {
                    script.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{System32}reg.exe delete \"HKCU\\{key}\" /v \"{Escape(name)}\" /f >nul 2>&1"));
                }
            }
        }

        if (portableAppDataRoot is null)
        {
            return;
        }

        var root = Escape(Path.TrimEndingDirectorySeparator(installRoot));
        script.AppendLine(string.Create(CultureInfo.InvariantCulture, $"rd /s /q \"{root}\\Manager\" 2>nul"));
        script.AppendLine(string.Create(CultureInfo.InvariantCulture, $"rd /s /q \"{root}\\Launcher\" 2>nul"));
        script.AppendLine("rd /s /q \"%TEMP%\\.net\\TaskbarFolders.Manager\" 2>nul");
        script.AppendLine("rd /s /q \"%TEMP%\\.net\\TaskbarFolders.Launcher\" 2>nul");
        script.AppendLine(string.Create(CultureInfo.InvariantCulture, $"rd /s /q \"{Escape(portableAppDataRoot)}\" 2>nul"));
    }

    /// <summary>
    /// The value names one executable has in <paramref name="key"/>. MuiCache stores a path with
    /// <c>.FriendlyAppName</c> or <c>.ApplicationCompany</c> appended; the other keys use the path.
    /// </summary>
    private static IEnumerable<string> ValueNames(string key, string path)
    {
        if (key != Uninstaller.WindowsRecordKeys[0])
        {
            yield return path;
            yield break;
        }

        yield return path;
        yield return path + ".FriendlyAppName";
        yield return path + ".ApplicationCompany";
    }

    private static void RequireQuotable(string path)
    {
        if (path.AsSpan().IndexOfAny('"', '\r', '\n') >= 0)
        {
            throw new ArgumentException("The path cannot be quoted in a batch file.", nameof(path));
        }
    }

    private static string Escape(string path) => path.Replace("%", "%%", StringComparison.Ordinal);
}
