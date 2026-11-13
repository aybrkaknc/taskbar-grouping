using System;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Hosting;
using TaskbarFolders.Launcher.Interop;
using TaskbarFolders.Shared.Hosting;

namespace TaskbarFolders.Launcher;

/// <summary>
/// The launcher's entry point. It replaces the one WPF generates so that, while a resident launcher
/// is running, a click is passed on to it before WPF, DI or any window is loaded: this process sends
/// the request and exits. Without a resident launcher it starts the application as before, and that
/// process becomes the resident one.
/// </summary>
internal static class Program
{
    /// <summary>Starts the launcher.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        if (TryForwardToHost(args))
        {
            return 0;
        }

        // What the generated entry point does: App.xaml declares no resources, so there is no
        // InitializeComponent to call.
        var app = new App();
        return app.Run();
    }

    private static bool TryForwardToHost(string[] args)
    {
        // Pinning a group is a different job, done by a short-lived process of its own.
        if (CommandLineParser.HasPinMode(args) || !HostEndpoint.HostExists())
        {
            return false;
        }

        var groupId = CommandLineParser.TryParseGroupId(args);
        if (groupId is null)
        {
            // A tile pinned through the Windows API starts without its arguments; the group is
            // recovered from the AUMID Windows gave this process.
            var aumid = NativeMethods.TryGetCurrentProcessAumid();
            if (aumid is null || !GroupAumid.TryExtractGroupId(aumid, out var fromAumid))
            {
                return false;
            }

            groupId = fromAumid;
        }

        // The anchor is in device pixels, so the process must be per-monitor DPI aware before the
        // cursor is read (the launcher has no manifest), exactly as when it opens a popup itself.
        try
        {
            _ = NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1703 Windows: leave the awareness at its default.
        }

        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        var request = new HostRequest(groupId, cursor.X, cursor.Y, CommandLineParser.ParseFilesToAdd(args));
        return HostForwarder.TryForward(request);
    }
}
