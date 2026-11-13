using System;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Launcher.Interop;
using TaskbarFolders.Launcher.Services;

namespace TaskbarFolders.Launcher.Hosting;

/// <summary>
/// Gives memory back while the resident launcher is idle. A moment after the last popup has
/// closed it collects garbage and asks Windows to trim the working set: the pages are not freed,
/// they move to the standby list, where the next popup takes them back at once, and Task Manager
/// shows the launcher at a fraction of what it held while a popup was open.
/// </summary>
internal sealed class IdleTrimmer
{
    private readonly DispatcherTimer _timer;
    private readonly Func<bool> _isIdle;
    private readonly ILogger? _logger;
    private readonly PerfRecorder? _perf;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="isIdle">Whether no popup is open; checked again when the delay is over.</param>
    /// <param name="delay">How long after the last popup closed to trim.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="perf">Optional perf recorder.</param>
    public IdleTrimmer(Func<bool> isIdle, TimeSpan delay, ILogger? logger = null, PerfRecorder? perf = null)
    {
        ArgumentNullException.ThrowIfNull(isIdle);

        _isIdle = isIdle;
        _logger = logger;
        _perf = perf;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
        _timer.Tick += (_, _) => Trim();
    }

    /// <summary>Starts (or restarts) the countdown to a trim.</summary>
    public void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Stops the countdown.</summary>
    public void Cancel() => _timer.Stop();

    private void Trim()
    {
        _timer.Stop();
        if (!_isIdle())
        {
            return;
        }

        using var self = Process.GetCurrentProcess();
        self.Refresh();
        var before = self.WorkingSet64;

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        _ = NativeMethods.SetProcessWorkingSetSize(self.Handle, -1, -1);

        self.Refresh();
        var after = self.WorkingSet64;
        _logger?.LogInformation("Idle: working set trimmed from {Before:F0} MB to {After:F0} MB.", before / 1048576.0, after / 1048576.0);
        _perf?.Record("host.trim", ("before_mb", before / 1048576.0), ("after_mb", after / 1048576.0));
    }
}
