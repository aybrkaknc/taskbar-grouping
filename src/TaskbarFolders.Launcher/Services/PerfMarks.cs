using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Named timestamps taken while the popup opens, so one record can say where the time between
/// two coarse checkpoints went. Only the first mark of each name counts. Used from the UI thread
/// only; a mark costs a <see cref="Stopwatch.GetTimestamp"/> and a set lookup.
/// </summary>
internal sealed class PerfMarks
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<(string Name, long Ticks)> _marks = [];

    /// <summary>Records now under <paramref name="name"/>, unless that name was marked before.</summary>
    /// <param name="name">A short snake_case name for the point reached.</param>
    public void Mark(string name)
    {
        if (_seen.Add(name))
        {
            _marks.Add((name, Stopwatch.GetTimestamp()));
        }
    }

    /// <summary>
    /// The marks as perf-record fields: <c>mark_&lt;name&gt;_ms</c>, milliseconds after
    /// <paramref name="origin"/>, in the order they were taken.
    /// </summary>
    /// <param name="origin">A <see cref="Stopwatch.GetTimestamp"/> reading to measure from.</param>
    /// <returns>One field per mark.</returns>
    public IEnumerable<(string Key, object? Value)> ToFields(long origin)
    {
        foreach (var (name, ticks) in _marks)
        {
            yield return ($"mark_{name}_ms", PerfRecorder.ElapsedMs(origin, ticks));
        }
    }
}
