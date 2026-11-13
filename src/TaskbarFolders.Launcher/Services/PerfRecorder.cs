using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Writes timing measurements of the launcher as JSON lines, one object per line, to
/// <c>perf-launcher-yyyy-MM-dd.jsonl</c> in the logs folder. Every record carries the wall-clock
/// time and <c>ms_since_process_start</c>, so the file can be read or filtered without the
/// running program. Measuring must never break the launcher: write failures are swallowed.
/// </summary>
public sealed class PerfRecorder
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly DateTime _processStart;

    /// <summary>Initializes a new instance that writes to the logs folder.</summary>
    /// <param name="paths">Source of the logs folder.</param>
    public PerfRecorder(IAppDataPathProvider paths)
        : this(RequireNonNull(paths).LogsDirectory)
    {
    }

    /// <summary>For tests and for callers that already know the folder.</summary>
    /// <param name="directory">The folder to write the day's file into.</param>
    public PerfRecorder(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _processStart = Process.GetCurrentProcess().StartTime;
    }

    /// <summary>The file a record written now goes to.</summary>
    public string CurrentFile => Path.Combine(_directory, $"perf-launcher-{DateTime.Now:yyyy-MM-dd}.jsonl");

    /// <summary>
    /// Appends one record. <paramref name="fields"/> are written as given; values must be
    /// JSON-serializable (strings, numbers, booleans).
    /// </summary>
    /// <param name="evt">The event name, such as <c>popup.first_render</c>.</param>
    /// <param name="fields">The values to record, as key/value pairs.</param>
    public void Record(string evt, params (string Key, object? Value)[] fields)
    {
        var line = new Dictionary<string, object?>
        {
            ["time"] = DateTimeOffset.Now.ToString("O"),
            ["ms_since_process_start"] = Round((DateTime.Now - _processStart).TotalMilliseconds),
            ["pid"] = Environment.ProcessId,
            ["event"] = evt,
        };
        foreach (var (key, value) in fields)
        {
            line[key] = value is double d ? Round(d) : value;
        }

        try
        {
            var json = JsonSerializer.Serialize(line, _json);
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(CurrentFile, json + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Measuring is best-effort: the launcher keeps working without a record.
            Debug.WriteLine($"Perf record '{evt}' not written: {ex.Message}");
        }
    }

    /// <summary>
    /// How an app is started, for grouping timings: <c>shell</c> for an installed app
    /// (<c>shell:AppsFolder</c>), otherwise the file extension without its dot (<c>lnk</c>, <c>exe</c>, …).
    /// </summary>
    public static string KindOf(string? path)
    {
        if (AppEntry.IsShellApp(path))
        {
            return "shell";
        }

        var extension = string.IsNullOrEmpty(path) ? string.Empty : Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension.Length == 0 ? "none" : extension;
    }

    /// <summary>Milliseconds elapsed between two <see cref="Stopwatch.GetTimestamp"/> readings.</summary>
    public static double ElapsedMs(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static double Round(double value) => Math.Round(value, 1);

    private static IAppDataPathProvider RequireNonNull(IAppDataPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths;
    }
}
