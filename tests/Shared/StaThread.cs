using System.Threading;
using System.Windows.Threading;

namespace TaskbarFolders.TestSupport;

/// <summary>
/// Runs WPF test bodies on a short-lived STA thread — WPF objects require one and xUnit runs
/// tests on MTA threads — and pumps that thread's dispatcher. Shared by every test project.
/// </summary>
internal static class StaThread
{
    /// <summary>Runs <paramref name="body"/> on a new STA thread and returns its result.</summary>
    /// <exception cref="InvalidOperationException">The body threw; the original is the inner exception.</exception>
    public static T Run<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("STA test body failed.", failure);
        }

        return result;
    }

    /// <summary>Runs <paramref name="body"/> on a new STA thread.</summary>
    public static void Run(Action body) => Run(() =>
    {
        body();
        return 0;
    });

    /// <summary>Runs the current thread's dispatcher loop for <paramref name="duration"/>.</summary>
    public static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Pumps the dispatcher until <paramref name="condition"/> holds or <paramref name="timeout"/>
    /// passes — on a loaded machine no fixed wait is long enough.
    /// </summary>
    /// <returns>Whether the condition was met.</returns>
    public static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }
            Pump(TimeSpan.FromMilliseconds(50));
        }
        return true;
    }
}
