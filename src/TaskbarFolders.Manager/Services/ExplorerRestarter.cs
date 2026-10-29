using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TaskbarFolders.Manager.Services;

/// <summary>Default <see cref="IShellRestarter"/>: ends <c>explorer.exe</c> in this session and starts it again.</summary>
/// <remarks>
/// Windows restarts the shell by itself when it ends (<c>AutoRestartShell</c>), but that can be
/// turned off, so the shell is started here if it has not come back within a few seconds.
/// Open File Explorer windows usually run in the same process and close with it, which is why
/// this only runs after the user has agreed.
/// </remarks>
public sealed class ExplorerRestarter : IShellRestarter
{
    private const string ProcessName = "explorer";
    private static readonly TimeSpan _comeBackWait = TimeSpan.FromSeconds(5);

    private readonly ILogger<ExplorerRestarter>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public ExplorerRestarter(ILogger<ExplorerRestarter>? logger = null) => _logger = logger;

    /// <inheritdoc/>
    public async Task RestartAsync()
    {
        using var current = Process.GetCurrentProcess();
        var session = current.SessionId;

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                // Another user's shell is theirs.
                if (process.SessionId != session)
                {
                    continue;
                }

                try
                {
                    process.Kill();
                    await process.WaitForExitAsync().WaitAsync(_comeBackWait).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or TimeoutException)
                {
                    _logger?.LogWarning(ex, "Could not end Explorer ({Pid}).", process.Id);
                }
            }
        }

        var deadline = DateTime.UtcNow + _comeBackWait;
        while (DateTime.UtcNow < deadline)
        {
            if (IsRunningIn(session))
            {
                _logger?.LogInformation("Explorer restarted.");
                return;
            }
            await Task.Delay(250).ConfigureAwait(true);
        }

        try
        {
            using var started = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            _logger?.LogInformation("Explorer did not come back by itself; started it.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _logger?.LogWarning(ex, "Could not start Explorer again.");
        }
    }

    private static bool IsRunningIn(int session)
    {
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Any(p => p.SessionId == session);
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }
}
