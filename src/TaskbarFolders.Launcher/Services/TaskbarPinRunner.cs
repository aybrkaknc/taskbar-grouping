using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using Windows.Foundation.Metadata;
using Windows.UI.Shell;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Wraps <see cref="TaskbarManager.RequestPinCurrentAppAsync"/> with exit-code semantics so
/// the Manager-spawned launcher process can communicate the outcome back without IPC.
/// </summary>
/// <remarks>
/// Exit codes:
/// <list type="bullet">
///   <item><c>0</c> — pinned (user clicked Allow in the system dialog).</item>
///   <item><c>1</c> — user denied (clicked Cancel or closed the dialog).</item>
///   <item><c>2</c> — TaskbarManager unsupported on this Windows build or pinning policy
///   disabled (LTSC, Education, restricted Group Policy). Manager falls back to the
///   Explorer / .lnk flow.</item>
///   <item><c>3</c> — unexpected exception during the call. Logged.</item>
///   <item><c>5</c> — the API reported success but no pinned shortcut carrying the group's
///   AUMID could be found afterwards. Reported separately from success because claiming
///   "Pinned" with no tile behind it is indistinguishable from the app being broken.</item>
/// </list>
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class TaskbarPinRunner
{
    private readonly PinVerifier _verifier;
    private readonly ILogger<TaskbarPinRunner>? _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="verifier">Checks afterwards whether a tile actually appeared.</param>
    /// <param name="logger">Optional logger.</param>
    public TaskbarPinRunner(PinVerifier verifier, ILogger<TaskbarPinRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        _verifier = verifier;
        _logger = logger;
    }

    /// <summary>
    /// Asks Windows to pin the current process (identified by its AUMID) to the taskbar.
    /// Must be called from the UI thread; <paramref name="foregroundWindow"/> must be visible
    /// so the process can hold the foreground while the system consent dialog is shown.
    /// </summary>
    /// <param name="foregroundWindow">Visible host window for this process.</param>
    /// <param name="aumid">
    /// The AppUserModelID stamped on this process, used to verify afterwards that a tile
    /// carrying it really appeared.
    /// </param>
    public async Task<int> RunAsync(Window foregroundWindow, string aumid)
    {
        ArgumentNullException.ThrowIfNull(foregroundWindow);
        ArgumentException.ThrowIfNullOrWhiteSpace(aumid);

        try
        {
            // ApiInformation.IsTypePresent is the canonical "is this WinRT API surface
            // available?" check for desktop-bridge / Win32-with-WinRT callers. Falsy on
            // pre-Win10-1903 builds; returns true on Win11 24H2 + LTSC + Education.
            if (!ApiInformation.IsTypePresent("Windows.UI.Shell.TaskbarManager"))
            {
                _logger?.LogWarning("Windows.UI.Shell.TaskbarManager not present; pin unavailable.");
                return 2;
            }

            var manager = TaskbarManager.GetDefault();
            if (manager is null)
            {
                _logger?.LogWarning("TaskbarManager.GetDefault() returned null; pin unavailable.");
                return 2;
            }

            // Asked again before giving up: right after a group is created Windows answered
            // false, and about twenty seconds later true. The wait costs nothing on a machine
            // that allows pinning, and about five seconds before a machine that does not
            // (notifications off, policy) gets its explanation.
            if (!await PinPermissionWait.WaitUntilAllowedAsync(
                    () => manager.IsPinningAllowed,
                    PinPermissionWait.DefaultAttempts,
                    PinPermissionWait.DefaultInterval).ConfigureAwait(true))
            {
                _logger?.LogWarning(
                    "TaskbarManager.IsPinningAllowed=false after {Attempts} checks, {Interval} apart (notifications off, restricted SKU or policy); pin unavailable.",
                    PinPermissionWait.DefaultAttempts,
                    PinPermissionWait.DefaultInterval);
                return 2;
            }

            // Do NOT call InitializeWithWindow.Initialize on this object. That interop rule
            // applies to WinRT types that own a modal surface (FileOpenPicker, FolderPicker
            // and friends); TaskbarManager is a singleton service from GetDefault() and does
            // not implement IInitializeWithWindow. Calling it QueryInterfaces for an
            // interface the object does not have, and CsWinRT surfaces the failure as
            // InvalidCastException - which the catch below turned into exit code 3 on every
            // single attempt from v0.4.0 until v0.4.8, making everything after this point
            // unreachable. RequestPinCurrentAppAsync parents its own dialog and takes no HWND;
            // what it needs is for us to be the foreground process, which is handled below.

            // v0.4.2 diagnostic: log the Start Menu anchor directory contents at the exact
            // moment of the pin call. RequestPinCurrentAppAsync can return true cosmetically
            // when the Shell's AppsFolder index hasn't materialised the .lnk yet; this log
            // line lets us tell "anchor missing" from "anchor present but indexer raced".
            // Enumeration is defensive: a Defender lock / ACL change must not escalate a
            // diagnostic failure into a failed pin (the outer catch would map it to exit 3).
            var anchorDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "TaskbarFolders");
            var anchorExists = Directory.Exists(anchorDir);
            string anchorFiles;
            try
            {
                anchorFiles = anchorExists
                    ? string.Join(", ", Directory.EnumerateFiles(anchorDir, "*.lnk").Select(Path.GetFileName))
                    : "<dir missing>";
            }
            catch (Exception enumEx) when (enumEx is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                anchorFiles = $"<enum failed: {enumEx.GetType().Name}>";
            }
            _logger?.LogInformation(
                "Pin runner: Start Menu anchor dir={Dir} exists={Exists} contents=[{Files}]",
                anchorDir, anchorExists, anchorFiles);

            // v0.4.2 settle: GroupSyncService already SHChangeNotify-flushes, but the
            // AppsFolder index can still take a moment to surface a brand-new entry. 300 ms
            // is below the ~400 ms "feels instant" threshold so the user does not perceive
            // it as latency between clicking Pin and seeing the system dialog.
            // The consent dialog is shown by the shell on behalf of the foreground app, so a
            // denied foreground promotion is worth seeing in the log: Activate() returns false
            // when Windows' foreground lock refuses. App.xaml.cs activates the host window
            // before calling in; this re-assert covers the window having lost it since.
            var activated = foregroundWindow.Activate();
            if (!activated)
            {
                _logger?.LogWarning(
                    "Pin host window could not be brought to the foreground; the consent dialog may not appear.");
            }

            await Task.Delay(300).ConfigureAwait(true);

            // RequestPinCurrentAppAsync shows a system-managed "Allow [App] to pin?" dialog
            // on behalf of the foreground app; it takes no window handle of its own.
            var pinned = await manager.RequestPinCurrentAppAsync();

            if (pinned)
            {
                _logger?.LogInformation("TaskbarManager reported the current app as pinned.");

                // Trust, then check. A false positive here is what makes the application look
                // broken: a "Pinned" notification with no tile behind it.
                if (_verifier.IsPinned(aumid) == false)
                {
                    return 5;
                }

                return 0;
            }

            _logger?.LogInformation("TaskbarManager: user declined the pin request.");
            return 1;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "TaskbarManager pin call threw unexpectedly.");
            return 3;
        }
    }
}
