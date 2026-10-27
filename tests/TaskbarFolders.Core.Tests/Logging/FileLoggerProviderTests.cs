using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskbarFolders.Shared.Logging;
using Xunit;

namespace TaskbarFolders.Core.Tests.Logging;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _tempDir;

    public FileLoggerProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TaskbarFolders.Logs." + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static IOptions<FileLoggerOptions> Options(string dir, string prefix = "test", int retainDays = 14, LogLevel min = LogLevel.Information) =>
        Microsoft.Extensions.Options.Options.Create(new FileLoggerOptions
        {
            Directory = dir,
            FilePrefix = prefix,
            RetainDays = retainDays,
            MinimumLevel = min,
        });

    [Fact]
    public void Ctor_ThrowsWhenDirectoryUnset()
    {
        var action = () => new FileLoggerProvider(Options(string.Empty));

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_CreatesDirectoryIfMissing()
    {
        using var provider = new FileLoggerProvider(Options(_tempDir));

        Directory.Exists(_tempDir).Should().BeTrue();
    }

    // An uninstall deletes the logs folder while the Manager still runs; its last lines used
    // to throw from the host's shutdown.
    [Fact]
    public void Logger_DropsTheLine_WhenTheFolderHasBeenDeleted()
    {
        using var provider = new FileLoggerProvider(Options(_tempDir, "gone"));
        var logger = provider.CreateLogger("Uninstall");
        Directory.Delete(_tempDir, recursive: true);

        var log = () => logger.LogInformation("after the uninstall");

        log.Should().NotThrow();
        Directory.Exists(_tempDir).Should().BeFalse("logging must not bring the deleted folder back");
    }

    [Fact]
    public void Logger_WritesAppendedLineToTodayFile()
    {
        using var provider = new FileLoggerProvider(Options(_tempDir, "smoke"));
        var logger = provider.CreateLogger("cat");

        logger.LogInformation("Hello world {Number}", 42);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var file = Path.Combine(_tempDir, $"smoke-{today}.log");
        File.Exists(file).Should().BeTrue();

        var contents = File.ReadAllText(file);
        contents.Should().Contain("[Information]")
            .And.Contain("cat")
            .And.Contain("Hello world 42");
    }

    [Fact]
    public void Logger_RespectsMinimumLevel()
    {
        using var provider = new FileLoggerProvider(Options(_tempDir, "level", min: LogLevel.Warning));
        var logger = provider.CreateLogger("cat");

        logger.LogInformation("ignored");
        logger.LogWarning("kept");

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var file = Path.Combine(_tempDir, $"level-{today}.log");
        var contents = File.ReadAllText(file);

        contents.Should().NotContain("ignored");
        contents.Should().Contain("kept");
    }

    [Fact]
    public void Logger_AppendsExceptionToOutput()
    {
        using var provider = new FileLoggerProvider(Options(_tempDir, "exc"));
        var logger = provider.CreateLogger("cat");

        logger.LogError(new InvalidOperationException("boom"), "Something failed");

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var file = Path.Combine(_tempDir, $"exc-{today}.log");
        var contents = File.ReadAllText(file);

        contents.Should().Contain("Something failed");
        contents.Should().Contain("InvalidOperationException");
        contents.Should().Contain("boom");
    }

    [Fact]
    public void Ctor_DoesNotPrune_LeavesStaleFilesForBackgroundSweep()
    {
        // v0.4 contract: ctor must NOT touch existing log files. Pruning is deferred to
        // StartBackgroundPrune so the per-process startup is not blocked by enumerate-and-delete.
        Directory.CreateDirectory(_tempDir);
        var stale = Path.Combine(_tempDir, "test-" + DateTime.UtcNow.AddDays(-40).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
        File.WriteAllText(stale, "old");

        using var provider = new FileLoggerProvider(Options(_tempDir, "test", retainDays: 14));

        File.Exists(stale).Should().BeTrue("v0.4 ctor does not prune");
    }

    [Fact]
    public void StartBackgroundPrune_DeletesStaleFiles()
    {
        Directory.CreateDirectory(_tempDir);
        var stale = Path.Combine(_tempDir, "test-" + DateTime.UtcNow.AddDays(-40).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
        var fresh = Path.Combine(_tempDir, "test-" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
        File.WriteAllText(stale, "old");
        File.WriteAllText(fresh, "new");

        using var provider = new FileLoggerProvider(Options(_tempDir, "test", retainDays: 14));
        provider.StartBackgroundPrune();

        // Background task — poll up to 30 s for the deletion to land. CI runners with cold
        // ThreadPool can take >2 s to schedule the Task.Run lambda, and under runner
        // contention (parallel workflow bursts) even a 10 s deadline has flaked.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (File.Exists(stale) && DateTime.UtcNow < deadline)
        {
            System.Threading.Thread.Sleep(50);
        }

        File.Exists(stale).Should().BeFalse("background prune must remove stale files");
        File.Exists(fresh).Should().BeTrue();
    }

    [Fact]
    public void StartBackgroundPrune_KeepsAllFiles_WhenRetainDaysIsZeroOrNegative()
    {
        Directory.CreateDirectory(_tempDir);
        var ancient = Path.Combine(_tempDir, "test-2000-01-01.log");
        File.WriteAllText(ancient, "ancient");

        using var provider = new FileLoggerProvider(Options(_tempDir, "test", retainDays: 0));
        provider.StartBackgroundPrune();

        // Settle window — prune is no-op so the file must remain.
        System.Threading.Thread.Sleep(100);

        File.Exists(ancient).Should().BeTrue();
    }

    [Fact]
    public void StartBackgroundPrune_IgnoresFilesThatDoNotMatchTheDatePattern()
    {
        Directory.CreateDirectory(_tempDir);
        var stray = Path.Combine(_tempDir, "test-not-a-date.log");
        File.WriteAllText(stray, "weird");

        using var provider = new FileLoggerProvider(Options(_tempDir, "test"));
        provider.StartBackgroundPrune();

        System.Threading.Thread.Sleep(100);

        File.Exists(stray).Should().BeTrue();
    }
}
