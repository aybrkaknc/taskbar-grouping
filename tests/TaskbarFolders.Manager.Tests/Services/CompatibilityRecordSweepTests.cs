using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.TestSupport;
using Xunit;

namespace TaskbarFolders.Manager.Tests.Services;

/// <summary>
/// The script that removes the records Windows writes when the uninstall's own processes exit,
/// and, for a portable copy, also removes the copy's own folders.
/// </summary>
public sealed class CompatibilityRecordSweepTests
{
    private const string Root = @"C:\Users\u\AppData\Local\Programs\TaskbarFolders";

    [Fact]
    public void RecordPaths_NameTheThreeExecutablesThatRunDuringAnUninstall()
    {
        CompatibilityRecordSweep.RecordPaths(Root + @"\").Should().Equal(
            Root + @"\Manager\TaskbarFolders.Manager.exe",
            Root + @"\Launcher\TaskbarFolders.Launcher.exe",
            Root + @"\unins000.exe");
    }

    [Fact]
    public void BuildScript_DeletesEachRecordFromEachKey()
    {
        var script = CompatibilityRecordSweep.BuildScript(Root);

        foreach (var key in Uninstaller.WindowsRecordKeys)
        {
            foreach (var path in CompatibilityRecordSweep.RecordPaths(Root))
            {
                script.Should().Contain($"%SystemRoot%\\System32\\reg.exe delete \"HKCU\\{key}\" /v \"{path}\" /f");
            }
        }
    }

    /// <summary>
    /// Measured on 2026-10-29: Windows wrote <c>unins000.exe</c>'s record 3.4 s after it exited,
    /// and a single pass 3 s after the exit beat it by under a second. So the sweep runs twice,
    /// the second time well after any delay seen.
    /// </summary>
    [Fact]
    public void BuildScript_SweepsTwice_FirstAfterTheSettleDelay_ThenAgainLater()
    {
        var script = CompatibilityRecordSweep.BuildScript(Root);
        var line = $"%SystemRoot%\\System32\\reg.exe delete \"HKCU\\{Uninstaller.WindowsRecordKeys[^1]}\" /v \"{Root}\\unins000.exe\" /f";

        var first = script.IndexOf(line, StringComparison.Ordinal);
        var second = script.IndexOf(line, first + 1, StringComparison.Ordinal);
        second.Should().BeGreaterThan(first, "a record written after the first pass must still go");
        script.LastIndexOf(line, StringComparison.Ordinal).Should().Be(second, "exactly two passes");

        var settle = $"ping.exe -n {CompatibilityRecordSweep.FirstPassSeconds + 1} 127.0.0.1";
        var gap = $"ping.exe -n {CompatibilityRecordSweep.SecondPassSeconds - CompatibilityRecordSweep.FirstPassSeconds + 1} 127.0.0.1";
        script.IndexOf(settle, StringComparison.Ordinal).Should().BeInRange(0, first);
        script.IndexOf(gap, first, StringComparison.Ordinal).Should().BeInRange(first, second);
        CompatibilityRecordSweep.FirstPassSeconds.Should().BeGreaterThanOrEqualTo(3);
        CompatibilityRecordSweep.SecondPassSeconds.Should().BeGreaterThan(CompatibilityRecordSweep.FirstPassSeconds);
    }

    [Fact]
    public void BuildScript_WaitsForBothProcessesBeforeTheFirstDelete()
    {
        var script = CompatibilityRecordSweep.BuildScript(Root);

        var firstDelete = script.IndexOf("reg.exe delete", StringComparison.Ordinal);
        firstDelete.Should().BeGreaterThan(0);
        script.IndexOf("TaskbarFolders.Manager.exe\" /nh", StringComparison.Ordinal).Should().BeInRange(0, firstDelete);
        script.IndexOf("unins000.exe\" /nh", StringComparison.Ordinal).Should().BeInRange(0, firstDelete);
    }

