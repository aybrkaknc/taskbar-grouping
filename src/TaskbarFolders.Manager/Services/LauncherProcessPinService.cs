using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;

namespace TaskbarFolders.Manager.Services;

/// <summary>
/// Default <see cref="IPinToTaskbarService"/>. Resolves the launcher executable via
/// <see cref="ILauncherPathResolver"/>, spawns it with <c>--pin-mode --group-id &lt;id&gt;</c>,
/// and maps the exit code to a <see cref="PinResult"/>.
/// </summary>
public sealed class LauncherProcessPinService : IPinToTaskbarService
{
    /// <summary>
    /// Hard cap on how long the launcher pin process may run. Generous because the user is
    /// staring at a system Yes/No dialog — alt-tabbing, reading, phone call: 30 s is too
    /// tight; 2 minutes is a comfortable upper bound that still catches stuck processes.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly ILauncherPathResolver _resolver;
    private readonly IProcessRunner _runner;
    private readonly ILogger<LauncherProcessPinService>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public LauncherProcessPinService(
        ILauncherPathResolver resolver,
        IProcessRunner runner,
        ILogger<LauncherProcessPinService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(runner);

        _resolver = resolver;
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PinResult> PinAsync(string groupId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var launcher = _resolver.TryResolve();
        if (launcher is null)
        {
            _logger?.LogError("Launcher binary could not be resolved; cannot invoke pin-mode.");
            return PinResult.Error;
        }

        var psi = new ProcessStartInfo
        {
            FileName = launcher,
            // UseShellExecute=false is required to observe the child exit code.
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(launcher) ?? string.Empty,
        };
        // ArgumentList avoids manual quoting issues with the group id.
        psi.ArgumentList.Add("--pin-mode");
        psi.ArgumentList.Add("--group-id");
        psi.ArgumentList.Add(groupId);

        try
        {
            var exitCode = await _runner.RunAndWaitAsync(psi, DefaultTimeout, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Pin-mode launcher exit code {ExitCode} for group {GroupId}.", exitCode, groupId);
            return exitCode switch
            {
                0 => PinResult.Success,
                1 => PinResult.UserDenied,
                2 => PinResult.Unsupported,
                5 => PinResult.NotVerified,
                _ => PinResult.Error,
            };
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "Pin-mode launcher timed out for group {GroupId}.", groupId);
            return PinResult.Error;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Pin-mode launcher failed for group {GroupId}.", groupId);
            return PinResult.Error;
        }
    }
}
