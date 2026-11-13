using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskbarFolders.Shared.Hosting;

namespace TaskbarFolders.Click;

/// <summary>
/// What a pinned tile (or an Explorer menu entry) starts. If a resident launcher is running, this
/// sends it the click — the group, where the cursor was, and any files — and exits; the launcher
/// opens the popup. If there is none, it starts the real launcher with the arguments it was given,
/// and that launcher becomes the resident one. Pin mode is the launcher's job and is passed on.
/// </summary>
internal static class Program
{
    private const string LauncherFileName = "TaskbarFolders.Launcher.exe";
    private const string GroupIdArg = "--group-id";
    private const string PinModeArg = "--pin-mode";

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (HANDLE)-4
    private static readonly IntPtr _perMonitorAwareV2 = new(-4);

    private static int Main(string[] args)
    {
        if (!HasArg(args, PinModeArg) && TryForward(args))
        {
            return 0;
        }

        return StartLauncher(args);
    }

    private static bool TryForward(string[] args)
    {
        if (!HostEndpoint.HostExists())
        {
            return false;
        }

        var groupId = ValueOf(args, GroupIdArg) ?? GroupIdFromAumid();
        if (groupId is null)
        {
            return false;
        }

        // The anchor is in device pixels, so this process must be per-monitor DPI aware before the
        // cursor is read: it has no manifest, exactly as the launcher has none.
        _ = SetProcessDpiAwarenessContext(_perMonitorAwareV2);
        if (!GetCursorPos(out var cursor))
        {
            return false;
        }

        return HostForwarder.TryForward(new HostRequest(groupId, cursor.X, cursor.Y, FilesIn(args)));
    }

    // The real launcher sits next to this program. It starts with the original arguments, so a
    // click that could not be passed on behaves exactly as it did before there was a client.
    private static int StartLauncher(string[] args)
    {
        var launcher = Path.Combine(AppContext.BaseDirectory, LauncherFileName);
        if (!File.Exists(launcher))
        {
            return 1;
        }

        var start = new ProcessStartInfo(launcher)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            return process is null ? 1 : 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 1;
        }
    }

    private static bool HasArg(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ValueOf(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(args[i + 1]) ? null : args[i + 1];
            }
        }

        return null;
    }

    // Positional arguments are files to add: sent from Explorer's menu or dropped on the tile.
    private static List<string> FilesIn(string[] args)
    {
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], GroupIdArg, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(args[i]) && !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                files.Add(args[i]);
            }
        }

        return files;
    }

    // A tile pinned through the Windows API starts without its arguments; the group is recovered
    // from the AUMID Windows gave this process.
    private static string? GroupIdFromAumid()
    {
        if (GetCurrentProcessExplicitAppUserModelID(out var pointer) != 0 || pointer == IntPtr.Zero)
        {
            return null;
        }

        string? aumid;
        try
        {
            aumid = Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }

        const string prefix = HostEndpoint.GroupAumidPrefix;
        if (aumid is null || !aumid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || aumid.Length == prefix.Length)
        {
            return null;
        }

        return aumid[prefix.Length..];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);
}