    [Fact]
    public void BuildScript_GivesUpWaitingAfterTheTimeout_AndRemovesItselfLast()
    {
        var script = CompatibilityRecordSweep.BuildScript(Root);

        script.Should().Contain(CompatibilityRecordSweep.WaitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        script.TrimEnd().Split('\n').Last().Should().Contain("del \"%~f0\"");
    }

    [Fact]
    public void BuildScript_QuotesAPathWithSpacesAndAmpersands_AndDoublesPercentSigns()
    {
        var script = CompatibilityRecordSweep.BuildScript(@"C:\Users\A & B\100%\TaskbarFolders");

        script.Should().Contain(@"/v ""C:\Users\A & B\100%%\TaskbarFolders\unins000.exe"" /f");
    }

    [Fact]
    public void BuildScript_RefusesARootThatCannotBeQuoted()
    {
        var act = () => CompatibilityRecordSweep.BuildScript("C:\\a\"b");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildScript_AlsoDeletesTheMuiCacheNamesWindowsAppendsToAPath()
    {
        var script = CompatibilityRecordSweep.BuildScript(Root);
        var muiCache = Uninstaller.WindowsRecordKeys[0];
        var manager = Root + @"\Manager\TaskbarFolders.Manager.exe";

        foreach (var suffix in new[] { ".FriendlyAppName", ".ApplicationCompany" })
        {
            script.Should().Contain($"reg.exe delete \"HKCU\\{muiCache}\" /v \"{manager}{suffix}\" /f");
        }

        script.Should().NotContain($"HKCU\\{Uninstaller.WindowsRecordKeys[^1]}\" /v \"{manager}.FriendlyAppName\"", "only MuiCache appends a suffix");
    }

    [Fact]
    public void BuildScript_ForAnInstalledCopy_DeletesNoFolders()
    {
        CompatibilityRecordSweep.BuildScript(Root).Should().NotContain("rd ", "Inno's uninstaller removes the program folder");
    }

    private const string AppData = @"C:\Users\u\AppData\Roaming\TaskbarFolders";

    [Fact]
    public void BuildPortableScript_DeletesTheFolders_TheUnpackedRuntime_AndTheLastLogLines_InBothPasses()
    {
        var script = CompatibilityRecordSweep.BuildPortableScript(Root, AppData);

        foreach (var folder in new[]
        {
            $"rd /s /q \"{Root}\\Manager\"",
            $"rd /s /q \"{Root}\\Launcher\"",
            "rd /s /q \"%TEMP%\\.net\\TaskbarFolders.Manager\"",
            "rd /s /q \"%TEMP%\\.net\\TaskbarFolders.Launcher\"",
            $"rd /s /q \"{AppData}\"",
        })
        {
            script.Split(folder).Length.Should().Be(3, $"{folder} runs in both passes");
        }

        var wait = script.IndexOf(":sweep", StringComparison.Ordinal);
        script.IndexOf("rd /s /q", StringComparison.Ordinal).Should().BeGreaterThan(wait, "nothing is deleted while the Manager still runs");
    }

    [Fact]
    public void BuildPortableScript_RemovesTheExtractedFolderOnlyIfEmpty_AndItselfLast()
    {
        var lines = CompatibilityRecordSweep.BuildPortableScript(Root, AppData).TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        lines[^2].Should().Be($"rd \"{Root}\" 2>nul", "no /s: a folder that holds the user's own files stays");
        lines[^1].Should().Contain("del \"%~f0\"");
        lines.Should().NotContain(l => l.StartsWith($"rd /s /q \"{Root}\"", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPortableScript_QuotesPathsLikeTheInstalledOne()
    {
        var script = CompatibilityRecordSweep.BuildPortableScript(@"C:\A & B\100%\TaskbarFolders", @"C:\100%\data");

        script.Should().Contain(@"rd /s /q ""C:\A & B\100%%\TaskbarFolders\Manager""").And.Contain(@"rd /s /q ""C:\100%%\data""");
        var act = () => CompatibilityRecordSweep.BuildPortableScript(Root, "C:\\a\"b");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheRepositoryNoLongerShipsAnUninstallScript()
    {
        File.Exists(Path.Combine(RepoRoot.Path, "installer", "Uninstall.cmd")).Should().BeFalse(
            "the Manager's helper does what the script did, without a window the user has to wait for");
    }
}
