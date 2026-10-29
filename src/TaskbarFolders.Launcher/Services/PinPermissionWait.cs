using System;
using System.Threading.Tasks;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Asks whether Windows allows pinning more than once before concluding that it does not.
/// </summary>
/// <remarks>
/// <c>TaskbarManager.IsPinningAllowed</c> was observed false two seconds after a group's shortcut
/// was written and true twenty-three seconds later, with nothing else changed. Waiting only
/// gives the shell time to take the new shortcut up; the consent dialog Windows then shows is
/// untouched.
/// </remarks>
public static class PinPermissionWait
{
    /// <summary>How often <c>IsPinningAllowed</c> is asked before the Launcher gives up.</summary>
    public const int DefaultAttempts = 6;

    /// <summary>The pause between two questions.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Returns as soon as <paramref name="isAllowed"/> says yes, and <see langword="false"/> after
    /// it has said no <paramref name="attempts"/> times. Nothing is waited for before the first
    /// question or after the last answer.
    /// </summary>
    /// <param name="isAllowed">Asks Windows.</param>
    /// <param name="attempts">How many times to ask; at least 1.</param>
    /// <param name="interval">The pause between two questions.</param>
    /// <param name="delay">Pauses; defaults to <see cref="Task.Delay(TimeSpan)"/>. Replaced in tests.</param>
    public static async Task<bool> WaitUntilAllowedAsync(
        Func<bool> isAllowed,
        int attempts,
        TimeSpan interval,
        Func<TimeSpan, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(isAllowed);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        delay ??= Task.Delay;
        for (var attempt = 1; ; attempt++)
        {
            if (isAllowed())
            {
                return true;
            }

            if (attempt >= attempts)
            {
                return false;
            }

            await delay(interval).ConfigureAwait(true);
        }
    }
}
