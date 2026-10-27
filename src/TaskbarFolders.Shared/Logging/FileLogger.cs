using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TaskbarFolders.Shared.Logging;

/// <summary>
/// Per-category logger that writes formatted entries to a daily-rotated file under
/// <see cref="FileLoggerOptions.Directory"/>. Acquires a shared lock on every write so that
/// multiple categories cannot interleave bytes within a single line.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly FileLoggerOptions _options;
    private readonly object _writeLock;

    public FileLogger(string category, FileLoggerOptions options, object writeLock)
    {
        _category = category;
        _options = options;
        _writeLock = writeLock;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(logLevel.ToString());
        builder.Append("] ");
        builder.Append(_category);
        builder.Append(": ");
        builder.Append(message);
        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }
        builder.AppendLine();

        var line = builder.ToString();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var file = Path.Combine(_options.Directory, $"{_options.FilePrefix}-{today}.log");

        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(file, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the caller down. The folder is gone after an
                // uninstall, and the line is dropped rather than the folder re-created.
            }
        }
    }
}
